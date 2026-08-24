using System.Text;
using System.Text.Json;

namespace BambuLab.X1Camera.Probe;

internal sealed class ProbeSettings(string host, string accessCode, string? certificateSha256, int port)
{
    public string Host { get; } = host;

    public string AccessCode { get; } = accessCode;

    public string? CertificateSha256 { get; } = certificateSha256;

    public int Port { get; } = port;

    public static bool TryFromEnvironment(out ProbeSettings settings)
    {
        var host = Environment.GetEnvironmentVariable("BAMBU_X1_HOST");
        var accessCode = Environment.GetEnvironmentVariable("BAMBU_X1_ACCESS_CODE");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(accessCode))
        {
            settings = null!;
            return false;
        }

        settings = new ProbeSettings(
            host,
            accessCode,
            Environment.GetEnvironmentVariable("BAMBU_X1_CERT_SHA256"),
            322);
        return true;
    }

    public override string ToString() => nameof(ProbeSettings);
}

internal static class ProbeSettingsStore
{
    private const int CurrentFormatVersion = 1;
    private const string EncodingName = "base64-utf8";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "bambu-x1-camera.settings.json");

    public static async Task<ProbeSettings> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var document = await JsonSerializer.DeserializeAsync<EncodedProbeSettingsDocument>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The saved settings file is empty.");
        if (document.FormatVersion != CurrentFormatVersion || !string.Equals(document.Encoding, EncodingName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved settings format or encoding is not supported.");
        }

        var host = DecodeRequired(document.Host, "host");
        var accessCode = DecodeRequired(document.AccessCode, "access code");
        var certificateSha256 = DecodeOptional(document.CertificateSha256, "certificate pin");
        if (document.Port is < 1 or > 65535)
        {
            throw new InvalidDataException("The saved settings contain an invalid port.");
        }

        return new ProbeSettings(host, accessCode, certificateSha256, document.Port);
    }

    public static async Task SaveAsync(string path, ProbeSettings settings, CancellationToken cancellationToken)
    {
        var document = new EncodedProbeSettingsDocument
        {
            FormatVersion = CurrentFormatVersion,
            Encoding = EncodingName,
            Host = Encode(settings.Host),
            AccessCode = Encode(settings.AccessCode),
            CertificateSha256 = EncodeOptional(settings.CertificateSha256),
            Port = settings.Port
        };
        await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static string Encode(string value) => Convert.ToBase64String(StrictUtf8.GetBytes(value));

    private static string? EncodeOptional(string? value) => value is null ? null : Encode(value);

    private static string DecodeRequired(string? value, string field)
    {
        var decoded = DecodeOptional(value, field);
        return string.IsNullOrWhiteSpace(decoded) ? throw new InvalidDataException($"The saved {field} is empty.") : decoded;
    }

    private static string? DecodeOptional(string? value, string field)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return StrictUtf8.GetString(Convert.FromBase64String(value));
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            throw new InvalidDataException($"The saved {field} is not valid Base64-encoded UTF-8.", exception);
        }
    }

    private sealed class EncodedProbeSettingsDocument
    {
        public int FormatVersion { get; init; }

        public string? Encoding { get; init; }

        public string? Host { get; init; }

        public string? AccessCode { get; init; }

        public string? CertificateSha256 { get; init; }

        public int Port { get; init; }
    }
}
