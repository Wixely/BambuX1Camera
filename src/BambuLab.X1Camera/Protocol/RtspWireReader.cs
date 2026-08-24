using System.Globalization;
using System.Text;

namespace BambuLab.X1Camera.Protocol;

internal abstract record RtspWireItem;

internal sealed record RtspInterleavedFrame(byte Channel, byte[] Payload) : RtspWireItem;

internal sealed record RtspResponse(
    int StatusCode,
    string ReasonPhrase,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body) : RtspWireItem
{
    public string? GetHeader(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

internal sealed class RtspWireReader(Stream stream)
{
    private const int MaximumHeaderBytes = 64 * 1024;
    private const int MaximumBodyBytes = 1024 * 1024;
    private readonly byte[] oneByte = new byte[1];

    public async ValueTask<RtspWireItem> ReadAsync(CancellationToken cancellationToken)
    {
        var first = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
        if (first == '$')
        {
            return await ReadInterleavedFrameAsync(cancellationToken).ConfigureAwait(false);
        }

        return await ReadResponseAsync(first, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<RtspInterleavedFrame> ReadInterleavedFrameAsync(CancellationToken cancellationToken)
    {
        var header = new byte[3];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = (header[1] << 8) | header[2];
        if (length == 0)
        {
            throw new InvalidDataException("An interleaved RTSP frame had an empty payload.");
        }

        var payload = GC.AllocateUninitializedArray<byte>(length);
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return new RtspInterleavedFrame(header[0], payload);
    }

    private async ValueTask<RtspResponse> ReadResponseAsync(byte first, CancellationToken cancellationToken)
    {
        using var headerBuffer = new MemoryStream();
        headerBuffer.WriteByte(first);
        var matched = first == '\r' ? 1 : 0;
        byte[] terminator = "\r\n\r\n"u8.ToArray();

        while (matched < terminator.Length)
        {
            var value = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            headerBuffer.WriteByte(value);
            if (headerBuffer.Length > MaximumHeaderBytes)
            {
                throw new InvalidDataException("The RTSP response header exceeded the configured limit.");
            }

            matched = value == terminator[matched]
                ? matched + 1
                : value == terminator[0] ? 1 : 0;
        }

        var headerText = Encoding.ASCII.GetString(headerBuffer.GetBuffer(), 0, checked((int)headerBuffer.Length));
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0 || !lines[0].StartsWith("RTSP/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The server returned an invalid RTSP status line.");
        }

        var statusParts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (statusParts.Length < 2 || !int.TryParse(statusParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var statusCode))
        {
            throw new InvalidDataException("The server returned an invalid RTSP status code.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0)
            {
                break;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                throw new InvalidDataException("The server returned a malformed RTSP header.");
            }

            headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        var bodyLength = 0;
        if (headers.TryGetValue("Content-Length", out var contentLengthText)
            && (!int.TryParse(contentLengthText, NumberStyles.None, CultureInfo.InvariantCulture, out bodyLength)
                || bodyLength is < 0 or > MaximumBodyBytes))
        {
            throw new InvalidDataException("The RTSP response contained an invalid Content-Length.");
        }

        var body = bodyLength == 0 ? [] : GC.AllocateUninitializedArray<byte>(bodyLength);
        if (bodyLength > 0)
        {
            await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        }

        return new RtspResponse(statusCode, statusParts.Length == 3 ? statusParts[2] : string.Empty, headers, body);
    }

    private async ValueTask<byte> ReadByteAsync(CancellationToken cancellationToken)
    {
        var count = await stream.ReadAsync(oneByte, cancellationToken).ConfigureAwait(false);
        return count == 0 ? throw new EndOfStreamException("The RTSP stream closed unexpectedly.") : oneByte[0];
    }
}
