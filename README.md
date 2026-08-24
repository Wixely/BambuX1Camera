# BambuLab.X1Camera

Managed .NET 10 transport for the Bambu Lab X1-series LAN camera. The library connects directly to the printer's authenticated RTSPS endpoint, reads interleaved RTP, and emits H.264 NAL units with Annex-B framing.

The core library does not use `bambu_networking.dll`, DirectShow, Media Foundation, FFmpeg, GStreamer, VLC, P/Invoke, Python, or Node.js. It does not decode H.264 into pixels; a browser or an optional platform codec should own that boundary.

The intended product includes live video, still-image capture, GitHub Packages distribution, a host-neutral plugin boundary, and a basic viewer. See the [project plan](docs/PROJECT-PLAN.md) for the architecture, milestones, acceptance criteria, and unresolved release choices.

The project is licensed under the [MIT License](LICENSE). Project code must be independently authored or sourced exclusively from MIT-licensed code, including all direct and transitive dependencies. See the [dependency and source policy](docs/DEPENDENCY-POLICY.md).

## Current status

- Implements RTSPS over `SslStream` on the X1 default port, RTSP Basic and Digest authentication, SDP track discovery, interleaved RTP, H.264 single-NAL, STAP-A, and FU-A packet handling.
- Supports OS certificate validation or a configured SHA-256 certificate pin. The console probe can perform an explicit trust-on-first-use certificate observation without sending credentials, then pin the accepted certificate for the authenticated connection.
- Includes dependency-free protocol specifications and an interactive console probe that supports timed tests and optional encoded Annex-B capture.
- A 30-second Windows hardware run against a physical X1C validated TLS pinning, legacy Digest authentication, SDP, setup/playback, clean cancellation/disposal, and encoded H.264 output with repeated IDR frames. Longer-duration, interruption, restart, and Linux validation remain before the implementation is treated as stable.
- Browser fMP4/WebCodecs delivery, still-image capture, the sample viewer, GitHub publishing, and the host plugin contract are planned next; they are not part of this initial slice.

## Build and verify

Requirements: .NET SDK 10.0.300 or a later 10.0 patch.

```powershell
dotnet build .\BambuX1Camera.slnx
dotnet run --project .\tests\BambuLab.X1Camera.Specs\BambuLab.X1Camera.Specs.csproj
```

The repository includes VS Code build and debug configuration. The default debug target runs the protocol specifications and never requests printer credentials.

## Probe a printer

Enable LAN live view on the printer, build the solution, and start the probe interactively:

```powershell
.\samples\BambuLab.X1Camera.Probe\bin\Debug\net10.0\BambuLab.X1Camera.Probe.exe --configure --duration 30
```

The probe masks access-code input. When no certificate pin is available, it observes the certificate without sending credentials and asks before trusting and pinning it. It then offers to save the printer address, access code, and certificate pin in `bambu-x1-camera.settings.json` beside the executable. Values are Base64-encoded UTF-8: this is reversible encoding, not encryption. Saving is opt-in and the filename is ignored by Git.

Later runs load that file automatically. Use `--configure` to replace it, `--duration N` for a bounded test, and `--output capture.h264` to save the encoded Annex-B stream. Capture output uses `FileMode.CreateNew` so it cannot silently overwrite an existing file.

Environment variables remain available for automation and take precedence over the saved file:

```powershell
$env:BAMBU_X1_HOST = "printer-address"
$env:BAMBU_X1_ACCESS_CODE = "access-code"
$env:BAMBU_X1_CERT_SHA256 = "printer-certificate-sha256-fingerprint"
dotnet run --project .\samples\BambuLab.X1Camera.Probe\BambuLab.X1Camera.Probe.csproj -- --duration 30
```

Environment variables are inherited by child processes. Clear sensitive values from the shell after testing:

```powershell
Remove-Item Env:BAMBU_X1_ACCESS_CODE
```

## API shape

```csharp
var options = new X1CameraOptions(host, accessCode)
{
    PinnedCertificateSha256 = certificateFingerprint
};

await using var session = await new X1CameraClient().ConnectAsync(options, cancellationToken);
await foreach (var nalUnit in session.ReadNalUnitsAsync(cancellationToken))
{
    // nalUnit.AnnexBData remains encoded H.264.
}
```

Only one reader is permitted per printer session. A plugin host should maintain one connection and fan out bounded copies or references to authorized consumers.

## Security

- Never log the access code, authorization headers, printer serial, certificate fingerprint, or private network address.
- Prefer a verified SHA-256 certificate pin when the printer certificate is not trusted by the operating system. Do not ship with `AllowUntrustedCertificate` enabled.
- The probe's optional local JSON is intended only for isolated development. Base64 does not protect credentials from another user or process that can read the file; delete it after testing or use environment variables instead on shared systems.
- Bind any future restream endpoint to an explicitly configured interface, authenticate viewers, and default recording to off.
- The protocol is not an officially supported Bambu third-party contract and may change with firmware.

## Project boundaries

This folder is currently a local source project with no remote. It is MIT-licensed, but the GitHub owner, repository visibility, package visibility, and publication timing have not been selected. Do not publish it until those choices are explicit and the pre-push review is complete.
