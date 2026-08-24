namespace BambuLab.X1Camera;

/// <summary>A complete encoded H.264 access unit suitable for decoding, recording, or restreaming.</summary>
public sealed class H264AccessUnit
{
    internal H264AccessUnit(
        byte[] annexBData,
        uint rtpTimestamp,
        TimeSpan presentationTime,
        ushort lastSequenceNumber,
        int nalUnitCount,
        bool isKeyFrame,
        bool hasSequenceParameterSet,
        bool hasPictureParameterSet,
        bool discontinuity)
    {
        AnnexBData = annexBData;
        RtpTimestamp = rtpTimestamp;
        PresentationTime = presentationTime;
        LastSequenceNumber = lastSequenceNumber;
        NalUnitCount = nalUnitCount;
        IsKeyFrame = isKeyFrame;
        HasSequenceParameterSet = hasSequenceParameterSet;
        HasPictureParameterSet = hasPictureParameterSet;
        Discontinuity = discontinuity;
    }

    /// <summary>Gets every NAL unit in the access unit using Annex-B start-code framing.</summary>
    public ReadOnlyMemory<byte> AnnexBData { get; }

    /// <summary>Gets the original 32-bit RTP timestamp.</summary>
    public uint RtpTimestamp { get; }

    /// <summary>Gets the presentation time normalized to the first access unit after a discontinuity.</summary>
    public TimeSpan PresentationTime { get; }

    /// <summary>Gets the last RTP sequence number contributing to this access unit.</summary>
    public ushort LastSequenceNumber { get; }

    /// <summary>Gets the number of NAL units in <see cref="AnnexBData"/>, including injected parameter sets.</summary>
    public int NalUnitCount { get; }

    /// <summary>Gets whether this access unit contains an IDR coded slice.</summary>
    public bool IsKeyFrame { get; }

    /// <summary>Gets whether this access unit contains a sequence parameter set.</summary>
    public bool HasSequenceParameterSet { get; }

    /// <summary>Gets whether this access unit contains a picture parameter set.</summary>
    public bool HasPictureParameterSet { get; }

    /// <summary>
    /// Gets whether a reconnect, packet discontinuity, invalid timestamp regression, or discarded incomplete
    /// access unit occurred before this unit. Consumers should reset decoder state and await a keyframe.
    /// </summary>
    public bool Discontinuity { get; }
}
