using System.Globalization;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using BambuLab.X1Camera.Protocol;

namespace BambuLab.X1Camera;

/// <summary>Connects to an X1-series printer's LAN camera without Bambu proprietary libraries.</summary>
public sealed class X1CameraClient
{
    /// <summary>Opens and authenticates an RTSPS/RTP/H.264 camera session.</summary>
    public async ValueTask<X1CameraSession> ConnectAsync(X1CameraOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var streamUri = options.GetStreamUri();
        var connection = await RtspConnection.ConnectAsync(options, cancellationToken).ConfigureAwait(false);

        try
        {
            var describeHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Accept"] = "application/sdp"
            };
            var describe = await connection.SendAndReceiveAsync("DESCRIBE", streamUri, describeHeaders, cancellationToken).ConfigureAwait(false);
            if (describe.StatusCode == 401)
            {
                var challenge = describe.GetHeader("WWW-Authenticate")
                    ?? throw new InvalidDataException("The printer rejected authentication without returning a challenge.");
                connection.Authenticator = RtspAuthenticator.Parse(challenge, "bblp", options.AccessCode);
                describe = await connection.SendAndReceiveAsync("DESCRIBE", streamUri, describeHeaders, cancellationToken).ConfigureAwait(false);
            }

            EnsureSuccess("DESCRIBE", describe);
            var videoTrack = SdpVideoTrack.Parse(describe.Body);
            var trackUri = ResolveControlUri(streamUri, describe.GetHeader("Content-Base"), videoTrack.Control);

            var setup = await connection.SendAndReceiveAsync("SETUP", trackUri, new Dictionary<string, string>
            {
                ["Transport"] = "RTP/AVP/TCP;unicast;interleaved=0-1"
            }, cancellationToken).ConfigureAwait(false);
            EnsureSuccess("SETUP", setup);

            var session = ParseSession(setup.GetHeader("Session"));
            var play = await connection.SendAndReceiveAsync("PLAY", streamUri, new Dictionary<string, string>
            {
                ["Session"] = session,
                ["Range"] = "npt=0.000-"
            }, cancellationToken).ConfigureAwait(false);
            EnsureSuccess("PLAY", play);

            return new X1CameraSession(connection, streamUri, session, videoTrack, options.KeepAliveInterval);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void EnsureSuccess(string method, RtspResponse response)
    {
        if (response.StatusCode is < 200 or >= 300)
        {
            throw new InvalidOperationException($"The printer rejected RTSP {method} with {response.StatusCode.ToString(CultureInfo.InvariantCulture)} {response.ReasonPhrase}.");
        }
    }

    private static string ParseSession(string? value)
    {
        var session = value?.Split(';', 2, StringSplitOptions.TrimEntries)[0];
        return string.IsNullOrWhiteSpace(session)
            ? throw new InvalidDataException("The printer did not return an RTSP session identifier.")
            : session;
    }

    private static Uri ResolveControlUri(Uri streamUri, string? contentBase, string control)
    {
        if (Uri.TryCreate(control, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        if (!string.IsNullOrWhiteSpace(contentBase) && Uri.TryCreate(contentBase, UriKind.Absolute, out var baseUri))
        {
            return new Uri(baseUri, control);
        }

        if (control.StartsWith("/", StringComparison.Ordinal))
        {
            return new UriBuilder(streamUri) { Path = control, Query = string.Empty }.Uri;
        }

        return new Uri($"{streamUri.AbsoluteUri.TrimEnd('/')}/{control}", UriKind.Absolute);
    }
}

/// <summary>An authenticated X1 camera session that yields encoded H.264 units.</summary>
public sealed class X1CameraSession : IAsyncDisposable
{
    private readonly RtspConnection connection;
    private readonly Uri streamUri;
    private readonly string sessionId;
    private readonly SdpVideoTrack videoTrack;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task keepAliveTask;
    private int reading;
    private int disposed;

    internal X1CameraSession(RtspConnection connection, Uri streamUri, string sessionId, SdpVideoTrack videoTrack, TimeSpan keepAliveInterval)
    {
        this.connection = connection;
        this.streamUri = streamUri;
        this.sessionId = sessionId;
        this.videoTrack = videoTrack;
        keepAliveTask = KeepAliveAsync(keepAliveInterval, lifetime.Token);
    }

    /// <summary>Gets the SDP-advertised RTP payload type.</summary>
    public int PayloadType => videoTrack.PayloadType;

    /// <summary>Gets the SDP-advertised RTP clock rate.</summary>
    public int ClockRate => videoTrack.ClockRate;

    /// <summary>Gets the SDP-advertised sequence parameter set, without an Annex-B prefix.</summary>
    public ReadOnlyMemory<byte> SequenceParameterSet => videoTrack.SequenceParameterSet ?? [];

    /// <summary>Gets the SDP-advertised picture parameter set, without an Annex-B prefix.</summary>
    public ReadOnlyMemory<byte> PictureParameterSet => videoTrack.PictureParameterSet ?? [];

    /// <summary>Reads H.264 NAL units until cancellation, disconnection, or disposal.</summary>
    public async IAsyncEnumerable<H264NalUnit> ReadNalUnitsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.Exchange(ref reading, 1) != 0)
        {
            throw new InvalidOperationException("A camera session supports one active reader. Fan out units in the host application.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var depacketizer = new H264RtpDepacketizer();
        try
        {
            while (!linked.IsCancellationRequested)
            {
                var item = await connection.ReadAsync(linked.Token).ConfigureAwait(false);
                if (item is not RtspInterleavedFrame { Channel: 0 } frame
                    || !RtpPacket.TryParse(frame.Payload, out var packet)
                    || packet.PayloadType != videoTrack.PayloadType)
                {
                    continue;
                }

                foreach (var unit in depacketizer.Push(packet))
                {
                    yield return unit;
                }
            }
        }
        finally
        {
            Volatile.Write(ref reading, 0);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        try
        {
            await keepAliveTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }

        try
        {
            await connection.SendAsync("TEARDOWN", streamUri, new Dictionary<string, string>
            {
                ["Session"] = sessionId
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }

        await connection.DisposeAsync().ConfigureAwait(false);
        lifetime.Dispose();
    }

    private async Task KeepAliveAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        if (interval <= TimeSpan.Zero)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await connection.SendAsync("GET_PARAMETER", streamUri, new Dictionary<string, string>
            {
                ["Session"] = sessionId
            }, cancellationToken).ConfigureAwait(false);
        }
    }
}
