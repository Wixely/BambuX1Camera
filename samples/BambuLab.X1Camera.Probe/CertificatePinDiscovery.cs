using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BambuLab.X1Camera.Probe;

internal static class CertificatePinDiscovery
{
    public static async Task<CertificateObservation> ObserveAsync(string host, int port, CancellationToken cancellationToken)
    {
        byte[]? certificateData = null;
        var policyErrors = SslPolicyErrors.None;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);

        await using var ssl = new SslStream(
            client.GetStream(),
            leaveInnerStreamOpen: false,
            (_, certificate, _, errors) =>
            {
                policyErrors = errors;
                certificateData = certificate?.GetRawCertData();
                return certificate is not null;
            });
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        }, timeout.Token).ConfigureAwait(false);

        if (certificateData is null)
        {
            throw new AuthenticationException("The printer did not present a TLS certificate.");
        }

        return new CertificateObservation(Convert.ToHexString(SHA256.HashData(certificateData)), policyErrors);
    }
}

internal sealed record CertificateObservation(string Sha256, SslPolicyErrors PolicyErrors);
