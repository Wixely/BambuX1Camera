using System.Buffers;

namespace BambuLab.X1Camera.Protocol;

internal sealed class H264AccessUnitAssembler
{
    private static readonly byte[] AnnexBStartCode = [0, 0, 0, 1];
    private readonly int maxBytes;
    private readonly int maxNalUnits;
    private ArrayBufferWriter<byte> data = new();
    private byte[]? sequenceParameterSet;
    private byte[]? pictureParameterSet;
    private uint? currentTimestamp;
    private uint? droppingTimestamp;
    private TimeSpan currentPresentationTime;
    private ushort lastSequenceNumber;
    private int nalUnitCount;
    private bool isKeyFrame;
    private bool hasSequenceParameterSet;
    private bool hasPictureParameterSet;
    private bool nextIsDiscontinuity = true;
    private H264RtpTimestampNormalizer timestampNormalizer;

    public H264AccessUnitAssembler(
        int clockRate,
        int maxBytes,
        int maxNalUnits,
        ReadOnlyMemory<byte> initialSequenceParameterSet,
        ReadOnlyMemory<byte> initialPictureParameterSet)
    {
        this.maxBytes = maxBytes;
        this.maxNalUnits = maxNalUnits;
        timestampNormalizer = new H264RtpTimestampNormalizer(clockRate);
        UpdateParameterSets(initialSequenceParameterSet, initialPictureParameterSet);
    }

    public H264AccessUnit? Push(H264NalUnit unit)
    {
        UpdateCachedParameterSet(unit);

        if (droppingTimestamp is { } dropped)
        {
            if (unit.RtpTimestamp == dropped && !unit.Marker)
            {
                return null;
            }

            droppingTimestamp = null;
            if (unit.RtpTimestamp == dropped)
            {
                return null;
            }
        }

        if (currentTimestamp is { } timestamp && timestamp != unit.RtpTimestamp)
        {
            ResetPending();
            nextIsDiscontinuity = true;
        }

        if (currentTimestamp is null)
        {
            currentTimestamp = unit.RtpTimestamp;
            if (!timestampNormalizer.TryNormalize(unit.RtpTimestamp, out currentPresentationTime))
            {
                timestampNormalizer.Reset();
                _ = timestampNormalizer.TryNormalize(unit.RtpTimestamp, out currentPresentationTime);
                nextIsDiscontinuity = true;
            }
        }

        var proposedCount = nalUnitCount + 1;
        var proposedBytes = data.WrittenCount + unit.AnnexBData.Length;
        if (proposedCount > maxNalUnits || proposedBytes > maxBytes)
        {
            droppingTimestamp = unit.Marker ? null : unit.RtpTimestamp;
            ResetPending();
            nextIsDiscontinuity = true;
            return null;
        }

        data.Write(unit.AnnexBData.Span);
        nalUnitCount = proposedCount;
        lastSequenceNumber = unit.LastSequenceNumber;
        isKeyFrame |= unit.IsKeyFrame;
        hasSequenceParameterSet |= unit.Type == H264NalUnitType.SequenceParameterSet;
        hasPictureParameterSet |= unit.Type == H264NalUnitType.PictureParameterSet;

        return unit.Marker ? Complete() : null;
    }

    public void Reset(
        int clockRate,
        ReadOnlyMemory<byte> initialSequenceParameterSet,
        ReadOnlyMemory<byte> initialPictureParameterSet)
    {
        ResetPending();
        droppingTimestamp = null;
        timestampNormalizer = new H264RtpTimestampNormalizer(clockRate);
        UpdateParameterSets(initialSequenceParameterSet, initialPictureParameterSet);
        nextIsDiscontinuity = true;
    }

