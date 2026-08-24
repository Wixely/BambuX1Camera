namespace BambuLab.X1Camera.Imaging;

/// <summary>An RGB24 video frame decoded from an H.264 access unit.</summary>
public sealed class X1CameraDecodedFrame
{
    internal X1CameraDecodedFrame(
        byte[] rgb24Data,
        int width,
        int height,
        uint rtpTimestamp,
        TimeSpan presentationTime)
    {
        Rgb24Data = rgb24Data;
        Width = width;
        Height = height;
        RtpTimestamp = rtpTimestamp;
        PresentationTime = presentationTime;
    }

    /// <summary>Gets tightly packed RGB pixels with three bytes per pixel and no row padding.</summary>
    public ReadOnlyMemory<byte> Rgb24Data { get; }

    /// <summary>Gets the visible frame width.</summary>
    public int Width { get; }

    /// <summary>Gets the visible frame height.</summary>
    public int Height { get; }

    /// <summary>Gets the source RTP timestamp associated with this decoded frame.</summary>
    public uint RtpTimestamp { get; }

    /// <summary>Gets the normalized source presentation time associated with this decoded frame.</summary>
    public TimeSpan PresentationTime { get; }
}
