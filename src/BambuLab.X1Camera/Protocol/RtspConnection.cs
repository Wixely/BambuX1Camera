using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;

namespace BambuLab.X1Camera.Protocol;

internal sealed class RtspConnection : IAsyncDisposable
{
    private readonly TcpClient? tcpClient;
    private readonly Stream stream;
    private readonly RtspWireReader reader;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<RtspResponse>> pendingResponses = new();
    private readonly Channel<RtspInterleavedFrame> frames;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task readLoopTask;
    private int sequence;
    private int aborted;
    private int disposed;
    private long droppedFrames;
    private Exception? terminalFailure;

    private RtspConnection(TcpClient? tcpClient, Stream stream, int frameQueueCapacity)
    {
        this.tcpClient = tcpClient;
        this.stream = stream;
        reader = new RtspWireReader(stream);
        frames = Channel.CreateBounded<RtspInterleavedFrame>(new BoundedChannelOptions(frameQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        readLoopTask = ReadLoopAsync(lifetime.Token);
    }

    public RtspAuthenticator? Authenticator { get; set; }

    public long DroppedFrameCount => Interlocked.Read(ref droppedFrames);

    public static async ValueTask<RtspConnection> ConnectAsync(X1CameraOptions options, CancellationToken cancellationToken)
    {
        var certificatePin = options.GetNormalizedCertificatePin();
        var client = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ConnectTimeout);

        try
        {
            await client.ConnectAsync(options.Host, options.Port, timeout.Token).ConfigureAwait(false);
            var ssl = new SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                (_, certificate, _, policyErrors) => ValidateCertificate(certificate, policyErrors, certificatePin, options.AllowUntrustedCertificate));
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = options.Host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, timeout.Token).ConfigureAwait(false);
            return new RtspConnection(client, ssl, options.FrameQueueCapacity);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    internal static RtspConnection CreateForStream(Stream stream, int frameQueueCapacity = 8)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new RtspConnection(tcpClient: null, stream, frameQueueCapacity);
    }

    public async ValueTask<RtspResponse> SendAndReceiveAsync(
        string method,
        Uri uri,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        var requestSequence = Interlocked.Increment(ref sequence);
        var completion = new TaskCompletionSource<RtspResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pendingResponses.TryAdd(requestSequence, completion))
        {
            throw new InvalidOperationException("The RTSP request sequence was already pending.");
        }

        var failure = Volatile.Read(ref terminalFailure);
        if (failure is not null)
        {
            pendingResponses.TryRemove(requestSequence, out _);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        try
        {
            var request = CreateRequest(method, uri, headers, requestSequence);
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }

            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            pendingResponses.TryRemove(requestSequence, out _);
        }
    }

    public async ValueTask<RtspInterleavedFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await frames.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
        catch (ChannelClosedException)
        {
            throw new EndOfStreamException("The RTSP stream closed unexpectedly.");
        }
    }

    public void Abort(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (Interlocked.Exchange(ref aborted, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        Interlocked.CompareExchange(ref terminalFailure, error, null);
        frames.Writer.TryComplete(error);
        FailPendingResponses(error);
        stream.Dispose();
        tcpClient?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref aborted, 1) == 0)
        {
            lifetime.Cancel();
            frames.Writer.TryComplete();
            FailPendingResponses(new ObjectDisposedException(nameof(RtspConnection)));
        }

        try
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            tcpClient?.Dispose();
        }

        try
        {
            await readLoopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (lifetime.IsCancellationRequested)
        {
        }

        writeLock.Dispose();
        lifetime.Dispose();
    }

    private byte[] CreateRequest(string method, Uri uri, IReadOnlyDictionary<string, string>? headers, int requestSequence)
    {
        var builder = new StringBuilder()
            .Append(method).Append(' ').Append(uri.AbsoluteUri).Append(" RTSP/1.0\r\n")
            .Append("CSeq: ").Append(requestSequence.ToString(CultureInfo.InvariantCulture)).Append("\r\n")
            .Append("User-Agent: BambuLab.X1Camera/0.1\r\n");

        if (Authenticator is not null)
        {
            builder.Append("Authorization: ").Append(Authenticator.CreateAuthorization(method, uri)).Append("\r\n");
        }

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
            }
        }

        builder.Append("Content-Length: 0\r\n\r\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var item = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (item is RtspResponse response)
                {
                    DispatchResponse(response);
                }
                else if (item is RtspInterleavedFrame frame && !frames.Writer.TryWrite(frame))
                {
                    Interlocked.Increment(ref droppedFrames);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
            Interlocked.CompareExchange(ref terminalFailure, exception, null);
        }
        finally
        {
            frames.Writer.TryComplete(failure);
            if (failure is not null)
            {
                FailPendingResponses(failure);
            }
        }
    }

    private void DispatchResponse(RtspResponse response)
    {
        var sequenceText = response.GetHeader("CSeq");
        if (!int.TryParse(sequenceText, NumberStyles.None, CultureInfo.InvariantCulture, out var responseSequence)
            || responseSequence <= 0)
        {
            throw new InvalidDataException("The RTSP response omitted a valid CSeq header.");
        }

        if (pendingResponses.TryRemove(responseSequence, out var completion))
        {
            completion.TrySetResult(response);
        }
    }

    private void FailPendingResponses(Exception exception)
    {
        foreach (var pair in pendingResponses)
        {
            if (pendingResponses.TryRemove(pair.Key, out var completion))
            {
                completion.TrySetException(exception);
            }
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0 || Volatile.Read(ref aborted) != 0, this);
        var failure = Volatile.Read(ref terminalFailure);
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static bool ValidateCertificate(
        X509Certificate? certificate,
        SslPolicyErrors policyErrors,
        string? certificatePin,
        bool allowUntrusted)
    {
        if (certificate is null)
        {
            return false;
        }

        if (certificatePin is not null)
        {
            using var certificate2 = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
            var expected = Convert.FromHexString(certificatePin);
            var actual = certificate2.GetCertHash(HashAlgorithmName.SHA256);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        return allowUntrusted || policyErrors == SslPolicyErrors.None;
    }
}
