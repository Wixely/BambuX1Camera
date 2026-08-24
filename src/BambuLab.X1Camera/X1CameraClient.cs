using System.Globalization;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using BambuLab.X1Camera.Protocol;

namespace BambuLab.X1Camera;

/// <summary>Connects to an X1-series printer's LAN camera without Bambu proprietary libraries.</summary>
public sealed class X1CameraClient
{
    /// <summary>Opens and authenticates an RTSPS/RTP/H.264 camera session.</summary>
    public async ValueTask<X1CameraSession> ConnectAsync(X1CameraOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var state = await EstablishAsync(options, cancellationToken).ConfigureAwait(false);
        return new X1CameraSession(this, options, state);
    }

    internal async ValueTask<X1CameraConnectionState> EstablishAsync(X1CameraOptions options, CancellationToken cancellationToken)
    {
        var streamUri = options.GetStreamUri();
        var connection = await RtspConnection.ConnectAsync(options, cancellationToken).ConfigureAwait(false);

        try
        {
            var describeHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Accept"] = "application/sdp"
            };
            var describe = await connection.SendAndReceiveAsync("DESCRIBE", streamUri, describeHeaders, cancellationToken).ConfigureAwait(false);
            if (describe.StatusCode == 401)
            {
                var challenge = describe.GetHeader("WWW-Authenticate")
                    ?? throw new InvalidDataException("The printer rejected authentication without returning a challenge.");
                connection.Authenticator = RtspAuthenticator.Parse(challenge, "bblp", options.AccessCode);
                describe = await connection.SendAndReceiveAsync("DESCRIBE", streamUri, describeHeaders, cancellationToken).ConfigureAwait(false);
            }

            EnsureSuccess("DESCRIBE", describe);
            var videoTrack = SdpVideoTrack.Parse(describe.Body);
            var trackUri = ResolveControlUri(streamUri, describe.GetHeader("Content-Base"), videoTrack.Control);

            var setup = await connection.SendAndReceiveAsync("SETUP", trackUri, new Dictionary<string, string>
            {
                ["Transport"] = "RTP/AVP/TCP;unicast;interleaved=0-1"
            }, cancellationToken).ConfigureAwait(false);
            EnsureSuccess("SETUP", setup);

            var session = ParseSession(setup.GetHeader("Session"));
            var play = await connection.SendAndReceiveAsync("PLAY", streamUri, new Dictionary<string, string>
            {
                ["Session"] = session.Id,
                ["Range"] = "npt=0.000-"
            }, cancellationToken).ConfigureAwait(false);
            EnsureSuccess("PLAY", play);

            return new X1CameraConnectionState(connection, streamUri, session.Id, session.Timeout, videoTrack);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static void EnsureSuccess(string method, RtspResponse response)
    {
        if (response.StatusCode is < 200 or >= 300)
        {
            throw new InvalidOperationException($"The printer rejected RTSP {method} with {response.StatusCode.ToString(CultureInfo.InvariantCulture)} {response.ReasonPhrase}.");
        }
    }

    internal static RtspSessionInfo ParseSession(string? value)
    {
        var parts = value?.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
        {
            throw new InvalidDataException("The printer did not return an RTSP session identifier.");
        }

        TimeSpan? timeout = null;
        foreach (var parameter in parts.Skip(1))
        {
            var separator = parameter.IndexOf('=');
            if (separator <= 0 || !parameter[..separator].Trim().Equals("timeout", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(parameter[(separator + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                || seconds is < 1 or > 86_400)
            {
                throw new InvalidDataException("The RTSP session contained an invalid timeout.");
            }

            timeout = TimeSpan.FromSeconds(seconds);
        }

        return new RtspSessionInfo(parts[0], timeout);
    }

    private static Uri ResolveControlUri(Uri streamUri, string? contentBase, string control)
    {
        if (Uri.TryCreate(control, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        if (!string.IsNullOrWhiteSpace(contentBase) && Uri.TryCreate(contentBase, UriKind.Absolute, out var baseUri))
        {
            return new Uri(baseUri, control);
        }

        if (control.StartsWith("/", StringComparison.Ordinal))
        {
            return new UriBuilder(streamUri) { Path = control, Query = string.Empty }.Uri;
        }

        return new Uri($"{streamUri.AbsoluteUri.TrimEnd('/')}/{control}", UriKind.Absolute);
    }
}

internal sealed record X1CameraConnectionState(
    RtspConnection Connection,
    Uri StreamUri,
    string SessionId,
    TimeSpan? SessionTimeout,
    SdpVideoTrack VideoTrack);

internal sealed record RtspSessionInfo(string Id, TimeSpan? Timeout);

/// <summary>An authenticated X1 camera session that yields encoded H.264 units.</summary>
public sealed class X1CameraSession : IAsyncDisposable
{
    private readonly Func<CancellationToken, ValueTask<X1CameraConnectionState>> establishConnection;
    private readonly X1CameraOptions options;
    private readonly CancellationTokenSource lifetime = new();
    private ActiveConnection active;
    private int reading;
    private int disposed;
    private long archivedDroppedFrames;
    private long interleavedFramesReceived;
    private long rtpPacketsReceived;
    private long malformedRtpPackets;
    private long unexpectedPayloadPackets;
    private long missingRtpPackets;
    private long outOfOrderRtpPackets;
    private long nalUnitsEmitted;
    private long reconnects;
    private long keepAlivesAcknowledged;
    private long keepAliveFailures;
    private int lastKeepAliveStatusCode;
    private long teardownsAcknowledged;
    private long teardownFailures;
    private int lastTeardownStatusCode;

    internal X1CameraSession(X1CameraClient client, X1CameraOptions options, X1CameraConnectionState state)
        : this(options, state, cancellationToken => client.EstablishAsync(options, cancellationToken))
    {
    }

    internal X1CameraSession(
        X1CameraOptions options,
        X1CameraConnectionState state,
        Func<CancellationToken, ValueTask<X1CameraConnectionState>> establishConnection)
    {
        this.options = options;
        this.establishConnection = establishConnection;
        active = StartConnection(state);
    }

    /// <summary>Gets the SDP-advertised RTP payload type for the current connection.</summary>
    public int PayloadType => Volatile.Read(ref active).State.VideoTrack.PayloadType;

    /// <summary>Gets the SDP-advertised RTP clock rate for the current connection.</summary>
    public int ClockRate => Volatile.Read(ref active).State.VideoTrack.ClockRate;

    /// <summary>Gets the current SDP-advertised sequence parameter set, without an Annex-B prefix.</summary>
    public ReadOnlyMemory<byte> SequenceParameterSet => Volatile.Read(ref active).State.VideoTrack.SequenceParameterSet ?? [];

    /// <summary>Gets the current SDP-advertised picture parameter set, without an Annex-B prefix.</summary>
    public ReadOnlyMemory<byte> PictureParameterSet => Volatile.Read(ref active).State.VideoTrack.PictureParameterSet ?? [];

    /// <summary>Gets the server-advertised RTSP session timeout for the current connection, when supplied.</summary>
    public TimeSpan? ServerSessionTimeout => Volatile.Read(ref active).State.SessionTimeout;

    /// <summary>Gets a point-in-time snapshot of stream health counters.</summary>
    public X1CameraStreamMetrics Metrics
    {
        get
        {
            var current = Volatile.Read(ref active);
            var currentDropped = Volatile.Read(ref current.Closed) == 0 ? current.State.Connection.DroppedFrameCount : 0;
            return new X1CameraStreamMetrics(
                Interlocked.Read(ref interleavedFramesReceived),
                Interlocked.Read(ref archivedDroppedFrames) + currentDropped,
                Interlocked.Read(ref rtpPacketsReceived),
                Interlocked.Read(ref malformedRtpPackets),
                Interlocked.Read(ref unexpectedPayloadPackets),
                Interlocked.Read(ref missingRtpPackets),
                Interlocked.Read(ref outOfOrderRtpPackets),
                Interlocked.Read(ref nalUnitsEmitted),
                Interlocked.Read(ref reconnects),
                Interlocked.Read(ref keepAlivesAcknowledged),
                Interlocked.Read(ref keepAliveFailures),
                Volatile.Read(ref lastKeepAliveStatusCode),
                Interlocked.Read(ref teardownsAcknowledged),
                Interlocked.Read(ref teardownFailures),
                Volatile.Read(ref lastTeardownStatusCode));
        }
    }

    /// <summary>Reads H.264 NAL units until cancellation, disposal, or exhausted reconnection attempts.</summary>
    public async IAsyncEnumerable<H264NalUnit> ReadNalUnitsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        BeginReading();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        try
        {
            await foreach (var item in ReadNalStreamAsync(linked.Token).ConfigureAwait(false))
            {
                if (item.Unit is { } unit)
                {
                    yield return unit;
                }
            }
        }
        finally
        {
            Volatile.Write(ref reading, 0);
        }
    }

    /// <summary>
    /// Reads complete encoded H.264 access units. Parameter sets are cached and injected before IDR access units
    /// when absent, and presentation timestamps account for normal 32-bit RTP wraparound.
    /// </summary>
    public async IAsyncEnumerable<H264AccessUnit> ReadAccessUnitsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        BeginReading();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var assembler = CreateAccessUnitAssembler();
        try
        {
            await foreach (var item in ReadNalStreamAsync(linked.Token).ConfigureAwait(false))
            {
                if (item.Discontinuity)
                {
                    var current = Volatile.Read(ref active).State.VideoTrack;
                    assembler.Reset(current.ClockRate, current.SequenceParameterSet ?? [], current.PictureParameterSet ?? []);
                }

                if (item.Unit is { } unit && assembler.Push(unit) is { } accessUnit)
                {
                    yield return accessUnit;
                }
            }
        }
        finally
        {
            Volatile.Write(ref reading, 0);
        }
    }

    private async IAsyncEnumerable<H264NalStreamItem> ReadNalStreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var depacketizer = new H264RtpDepacketizer();
        ushort? lastSequence = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var current = Volatile.Read(ref active);
            RtspInterleavedFrame? frame = null;
            var reconnected = false;
            try
            {
                frame = await current.State.Connection.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsRecoverable(exception))
            {
                await ReconnectAsync(current, exception, cancellationToken).ConfigureAwait(false);
                depacketizer = new H264RtpDepacketizer();
                lastSequence = null;
                reconnected = true;
            }

            if (reconnected)
            {
                yield return new H264NalStreamItem(null, true);
                continue;
            }

            Interlocked.Increment(ref interleavedFramesReceived);
            if (frame!.Channel != 0)
            {
                continue;
            }

            if (!RtpPacket.TryParse(frame.Payload, out var packet))
            {
                Interlocked.Increment(ref malformedRtpPackets);
                depacketizer = new H264RtpDepacketizer();
                yield return new H264NalStreamItem(null, true);
                continue;
            }

            if (packet.PayloadType != current.State.VideoTrack.PayloadType)
            {
                Interlocked.Increment(ref unexpectedPayloadPackets);
                continue;
            }

            Interlocked.Increment(ref rtpPacketsReceived);
            if (TrackSequence(packet.SequenceNumber, ref lastSequence))
            {
                depacketizer = new H264RtpDepacketizer();
                yield return new H264NalStreamItem(null, true);
            }

            foreach (var unit in depacketizer.Push(packet))
            {
                Interlocked.Increment(ref nalUnitsEmitted);
                yield return new H264NalStreamItem(unit, false);
            }
        }
    }

    private H264AccessUnitAssembler CreateAccessUnitAssembler()
    {
        var track = Volatile.Read(ref active).State.VideoTrack;
        return new H264AccessUnitAssembler(
            track.ClockRate,
            options.MaxAccessUnitBytes,
            options.MaxNalUnitsPerAccessUnit,
            track.SequenceParameterSet ?? [],
            track.PictureParameterSet ?? []);
    }

    private void BeginReading()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.Exchange(ref reading, 1) != 0)
        {
            throw new InvalidOperationException("A camera session supports one active reader. Fan out units in the host application.");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        var current = Volatile.Read(ref active);
        await CloseConnectionAsync(current, sendTeardown: current.Failure is null).ConfigureAwait(false);
        lifetime.Dispose();
    }

    private ActiveConnection StartConnection(X1CameraConnectionState state)
    {
        var connection = new ActiveConnection(state);
        connection.KeepAliveTask = RunKeepAliveAsync(connection);
        return connection;
    }

    private async Task RunKeepAliveAsync(ActiveConnection connection)
    {
        var interval = GetKeepAliveInterval(connection.State.SessionTimeout);
        if (interval <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(connection.KeepAliveLifetime.Token).ConfigureAwait(false))
            {
                var response = await connection.State.Connection.SendAndReceiveAsync(
                    "OPTIONS",
                    connection.State.StreamUri,
                    new Dictionary<string, string> { ["Session"] = connection.State.SessionId },
                    connection.KeepAliveLifetime.Token).ConfigureAwait(false);
                Volatile.Write(ref lastKeepAliveStatusCode, response.StatusCode);
                X1CameraClient.EnsureSuccess("OPTIONS", response);
                Interlocked.Increment(ref keepAlivesAcknowledged);
            }
        }
        catch (OperationCanceledException) when (connection.KeepAliveLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref keepAliveFailures);
            connection.Failure = exception;
            connection.State.Connection.Abort(new IOException("The acknowledged RTSP keepalive failed.", exception));
        }
    }

