using System.Buffers.Binary;
using System.Text;
using System.Threading.Channels;
using BambuLab.X1Camera;
using BambuLab.X1Camera.Probe;
using BambuLab.X1Camera.Protocol;

var specifications = new (string Name, Func<Task> Run)[]
{
    ("options do not disclose the access code", OptionsDoNotDiscloseAccessCode),
    ("RTP parser handles extension and padding", RtpParserHandlesExtensionAndPadding),
    ("single NAL packets become Annex-B units", SingleNalPacketBecomesAnnexB),
    ("STAP-A packets emit every aggregated unit", StapAEmitsAggregatedUnits),
    ("FU-A packets are reassembled", FuAIsReassembled),
    ("FU-A sequence gaps discard partial units", FuASequenceGapIsDiscarded),
    ("SDP parser discovers the H264 video track", SdpParserDiscoversH264Track),
    ("RTSP wire reader separates responses and interleaved frames", RtspWireReaderSeparatesFrames),
    ("digest authentication does not disclose the access code", DigestAuthenticationDoesNotDiscloseAccessCode),
    ("legacy digest omits an unchallenged algorithm", LegacyDigestOmitsUnchallengedAlgorithm),
    ("digest echoes an explicitly challenged MD5 algorithm", DigestEchoesExplicitMd5Algorithm),
    ("probe settings round-trip without plaintext values", ProbeSettingsRoundTripWithoutPlaintext),
    ("RTSP connection matches concurrent responses by CSeq", RtspConnectionMatchesResponsesByCSeq),
    ("RTSP connection drops frames at its bounded capacity", RtspConnectionDropsFramesAtCapacity),
    ("stream metrics count RTP sequence gaps", StreamMetricsCountSequenceGaps),
    ("camera session reconnects after a stream failure", CameraSessionReconnectsAfterFailure),
    ("camera session acknowledges keepalive and teardown", CameraSessionAcknowledgesLifecycleRequests),
    ("RTSP session parser bounds the advertised timeout", RtspSessionParserBoundsTimeout)
};

