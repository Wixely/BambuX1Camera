using H264Sharp.Decoder;
using H264Sharp.Decoder.Picture;

namespace BambuLab.X1Camera.Imaging;

/// <summary>Decodes complete Annex-B H.264 keyframes into PNG images using H264Sharp.</summary>
public sealed class X1CameraPngDecoder
{
    /// <summary>Decodes one complete keyframe access unit into a PNG image.</summary>
    public X1CameraPngImage Decode(H264AccessUnit accessUnit)
    {
        ArgumentNullException.ThrowIfNull(accessUnit);
        if (!accessUnit.IsKeyFrame)
        {
            throw new ArgumentException("Still-image decoding requires an IDR access unit.", nameof(accessUnit));
        }

        if (!accessUnit.HasSequenceParameterSet || !accessUnit.HasPictureParameterSet)
        {
            throw new ArgumentException("Still-image decoding requires SPS and PPS parameter sets.", nameof(accessUnit));
        }

        var picture = new H264FrameDecoder().DecodeFirstIFrame(accessUnit.AnnexBData.Span);
        var rgb = YuvToRgb.Convert(picture, picture.Vui);
        var png = PngEncoder.EncodeRgb(picture.Width, picture.Height, rgb);
        return new X1CameraPngImage(
            png,
            picture.Width,
            picture.Height,
            accessUnit.RtpTimestamp,
            accessUnit.PresentationTime);
    }
}
