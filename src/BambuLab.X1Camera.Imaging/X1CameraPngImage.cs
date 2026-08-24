namespace BambuLab.X1Camera.Imaging;

/// <summary>A decoded camera frame encoded as a PNG image.</summary>
public sealed class X1CameraPngImage
{
    internal X1CameraPngImage(
        byte[] data,
        int width,
        int height,
        uint rtpTimestamp,
        TimeSpan presentationTime)
    {
        Data = data;
        Width = width;
        Height = height;
        RtpTimestamp = rtpTimestamp;
        PresentationTime = presentationTime;
    }

    /// <summary>Gets the complete PNG file bytes.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the decoded image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the decoded image height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the source access unit's RTP timestamp.</summary>
    public uint RtpTimestamp { get; }

    /// <summary>Gets the source access unit's normalized presentation time.</summary>
    public TimeSpan PresentationTime { get; }

    /// <summary>Gets the image media type.</summary>
    public string MediaType => "image/png";
}
