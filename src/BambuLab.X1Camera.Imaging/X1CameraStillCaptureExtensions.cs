namespace BambuLab.X1Camera.Imaging;

/// <summary>Still-image capture operations for authenticated X1 camera sessions.</summary>
public static class X1CameraStillCaptureExtensions
{
    /// <summary>
    /// Waits for the next complete IDR access unit, decodes it with H264Sharp, and returns a PNG image.
    /// This operation becomes the session's one active reader.
    /// </summary>
    public static async ValueTask<X1CameraPngImage> CapturePngAsync(
        this X1CameraSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var decoder = new X1CameraPngDecoder();
        await foreach (var accessUnit in session.ReadAccessUnitsAsync(cancellationToken).ConfigureAwait(false))
        {
            if (accessUnit.IsKeyFrame
                && accessUnit.HasSequenceParameterSet
                && accessUnit.HasPictureParameterSet)
            {
                return decoder.Decode(accessUnit);
            }
        }

        throw new EndOfStreamException("The camera stream ended before a complete keyframe was available.");
    }

    /// <summary>Waits for and decodes the next complete IDR from a fan-out subscription.</summary>
    public static async ValueTask<X1CameraPngImage> CapturePngAsync(
        this X1CameraAccessUnitSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        var decoder = new X1CameraPngDecoder();
        await foreach (var accessUnit in subscription.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (accessUnit.IsKeyFrame
                && accessUnit.HasSequenceParameterSet
                && accessUnit.HasPictureParameterSet)
            {
                return decoder.Decode(accessUnit);
            }
        }

        throw new EndOfStreamException("The camera stream ended before a complete keyframe was available.");
    }
}