var failures = 0;
foreach (var specification in specifications)
{
    try
    {
        await specification.Run();
        Console.WriteLine($"PASS {specification.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {specification.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{specifications.Length - failures}/{specifications.Length} specifications passed.");
return failures == 0 ? 0 : 1;

static Task OptionsDoNotDiscloseAccessCode()
{
    const string secret = "not-a-real-access-code";
    var options = new X1CameraOptions("printer.local", secret);
    AssertEx.False(options.ToString().Contains(secret, StringComparison.Ordinal));
    return Task.CompletedTask;
}

static Task RtpParserHandlesExtensionAndPadding()
{
    var packetBytes = new byte[12 + 4 + 4 + 2 + 2];
    packetBytes[0] = 0xb0;
    packetBytes[1] = 0xe0;
    BinaryPrimitives.WriteUInt16BigEndian(packetBytes.AsSpan(2), 17);
    BinaryPrimitives.WriteUInt32BigEndian(packetBytes.AsSpan(4), 1234);
    BinaryPrimitives.WriteUInt32BigEndian(packetBytes.AsSpan(8), 99);
    BinaryPrimitives.WriteUInt16BigEndian(packetBytes.AsSpan(12), 0x1000);
    BinaryPrimitives.WriteUInt16BigEndian(packetBytes.AsSpan(14), 1);
    packetBytes[16] = 1;
    packetBytes[17] = 2;
    packetBytes[18] = 3;
    packetBytes[19] = 4;
    packetBytes[20] = 0x65;
    packetBytes[21] = 0xaa;
    packetBytes[^1] = 2;

    AssertEx.True(RtpPacket.TryParse(packetBytes, out var packet));
    AssertEx.Equal((byte)96, packet.PayloadType);
    AssertEx.True(packet.Marker);
    AssertEx.SequenceEqual(new byte[] { 0x65, 0xaa }, packet.Payload.Span);
    return Task.CompletedTask;
}

static Task SingleNalPacketBecomesAnnexB()
{
    var packet = ParseRtp(1, 10, true, [0x65, 0x11, 0x22]);
    var unit = AssertEx.Single(new H264RtpDepacketizer().Push(packet));
    AssertEx.SequenceEqual(new byte[] { 0, 0, 0, 1, 0x65, 0x11, 0x22 }, unit.AnnexBData.Span);
    AssertEx.True(unit.IsKeyFrame);
    AssertEx.True(unit.Marker);
    return Task.CompletedTask;
}

static Task StapAEmitsAggregatedUnits()
{
    var packet = ParseRtp(2, 20, true, [0x78, 0, 2, 0x67, 0x01, 0, 2, 0x68, 0x02]);
    var units = new H264RtpDepacketizer().Push(packet);
    AssertEx.Equal(2, units.Count);
    AssertEx.Equal(H264NalUnitType.SequenceParameterSet, units[0].Type);
    AssertEx.False(units[0].Marker);
    AssertEx.Equal(H264NalUnitType.PictureParameterSet, units[1].Type);
    AssertEx.True(units[1].Marker);
    return Task.CompletedTask;
}

static Task FuAIsReassembled()
{
    var depacketizer = new H264RtpDepacketizer();
    AssertEx.Equal(0, depacketizer.Push(ParseRtp(30, 77, false, [0x7c, 0x85, 0x11, 0x22])).Count);
    var unit = AssertEx.Single(depacketizer.Push(ParseRtp(31, 77, true, [0x7c, 0x45, 0x33, 0x44])));
    AssertEx.SequenceEqual(new byte[] { 0, 0, 0, 1, 0x65, 0x11, 0x22, 0x33, 0x44 }, unit.AnnexBData.Span);
    AssertEx.Equal((ushort)31, unit.LastSequenceNumber);
    return Task.CompletedTask;
}

static Task FuASequenceGapIsDiscarded()
{
    var depacketizer = new H264RtpDepacketizer();
    _ = depacketizer.Push(ParseRtp(40, 88, false, [0x7c, 0x85, 0x11]));
    AssertEx.Equal(0, depacketizer.Push(ParseRtp(42, 88, true, [0x7c, 0x45, 0x22])).Count);
    return Task.CompletedTask;
}

static Task SdpParserDiscoversH264Track()
{
    const string sdp = "v=0\r\nm=video 0 RTP/AVP 96\r\na=rtpmap:96 H264/90000\r\na=fmtp:96 packetization-mode=1;sprop-parameter-sets=Z0IAH+KQCgC3AQEBQAAA+gAAHuSg,aM4G4g==\r\na=control:track1\r\n";
    var track = SdpVideoTrack.Parse(Encoding.ASCII.GetBytes(sdp));
    AssertEx.Equal(96, track.PayloadType);
    AssertEx.Equal(90_000, track.ClockRate);
    AssertEx.Equal("track1", track.Control);
    AssertEx.True(track.SequenceParameterSet is { Length: > 0 });
    AssertEx.True(track.PictureParameterSet is { Length: > 0 });
    return Task.CompletedTask;
}

static async Task RtspWireReaderSeparatesFrames()
{
    var response = Encoding.ASCII.GetBytes("RTSP/1.0 200 OK\r\nCSeq: 1\r\nContent-Length: 3\r\n\r\nsdp");
    var interleaved = new byte[] { (byte)'$', 0, 0, 3, 1, 2, 3 };
    using var stream = new MemoryStream([.. response, .. interleaved]);
    var reader = new RtspWireReader(stream);

    var first = await reader.ReadAsync(CancellationToken.None);
    var parsedResponse = AssertEx.IsType<RtspResponse>(first);
    AssertEx.Equal(200, parsedResponse.StatusCode);
    AssertEx.SequenceEqual("sdp"u8, parsedResponse.Body);

    var second = await reader.ReadAsync(CancellationToken.None);
    var frame = AssertEx.IsType<RtspInterleavedFrame>(second);
    AssertEx.Equal((byte)0, frame.Channel);
    AssertEx.SequenceEqual(new byte[] { 1, 2, 3 }, frame.Payload);
}

static Task DigestAuthenticationDoesNotDiscloseAccessCode()
{
    const string secret = "not-a-real-access-code";
    var authenticator = RtspAuthenticator.Parse("Digest realm=\"camera\", nonce=\"abc\", qop=\"auth\"", "bblp", secret);
    var value = authenticator.CreateAuthorization("DESCRIBE", new Uri("rtsps://printer.local:322/streaming/live/1"));
    AssertEx.True(value.StartsWith("Digest ", StringComparison.Ordinal));
    AssertEx.False(value.Contains(secret, StringComparison.Ordinal));
    AssertEx.True(value.Contains("qop=auth", StringComparison.Ordinal));
    return Task.CompletedTask;
}

static Task LegacyDigestOmitsUnchallengedAlgorithm()
{
    var authenticator = RtspAuthenticator.Parse("Digest realm=\"camera\", nonce=\"abc\"", "bblp", "test-code");
    var value = authenticator.CreateAuthorization("DESCRIBE", new Uri("rtsps://printer.local:322/streaming/live/1"));
    AssertEx.False(value.Contains("algorithm=", StringComparison.OrdinalIgnoreCase));
    return Task.CompletedTask;
}

static Task DigestEchoesExplicitMd5Algorithm()
{
    var authenticator = RtspAuthenticator.Parse("Digest realm=\"camera\", nonce=\"abc\", algorithm=MD5", "bblp", "test-code");
    var value = authenticator.CreateAuthorization("DESCRIBE", new Uri("rtsps://printer.local:322/streaming/live/1"));
    AssertEx.True(value.Contains("algorithm=MD5", StringComparison.Ordinal));
    return Task.CompletedTask;
}

static async Task ProbeSettingsRoundTripWithoutPlaintext()
{
    const string host = "printer.example";
    const string accessCode = "test-code";
    const string certificatePin = "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF";
    var path = Path.Combine(Path.GetTempPath(), $"bambu-x1-camera-settings-{Guid.NewGuid():N}.json");
    try
    {
        await ProbeSettingsStore.SaveAsync(path, new ProbeSettings(host, accessCode, certificatePin, 322), CancellationToken.None);
        var contents = await File.ReadAllTextAsync(path);
        AssertEx.False(contents.Contains(host, StringComparison.Ordinal));
        AssertEx.False(contents.Contains(accessCode, StringComparison.Ordinal));
        AssertEx.False(contents.Contains(certificatePin, StringComparison.Ordinal));

        var loaded = await ProbeSettingsStore.LoadAsync(path, CancellationToken.None);
        AssertEx.Equal(host, loaded.Host);
        AssertEx.Equal(accessCode, loaded.AccessCode);
        AssertEx.Equal(certificatePin, loaded.CertificateSha256);
        AssertEx.Equal(322, loaded.Port);
        AssertEx.False(loaded.ToString().Contains(accessCode, StringComparison.Ordinal));
    }
    finally
    {
        File.Delete(path);
    }
}

static async Task RtspConnectionMatchesResponsesByCSeq()
{
    var requestSequences = new List<int>();
    ScriptedDuplexStream? stream = null;
    stream = new ScriptedDuplexStream(request =>
    {
        requestSequences.Add(ReadRequestSequence(request));
        if (requestSequences.Count != 2)
        {
            return;
        }

        stream!.QueueIncoming([(byte)'$', 0, 0, 3, 1, 2, 3]);
        stream.QueueIncoming(Encoding.ASCII.GetBytes($"RTSP/1.0 202 Accepted\r\nCSeq: {requestSequences[1]}\r\nContent-Length: 0\r\n\r\n"));
        stream.QueueIncoming(Encoding.ASCII.GetBytes($"RTSP/1.0 201 Created\r\nCSeq: {requestSequences[0]}\r\nContent-Length: 0\r\n\r\n"));
    });

    await using var connection = RtspConnection.CreateForStream(stream);
    var uri = new Uri("rtsps://printer.example:322/streaming/live/1");
    var firstTask = connection.SendAndReceiveAsync("OPTIONS", uri, null, CancellationToken.None).AsTask();
    var secondTask = connection.SendAndReceiveAsync("GET_PARAMETER", uri, null, CancellationToken.None).AsTask();
    await Task.WhenAll(firstTask, secondTask);
    AssertEx.Equal(201, firstTask.Result.StatusCode);
    AssertEx.Equal(202, secondTask.Result.StatusCode);

    var frame = await connection.ReadFrameAsync(CancellationToken.None);
    AssertEx.Equal((byte)0, frame.Channel);
    AssertEx.SequenceEqual(new byte[] { 1, 2, 3 }, frame.Payload);
}

static async Task RtspConnectionDropsFramesAtCapacity()
{
    var bytes = Enumerable.Range(1, 3)
        .SelectMany(value => new byte[] { (byte)'$', 0, 0, 1, (byte)value })
        .ToArray();
    await using var connection = RtspConnection.CreateForStream(new MemoryStream(bytes), frameQueueCapacity: 1);
    var deadline = DateTime.UtcNow.AddSeconds(1);
    while (connection.DroppedFrameCount < 2 && DateTime.UtcNow < deadline)
    {
        await Task.Delay(10);
    }

    AssertEx.Equal(2L, connection.DroppedFrameCount);
    var frame = await connection.ReadFrameAsync(CancellationToken.None);
    AssertEx.SequenceEqual(new byte[] { 1 }, frame.Payload);
}

static async Task StreamMetricsCountSequenceGaps()
{
    var streamBytes = CreateInterleavedRtpStream(
        CreateRtpPacketBytes(10, 100, true, [0x65, 0x01]),
        CreateRtpPacketBytes(12, 200, true, [0x61, 0x02]));
    var options = new X1CameraOptions("printer.example", "test-code")
    {
        KeepAliveInterval = TimeSpan.Zero,
        ReconnectAttempts = 0,
        ReconnectDelay = TimeSpan.Zero,
        TeardownTimeout = TimeSpan.FromMilliseconds(20)
    };
    var state = CreateConnectionState(streamBytes);
    await using var session = new X1CameraSession(
        options,
        state,
        _ => ValueTask.FromException<X1CameraConnectionState>(new IOException("No replacement expected.")));

    var units = 0;
    await foreach (var _ in session.ReadNalUnitsAsync())
    {
        units++;
        if (units == 2)
        {
            break;
        }
    }

    AssertEx.Equal(2L, session.Metrics.RtpPacketsReceived);
    AssertEx.Equal(1L, session.Metrics.MissingRtpPackets);
    AssertEx.Equal(2L, session.Metrics.NalUnitsEmitted);
}

static async Task CameraSessionReconnectsAfterFailure()
{
    var options = new X1CameraOptions("printer.example", "test-code")
    {
        KeepAliveInterval = TimeSpan.Zero,
        ReconnectAttempts = 1,
        ReconnectDelay = TimeSpan.Zero,
        TeardownTimeout = TimeSpan.FromMilliseconds(20)
    };
    var initial = CreateConnectionState(CreateInterleavedRtpStream(CreateRtpPacketBytes(1, 100, true, [0x65, 0x01])));
    var replacement = CreateConnectionState(CreateInterleavedRtpStream(CreateRtpPacketBytes(2, 200, true, [0x65, 0x02])));
    var establishCalls = 0;
    await using var session = new X1CameraSession(
        options,
        initial,
        _ =>
        {
            establishCalls++;
            return ValueTask.FromResult(replacement);
        });

    var units = 0;
    await foreach (var _ in session.ReadNalUnitsAsync())
    {
        units++;
        if (units == 2)
        {
            break;
        }
    }

    AssertEx.Equal(1, establishCalls);
    AssertEx.Equal(1L, session.Metrics.Reconnects);
    AssertEx.Equal(2L, session.Metrics.NalUnitsEmitted);
}

static async Task CameraSessionAcknowledgesLifecycleRequests()
{
    var requests = new List<string>();
    ScriptedDuplexStream? stream = null;
    stream = new ScriptedDuplexStream(request =>
    {
        requests.Add(request);
        var sequence = ReadRequestSequence(request);
        stream!.QueueIncoming(Encoding.ASCII.GetBytes($"RTSP/1.0 200 OK\r\nCSeq: {sequence}\r\nContent-Length: 0\r\n\r\n"));
    });
    var connection = RtspConnection.CreateForStream(stream);
    var state = new X1CameraConnectionState(
        connection,
        new Uri("rtsps://printer.example:322/streaming/live/1"),
        "test-session",
        TimeSpan.FromMilliseconds(80),
        new SdpVideoTrack(96, 90_000, "track1", null, null));
    var options = new X1CameraOptions("printer.example", "test-code")
    {
        KeepAliveInterval = TimeSpan.FromSeconds(1),
        ReconnectAttempts = 0,
        TeardownTimeout = TimeSpan.FromSeconds(1)
    };
    var session = new X1CameraSession(
        options,
        state,
        _ => ValueTask.FromException<X1CameraConnectionState>(new IOException("No replacement expected.")));
    try
    {
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (session.Metrics.KeepAlivesAcknowledged < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        await session.DisposeAsync();
        AssertEx.True(session.Metrics.KeepAlivesAcknowledged >= 2);
        AssertEx.Equal(0L, session.Metrics.KeepAliveFailures);
        AssertEx.Equal(200, session.Metrics.LastKeepAliveStatusCode);
        AssertEx.Equal(1L, session.Metrics.TeardownsAcknowledged);
        AssertEx.Equal(0L, session.Metrics.TeardownFailures);
        AssertEx.Equal(200, session.Metrics.LastTeardownStatusCode);
        AssertEx.True(requests.Any(request => request.StartsWith("OPTIONS ", StringComparison.Ordinal)));
        AssertEx.True(requests.Where(request => request.StartsWith("OPTIONS ", StringComparison.Ordinal))
            .All(request => request.Contains("\r\nSession: test-session\r\n", StringComparison.OrdinalIgnoreCase)));
        AssertEx.True(requests.Any(request => request.StartsWith("TEARDOWN ", StringComparison.Ordinal)));
    }
    finally
    {
        await session.DisposeAsync();
    }
}

static Task RtspSessionParserBoundsTimeout()
{
    var session = X1CameraClient.ParseSession("session-token;timeout=10");
    AssertEx.Equal("session-token", session.Id);
    AssertEx.Equal(TimeSpan.FromSeconds(10), session.Timeout);

    try
    {
        _ = X1CameraClient.ParseSession("session-token;timeout=1000000");
        throw new InvalidOperationException("Expected an invalid timeout to be rejected.");
    }
    catch (InvalidDataException)
    {
    }

    return Task.CompletedTask;
}

static X1CameraConnectionState CreateConnectionState(byte[] streamBytes)
{
    var stream = new MemoryStream();
    stream.Write(streamBytes);
    stream.Position = 0;
    var connection = RtspConnection.CreateForStream(stream);
    var track = new SdpVideoTrack(96, 90_000, "track1", null, null);
    return new X1CameraConnectionState(
        connection,
        new Uri("rtsps://printer.example:322/streaming/live/1"),
        "test-session",
        null,
        track);
}

static byte[] CreateInterleavedRtpStream(params byte[][] packets)
{
    using var stream = new MemoryStream();
    foreach (var packet in packets)
    {
        stream.WriteByte((byte)'$');
        stream.WriteByte(0);
        stream.WriteByte((byte)(packet.Length >> 8));
        stream.WriteByte((byte)packet.Length);
        stream.Write(packet);
    }

    return stream.ToArray();
}

static int ReadRequestSequence(string request)
{
    var line = request.Split("\r\n", StringSplitOptions.None)
        .Single(value => value.StartsWith("CSeq:", StringComparison.OrdinalIgnoreCase));
    return int.Parse(line.AsSpan(line.IndexOf(':') + 1).Trim(), System.Globalization.CultureInfo.InvariantCulture);
}

static RtpPacket ParseRtp(ushort sequence, uint timestamp, bool marker, byte[] payload)
{
    var bytes = CreateRtpPacketBytes(sequence, timestamp, marker, payload);
    AssertEx.True(RtpPacket.TryParse(bytes, out var packet));
    return packet;
}

static byte[] CreateRtpPacketBytes(ushort sequence, uint timestamp, bool marker, byte[] payload)
{
    var bytes = new byte[12 + payload.Length];
    bytes[0] = 0x80;
    bytes[1] = (byte)(96 | (marker ? 0x80 : 0));
    BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), sequence);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), timestamp);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 123);
    payload.CopyTo(bytes, 12);
    return bytes;
}

