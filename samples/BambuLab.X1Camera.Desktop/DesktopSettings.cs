using System.Text;
using System.Text.Json;

namespace BambuLab.X1Camera.Desktop;

internal sealed record DesktopSettings(string Host, string AccessCode, string? CertificateSha256, int Port)
{
    public override string ToString() => nameof(DesktopSettings);
}

internal static class DesktopSettingsStore
{
    private const int CurrentFormatVersion = 1;
    private const string EncodingName = "base64-utf8";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "bambu-x1-camera.settings.json");

    public static async Task<DesktopSettings> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var document = await JsonSerializer.DeserializeAsync<EncodedSettingsDocument>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
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

        return new DesktopSettings(host, accessCode, certificateSha256, document.Port);
    }

    public static async Task SaveAsync(string path, DesktopSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException("The settings path must contain a directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var document = new EncodedSettingsDocument
        {
            FormatVersion = CurrentFormatVersion,
            Encoding = EncodingName,
            Host = Encode(settings.Host),
            AccessCode = Encode(settings.AccessCode),
            CertificateSha256 = EncodeOptional(settings.CertificateSha256),
            Port = settings.Port
        };

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
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

    private sealed class EncodedSettingsDocument
    {
        public int FormatVersion { get; init; }

        public string? Encoding { get; init; }

        public string? Host { get; init; }

        public string? AccessCode { get; init; }

        public string? CertificateSha256 { get; init; }

        public int Port { get; init; }
    }
}
