using System.Buffers;
using System.Buffers.Binary;

namespace BambuLab.X1Camera.Protocol;

internal sealed class H264RtpDepacketizer
{
    private static readonly byte[] AnnexBStartCode = [0, 0, 0, 1];
    private ArrayBufferWriter<byte>? fragmentedNal;
    private uint fragmentedTimestamp;
    private ushort lastFragmentSequence;

    public IReadOnlyList<H264NalUnit> Push(RtpPacket packet)
    {
        var payload = packet.Payload.Span;
        if (payload.IsEmpty)
        {
            return [];
        }

        var nalType = payload[0] & 0x1f;
        return nalType switch
        {
            >= 1 and <= 23 => [CreateNal(payload, packet)],
            24 => ParseStapA(payload, packet),
            28 => ParseFuA(payload, packet),
            _ => []
        };
    }

    private static H264NalUnit CreateNal(ReadOnlySpan<byte> payload, RtpPacket packet)
    {
        var data = GC.AllocateUninitializedArray<byte>(AnnexBStartCode.Length + payload.Length);
        AnnexBStartCode.CopyTo(data, 0);
        payload.CopyTo(data.AsSpan(AnnexBStartCode.Length));
        return new H264NalUnit(data, packet.Timestamp, packet.SequenceNumber, packet.Marker);
    }

    private static IReadOnlyList<H264NalUnit> ParseStapA(ReadOnlySpan<byte> payload, RtpPacket packet)
    {
        var units = new List<H264NalUnit>();
        var offset = 1;
        while (offset + 2 <= payload.Length)
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(offset, 2));
            offset += 2;
            if (length == 0 || offset + length > payload.Length)
            {
                return [];
            }

            var isLast = offset + length == payload.Length;
            var effectivePacket = packet with { Marker = packet.Marker && isLast };
            units.Add(CreateNal(payload.Slice(offset, length), effectivePacket));
            offset += length;
        }

        return offset == payload.Length ? units : [];
    }

    private IReadOnlyList<H264NalUnit> ParseFuA(ReadOnlySpan<byte> payload, RtpPacket packet)
    {
        if (payload.Length < 3)
        {
            ResetFragment();
            return [];
        }

        var fuHeader = payload[1];
        var isStart = (fuHeader & 0x80) != 0;
        var isEnd = (fuHeader & 0x40) != 0;
        var originalNalHeader = (byte)((payload[0] & 0xe0) | (fuHeader & 0x1f));

        if (isStart)
        {
            fragmentedNal = new ArrayBufferWriter<byte>(payload.Length * 2);
            fragmentedNal.Write(AnnexBStartCode);
            fragmentedNal.Write([originalNalHeader]);
            fragmentedNal.Write(payload[2..]);
            fragmentedTimestamp = packet.Timestamp;
            lastFragmentSequence = packet.SequenceNumber;
        }
        else
        {
            var expectedSequence = unchecked((ushort)(lastFragmentSequence + 1));
            if (fragmentedNal is null || packet.Timestamp != fragmentedTimestamp || packet.SequenceNumber != expectedSequence)
            {
                ResetFragment();
                return [];
            }

            fragmentedNal.Write(payload[2..]);
            lastFragmentSequence = packet.SequenceNumber;
        }

        if (!isEnd || fragmentedNal is null)
        {
            return [];
        }

        var data = fragmentedNal.WrittenSpan.ToArray();
        ResetFragment();
        return [new H264NalUnit(data, packet.Timestamp, packet.SequenceNumber, packet.Marker)];
    }

    private void ResetFragment()
    {
        fragmentedNal = null;
        fragmentedTimestamp = 0;
        lastFragmentSequence = 0;
    }
}
