namespace BambuLab.X1Camera;

/// <summary>Common H.264 network abstraction layer unit types.</summary>
public enum H264NalUnitType : byte
{
    /// <summary>An unspecified or unsupported unit type.</summary>
    Unspecified = 0,

    /// <summary>A non-IDR coded slice.</summary>
    CodedSlice = 1,

    /// <summary>An instantaneous decoder refresh coded slice.</summary>
    IdrCodedSlice = 5,

    /// <summary>Supplemental enhancement information.</summary>
    SupplementalEnhancementInformation = 6,

    /// <summary>A sequence parameter set.</summary>
    SequenceParameterSet = 7,

    /// <summary>A picture parameter set.</summary>
    PictureParameterSet = 8,

    /// <summary>An access-unit delimiter.</summary>
    AccessUnitDelimiter = 9
}

/// <summary>An H.264 NAL unit reconstructed from an RTP packet stream.</summary>
public sealed class H264NalUnit
{
    internal H264NalUnit(byte[] annexBData, uint rtpTimestamp, ushort lastSequenceNumber, bool marker)
    {
        AnnexBData = annexBData;
        RtpTimestamp = rtpTimestamp;
        LastSequenceNumber = lastSequenceNumber;
        Marker = marker;
        Type = annexBData.Length > 4 ? (H264NalUnitType)(annexBData[4] & 0x1f) : H264NalUnitType.Unspecified;
    }

    /// <summary>Gets the complete NAL unit prefixed with a four-byte Annex-B start code.</summary>
    public ReadOnlyMemory<byte> AnnexBData { get; }

    /// <summary>Gets the RTP timestamp associated with the unit.</summary>
    public uint RtpTimestamp { get; }

    /// <summary>Gets the last RTP sequence number contributing to the unit.</summary>
    public ushort LastSequenceNumber { get; }

    /// <summary>Gets whether the RTP marker bit was set on the final contributing packet.</summary>
    public bool Marker { get; }

    /// <summary>Gets the H.264 NAL unit type.</summary>
    public H264NalUnitType Type { get; }

    /// <summary>Gets whether this unit is an IDR coded slice.</summary>
    public bool IsKeyFrame => Type == H264NalUnitType.IdrCodedSlice;
}