    private H264AccessUnit? Complete()
    {
        var injectSequenceParameterSet = isKeyFrame && !hasSequenceParameterSet ? sequenceParameterSet : null;
        var injectPictureParameterSet = isKeyFrame && !hasPictureParameterSet ? pictureParameterSet : null;
        var injectedCount = (injectSequenceParameterSet is null ? 0 : 1) + (injectPictureParameterSet is null ? 0 : 1);
        var completedBytes = data.WrittenCount
            + (injectSequenceParameterSet?.Length ?? 0)
            + (injectPictureParameterSet?.Length ?? 0);
        if (completedBytes > maxBytes || nalUnitCount + injectedCount > maxNalUnits)
        {
            ResetPending();
            nextIsDiscontinuity = true;
            return null;
        }

        var output = GC.AllocateUninitializedArray<byte>(completedBytes);
        var offset = 0;
        Copy(injectSequenceParameterSet, output, ref offset);
        Copy(injectPictureParameterSet, output, ref offset);
        data.WrittenSpan.CopyTo(output.AsSpan(offset));

        var result = new H264AccessUnit(
            output,
            currentTimestamp!.Value,
            currentPresentationTime,
            lastSequenceNumber,
            nalUnitCount + injectedCount,
            isKeyFrame,
            hasSequenceParameterSet || injectSequenceParameterSet is not null,
            hasPictureParameterSet || injectPictureParameterSet is not null,
            nextIsDiscontinuity);
        ResetPending();
        nextIsDiscontinuity = false;
        return result;
    }

    private void UpdateCachedParameterSet(H264NalUnit unit)
    {
        if (unit.Type == H264NalUnitType.SequenceParameterSet)
        {
            sequenceParameterSet = unit.AnnexBData.ToArray();
        }
        else if (unit.Type == H264NalUnitType.PictureParameterSet)
        {
            pictureParameterSet = unit.AnnexBData.ToArray();
        }
    }

    private void UpdateParameterSets(ReadOnlyMemory<byte> sps, ReadOnlyMemory<byte> pps)
    {
        if (!sps.IsEmpty)
        {
            sequenceParameterSet = AddStartCode(sps.Span);
        }

        if (!pps.IsEmpty)
        {
            pictureParameterSet = AddStartCode(pps.Span);
        }
    }

    private void ResetPending()
    {
        data = new ArrayBufferWriter<byte>();
        currentTimestamp = null;
        currentPresentationTime = TimeSpan.Zero;
        lastSequenceNumber = 0;
        nalUnitCount = 0;
        isKeyFrame = false;
        hasSequenceParameterSet = false;
        hasPictureParameterSet = false;
    }

    private static byte[] AddStartCode(ReadOnlySpan<byte> parameterSet)
    {
        var result = GC.AllocateUninitializedArray<byte>(AnnexBStartCode.Length + parameterSet.Length);
        AnnexBStartCode.CopyTo(result, 0);
        parameterSet.CopyTo(result.AsSpan(AnnexBStartCode.Length));
        return result;
    }

    private static void Copy(byte[]? source, byte[] destination, ref int offset)
    {
        if (source is null)
        {
            return;
        }

        source.CopyTo(destination, offset);
        offset += source.Length;
    }
}

internal sealed class H264RtpTimestampNormalizer(int clockRate)
{
    private uint? previous;
    private long elapsedClockTicks;

    public bool TryNormalize(uint timestamp, out TimeSpan presentationTime)
    {
        if (previous is { } last)
        {
            var delta = unchecked((int)(timestamp - last));
            if (delta < 0)
            {
                presentationTime = default;
                return false;
            }

            elapsedClockTicks += delta;
        }

        previous = timestamp;
        var seconds = elapsedClockTicks / clockRate;
        var remainder = elapsedClockTicks % clockRate;
        presentationTime = TimeSpan.FromTicks(
            checked((seconds * TimeSpan.TicksPerSecond) + ((remainder * TimeSpan.TicksPerSecond) / clockRate)));
        return true;
    }

    public void Reset()
    {
        previous = null;
        elapsedClockTicks = 0;
    }
}