    private async Task ReconnectAsync(ActiveConnection failedConnection, Exception failure, CancellationToken cancellationToken)
    {
        failedConnection.Failure ??= failure;
        await CloseConnectionAsync(failedConnection, sendTeardown: false).ConfigureAwait(false);
        Exception lastFailure = failure;
        for (var attempt = 1; attempt <= options.ReconnectAttempts; attempt++)
        {
            if (options.ReconnectDelay > TimeSpan.Zero)
            {
                await Task.Delay(options.ReconnectDelay, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var state = await establishConnection(cancellationToken).ConfigureAwait(false);
                var replacement = StartConnection(state);
                Volatile.Write(ref active, replacement);
                Interlocked.Increment(ref reconnects);
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = new TimeoutException("A camera reconnection attempt timed out.");
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                lastFailure = exception;
            }
        }

        throw new IOException(
            $"The camera stream could not reconnect after {options.ReconnectAttempts.ToString(CultureInfo.InvariantCulture)} attempts.",
            lastFailure);
    }

    private async ValueTask CloseConnectionAsync(ActiveConnection connection, bool sendTeardown)
    {
        if (Interlocked.Exchange(ref connection.Closed, 1) != 0)
        {
            return;
        }

        connection.KeepAliveLifetime.Cancel();
        await connection.KeepAliveTask.ConfigureAwait(false);
        if (sendTeardown)
        {
            using var timeout = new CancellationTokenSource(options.TeardownTimeout);
            try
            {
                var response = await connection.State.Connection.SendAndReceiveAsync(
                    "TEARDOWN",
                    connection.State.StreamUri,
                    new Dictionary<string, string> { ["Session"] = connection.State.SessionId },
                    timeout.Token).ConfigureAwait(false);
                Volatile.Write(ref lastTeardownStatusCode, response.StatusCode);
                X1CameraClient.EnsureSuccess("TEARDOWN", response);
                Interlocked.Increment(ref teardownsAcknowledged);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Interlocked.Increment(ref teardownFailures);
            }
        }

        Interlocked.Add(ref archivedDroppedFrames, connection.State.Connection.DroppedFrameCount);
        await connection.State.Connection.DisposeAsync().ConfigureAwait(false);
        connection.KeepAliveLifetime.Dispose();
    }

    private bool TrackSequence(ushort sequence, ref ushort? lastSequence)
    {
        var discontinuity = false;
        if (lastSequence is { } previous)
        {
            var expected = (ushort)(previous + 1);
            var forwardDistance = (ushort)(sequence - expected);
            if (forwardDistance is > 0 and < 0x8000)
            {
                Interlocked.Add(ref missingRtpPackets, forwardDistance);
                discontinuity = true;
            }
            else if (forwardDistance >= 0x8000)
            {
                Interlocked.Increment(ref outOfOrderRtpPackets);
                discontinuity = true;
            }
        }

        lastSequence = sequence;
        return discontinuity;
    }

    private TimeSpan GetKeepAliveInterval(TimeSpan? serverTimeout)
    {
        if (options.KeepAliveInterval <= TimeSpan.Zero || serverTimeout is null)
        {
            return options.KeepAliveInterval;
        }

        var halfServerTimeout = TimeSpan.FromTicks(serverTimeout.Value.Ticks / 2);
        return halfServerTimeout < options.KeepAliveInterval ? halfServerTimeout : options.KeepAliveInterval;
    }

    private static bool IsRecoverable(Exception exception) => exception is
        IOException or
        SocketException or
        AuthenticationException or
        InvalidDataException or
        TimeoutException;

    private sealed class ActiveConnection(X1CameraConnectionState state)
    {
        public X1CameraConnectionState State { get; } = state;

        public CancellationTokenSource KeepAliveLifetime { get; } = new();

        public Task KeepAliveTask { get; set; } = Task.CompletedTask;

        public Exception? Failure { get; set; }

        public int Closed;
    }

    private readonly record struct H264NalStreamItem(H264NalUnit? Unit, bool Discontinuity);
}
