using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace BambuLab.X1Camera.Protocol;

internal sealed class RtspConnection : IAsyncDisposable
{
    private readonly TcpClient tcpClient;
    private readonly SslStream stream;
    private readonly RtspWireReader reader;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private int sequence;

    private RtspConnection(TcpClient tcpClient, SslStream stream)
    {
        this.tcpClient = tcpClient;
        this.stream = stream;
        reader = new RtspWireReader(stream);
    }

    public RtspAuthenticator? Authenticator { get; set; }

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
            return new RtspConnection(client, ssl);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async ValueTask<RtspResponse> SendAndReceiveAsync(
        string method,
        Uri uri,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        await SendAsync(method, uri, headers, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var item = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (item is RtspResponse response)
            {
                return response;
            }
        }
    }

    public async ValueTask SendAsync(
        string method,
        Uri uri,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        var requestSequence = Interlocked.Increment(ref sequence);
        var builder = new StringBuilder()
            .Append(method).Append(' ').Append(uri.AbsoluteUri).Append(" RTSP/1.0\r\n")
            .Append("CSeq: ").Append(requestSequence).Append("\r\n")
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
        var request = Encoding.ASCII.GetBytes(builder.ToString());
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
    }

    public ValueTask<RtspWireItem> ReadAsync(CancellationToken cancellationToken) => reader.ReadAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        writeLock.Dispose();
        await stream.DisposeAsync().ConfigureAwait(false);
        tcpClient.Dispose();
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

