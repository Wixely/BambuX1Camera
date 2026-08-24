using H264Sharp.Decoder;
using H264Sharp.Decoder.Picture;

namespace BambuLab.X1Camera.Imaging;

/// <summary>Decodes a bounded H.264 group of pictures into display-order RGB24 frames.</summary>
public sealed class X1CameraGopDecoder
{
    private const int MaximumAccessUnits = 512;
    private const int MaximumEncodedBytes = 64 * 1024 * 1024;

    /// <summary>Decodes access units beginning with a complete SPS/PPS-equipped IDR.</summary>
    public IReadOnlyList<X1CameraDecodedFrame> Decode(IReadOnlyList<H264AccessUnit> accessUnits)
    {
        ArgumentNullException.ThrowIfNull(accessUnits);
        if (accessUnits.Count is < 1 or > MaximumAccessUnits)
        {
            throw new ArgumentOutOfRangeException(nameof(accessUnits));
        }

        var first = accessUnits[0] ?? throw new ArgumentException("The access-unit collection contains null.", nameof(accessUnits));
        if (!first.IsKeyFrame || !first.HasSequenceParameterSet || !first.HasPictureParameterSet)
        {
            throw new ArgumentException("A GOP must begin with an SPS/PPS-equipped IDR access unit.", nameof(accessUnits));
        }

        var encodedLength = 0;
        foreach (var accessUnit in accessUnits)
        {
            ArgumentNullException.ThrowIfNull(accessUnit);
            encodedLength = checked(encodedLength + accessUnit.AnnexBData.Length);
            if (encodedLength > MaximumEncodedBytes)
            {
                throw new ArgumentException("The encoded GOP exceeds the 64 MiB decoder limit.", nameof(accessUnits));
            }
        }

        var encoded = new byte[encodedLength];
        var offset = 0;
        foreach (var accessUnit in accessUnits)
        {
            accessUnit.AnnexBData.Span.CopyTo(encoded.AsSpan(offset));
            offset += accessUnit.AnnexBData.Length;
        }

        var pictures = new H264FrameDecoder().DecodeAllFrames(encoded);
        if (pictures.Count == 0)
        {
            throw new InvalidDataException("H264Sharp did not decode a displayable frame from the GOP.");
        }

        var timings = accessUnits
            .OrderBy(accessUnit => accessUnit.PresentationTime)
            .Select(accessUnit => (accessUnit.RtpTimestamp, accessUnit.PresentationTime))
            .ToArray();
        var fallbackInterval = EstimateFrameInterval(timings);
        var frames = new X1CameraDecodedFrame[pictures.Count];
        for (var index = 0; index < pictures.Count; index++)
        {
            var picture = pictures[index];
            var timingIndex = Math.Min(index, timings.Length - 1);
            var timing = timings[timingIndex];
            var presentationTime = index < timings.Length
                ? timing.PresentationTime
                : timings[^1].PresentationTime + TimeSpan.FromTicks(fallbackInterval.Ticks * (index - timings.Length + 1L));
            frames[index] = new X1CameraDecodedFrame(
                YuvToRgb.Convert(picture, picture.Vui),
                picture.Width,
                picture.Height,
                timing.RtpTimestamp,
                presentationTime);
        }

        return frames;
    }

    private static TimeSpan EstimateFrameInterval((uint RtpTimestamp, TimeSpan PresentationTime)[] timings)
    {
        if (timings.Length < 2)
        {
            return TimeSpan.FromMilliseconds(40);
        }

        var intervals = new List<long>(timings.Length - 1);
        for (var index = 1; index < timings.Length; index++)
        {
            var ticks = (timings[index].PresentationTime - timings[index - 1].PresentationTime).Ticks;
            if (ticks > 0)
            {
                intervals.Add(ticks);
            }
        }

        if (intervals.Count == 0)
        {
            return TimeSpan.FromMilliseconds(40);
        }

        intervals.Sort();
        return TimeSpan.FromTicks(intervals[intervals.Count / 2]);
    }
}
