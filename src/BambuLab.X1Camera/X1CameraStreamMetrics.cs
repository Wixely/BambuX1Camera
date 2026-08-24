namespace BambuLab.X1Camera;

/// <summary>Provides a point-in-time snapshot of transport and H.264 stream health counters.</summary>
public readonly record struct X1CameraStreamMetrics(
    long InterleavedFramesReceived,
    long InterleavedFramesDropped,
    long RtpPacketsReceived,
    long MalformedRtpPackets,
    long UnexpectedPayloadPackets,
    long MissingRtpPackets,
    long OutOfOrderRtpPackets,
    long NalUnitsEmitted,
    long Reconnects,
    long KeepAlivesAcknowledged,
    long KeepAliveFailures,
    int LastKeepAliveStatusCode,
    long TeardownsAcknowledged,
    long TeardownFailures,
    int LastTeardownStatusCode);
