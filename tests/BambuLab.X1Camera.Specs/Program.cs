using System.Buffers.Binary;
using System.Text;
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
    ("probe settings round-trip without plaintext values", ProbeSettingsRoundTripWithoutPlaintext)
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

static RtpPacket ParseRtp(ushort sequence, uint timestamp, bool marker, byte[] payload)
{
    var bytes = new byte[12 + payload.Length];
    bytes[0] = 0x80;
    bytes[1] = (byte)(96 | (marker ? 0x80 : 0));
    BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), sequence);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), timestamp);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 123);
    payload.CopyTo(bytes, 12);
    AssertEx.True(RtpPacket.TryParse(bytes, out var packet));
    return packet;
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
