using System.Buffers.Binary;

namespace BambuLab.X1Camera.Protocol;

internal readonly record struct RtpPacket(
    byte PayloadType,
    bool Marker,
    ushort SequenceNumber,
    uint Timestamp,
    uint SynchronizationSource,
    ReadOnlyMemory<byte> Payload)
{
    public static bool TryParse(ReadOnlyMemory<byte> data, out RtpPacket packet)
    {
        packet = default;
        var span = data.Span;
        if (span.Length < 12 || (span[0] >> 6) != 2)
        {
            return false;
        }

        var hasPadding = (span[0] & 0x20) != 0;
        var hasExtension = (span[0] & 0x10) != 0;
        var csrcCount = span[0] & 0x0f;
        var headerLength = 12 + (csrcCount * 4);
        if (span.Length < headerLength)
        {
            return false;
        }

        if (hasExtension)
        {
            if (span.Length < headerLength + 4)
            {
                return false;
            }

            var extensionWords = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(headerLength + 2, 2));
            headerLength += 4 + (extensionWords * 4);
            if (span.Length < headerLength)
            {
                return false;
            }
        }

        var payloadLength = span.Length - headerLength;
        if (hasPadding)
        {
            if (payloadLength == 0)
            {
                return false;
            }

            var paddingLength = span[^1];
            if (paddingLength == 0 || paddingLength > payloadLength)
            {
                return false;
            }

            payloadLength -= paddingLength;
        }

        if (payloadLength <= 0)
        {
            return false;
        }

        packet = new RtpPacket(
            (byte)(span[1] & 0x7f),
            (span[1] & 0x80) != 0,
            BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2, 2)),
            BinaryPrimitives.ReadUInt32BigEndian(span.Slice(4, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8, 4)),
            data.Slice(headerLength, payloadLength));
        return true;
    }
}

