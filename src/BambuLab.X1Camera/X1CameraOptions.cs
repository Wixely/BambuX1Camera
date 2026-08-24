using System.Diagnostics;

namespace BambuLab.X1Camera;

/// <summary>Connection and security settings for an X1-series LAN camera.</summary>
[DebuggerDisplay("{Host}:{Port}{StreamPath}")]
public sealed class X1CameraOptions
{
    /// <summary>Creates options for a printer host and LAN access code.</summary>
    public X1CameraOptions(string host, string accessCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessCode);

        Host = host;
        AccessCode = accessCode;
    }

    /// <summary>Gets the printer DNS name or IP address.</summary>
    public string Host { get; }

    /// <summary>Gets the sensitive LAN access code. Applications must not log this value.</summary>
    public string AccessCode { get; }

    /// <summary>Gets or sets the RTSPS port.</summary>
    public int Port { get; init; } = 322;

    /// <summary>Gets or sets the RTSP stream path.</summary>
    public string StreamPath { get; init; } = "/streaming/live/1";

    /// <summary>
    /// Gets or sets an optional SHA-256 server-certificate fingerprint. Separators and case are ignored.
    /// When supplied, this pin is accepted even when the printer certificate is not trusted by the OS.
    /// </summary>
    public string? PinnedCertificateSha256 { get; init; }

    /// <summary>
    /// Gets or sets whether to accept any server certificate. This is unsafe and intended only for an
    /// isolated first-contact diagnostic. It defaults to <see langword="false"/>.
    /// </summary>
    public bool AllowUntrustedCertificate { get; init; }

    /// <summary>Gets or sets the TCP connection timeout.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the maximum RTSP keepalive interval. When the server advertises a shorter session
    /// timeout, the client uses half that timeout. Set to zero to disable keepalive requests.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets or sets the maximum number of automatic reconnection attempts after an established stream fails.</summary>
    public int ReconnectAttempts { get; init; } = 3;

    /// <summary>Gets or sets the delay before each automatic reconnection attempt.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets or sets the bounded number of interleaved RTSP frames buffered before new frames are dropped.</summary>
    public int FrameQueueCapacity { get; init; } = 256;

    /// <summary>Gets or sets the maximum encoded size of one assembled H.264 access unit.</summary>
    public int MaxAccessUnitBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Gets or sets the maximum number of NAL units in one assembled H.264 access unit.</summary>
    public int MaxNalUnitsPerAccessUnit { get; init; } = 1024;

    /// <summary>Gets or sets the maximum time allowed for an acknowledged RTSP teardown during disposal.</summary>
    public TimeSpan TeardownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    internal Uri GetStreamUri()
    {
        Validate();
        return new UriBuilder(Uri.UriSchemeHttps, Host, Port, StreamPath)
        {
            Scheme = "rtsps"
        }.Uri;
    }

    internal void Validate()
    {
        if (Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port));
        }

        if (!StreamPath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("The stream path must start with '/'.", nameof(StreamPath));
        }

        if (ConnectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout));
        }

        if (KeepAliveInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(KeepAliveInterval));
        }

        if (ReconnectAttempts is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(ReconnectAttempts));
        }

        if (ReconnectDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ReconnectDelay));
        }

        if (FrameQueueCapacity is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(FrameQueueCapacity));
        }

        if (MaxAccessUnitBytes is < 1024 or > 64 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAccessUnitBytes));
        }

        if (MaxNalUnitsPerAccessUnit is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxNalUnitsPerAccessUnit));
        }

        if (TeardownTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(TeardownTimeout));
        }
    }

    internal string? GetNormalizedCertificatePin()
    {
        if (string.IsNullOrWhiteSpace(PinnedCertificateSha256))
        {
            return null;
        }

        var normalized = string.Concat(PinnedCertificateSha256.Where(Uri.IsHexDigit)).ToUpperInvariant();
        if (normalized.Length != 64)
        {
            throw new ArgumentException("The SHA-256 certificate fingerprint must contain 64 hexadecimal characters.", nameof(PinnedCertificateSha256));
        }

        return normalized;
    }

    /// <inheritdoc />
    public override string ToString() => $"rtsps://{Host}:{Port}{StreamPath}";
}