internal sealed class ScriptedDuplexStream(Action<string> requestWritten) : Stream
{
    private readonly Channel<byte> incoming = Channel.CreateUnbounded<byte>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void QueueIncoming(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (!incoming.Writer.TryWrite(value))
            {
                throw new InvalidOperationException("The scripted stream was closed.");
            }
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        byte value;
        try
        {
            value = await incoming.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return 0;
        }

        buffer.Span[0] = value;
        var count = 1;
        while (count < buffer.Length && incoming.Reader.TryRead(out value))
        {
            buffer.Span[count++] = value;
        }

        return count;
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        requestWritten(Encoding.ASCII.GetString(buffer, offset, count));

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        requestWritten(Encoding.ASCII.GetString(buffer.Span));
        return ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            incoming.Writer.TryComplete();
        }

        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}

internal static class AssertEx
{
    public static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    public static void False(bool value) => True(!value);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
        }
    }

    public static void SequenceEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"Expected {Convert.ToHexString(expected)}, received {Convert.ToHexString(actual)}.");
        }
    }

    public static T Single<T>(IReadOnlyList<T> values)
    {
        Equal(1, values.Count);
        return values[0];
    }

    public static T IsType<T>(object value)
    {
        return value is T typed ? typed : throw new InvalidOperationException($"Expected {typeof(T).Name}, received {value.GetType().Name}.");
    }
}
