# BambuLab.X1Camera

Managed .NET 10 transport for the Bambu Lab X1-series LAN camera. The library connects directly to the printer's authenticated RTSPS endpoint, reads interleaved RTP, and emits H.264 NAL units with Annex-B framing.

The core library does not use `bambu_networking.dll`, DirectShow, Media Foundation, FFmpeg, GStreamer, VLC, P/Invoke, Python, or Node.js. It does not decode H.264 into pixels; a browser or an optional platform codec should own that boundary.

The intended product includes live video, still-image capture, GitHub Packages distribution, a host-neutral plugin boundary, and a basic viewer. See the [project plan](docs/PROJECT-PLAN.md) for the architecture, milestones, acceptance criteria, and unresolved release choices.

The project is licensed under the [MIT License](LICENSE). Project code must be independently authored or sourced exclusively from MIT-licensed code, including all direct and transitive dependencies. See the [dependency and source policy](docs/DEPENDENCY-POLICY.md).

## Current status

- Implements RTSPS over `SslStream` on the X1 default port, RTSP Basic and Digest authentication, SDP track discovery, interleaved RTP, H.264 single-NAL, STAP-A, and FU-A packet handling.
- Supports OS certificate validation or a configured SHA-256 certificate pin. Unsafe trust bypass is present only in the library options for isolated diagnostic work and is not exposed by the sample.
- Includes dependency-free protocol specifications and a console probe that can optionally capture the encoded Annex-B stream.
- Has not yet been exercised against the target physical X1C. The current protocol implementation is based on the researched community specification and must be hardware-validated before being treated as stable.
- Browser fMP4/WebCodecs delivery, still-image capture, the sample viewer, GitHub publishing, and the host plugin contract are planned next; they are not part of this initial slice.

## Build and verify

Requirements: .NET SDK 10.0.300 or a later 10.0 patch.

```powershell
dotnet build .\BambuX1Camera.slnx
dotnet run --project .\tests\BambuLab.X1Camera.Specs\BambuLab.X1Camera.Specs.csproj
```

The repository includes VS Code build and debug configuration. The default debug target runs the protocol specifications and never requests printer credentials.

## Probe a printer

Enable LAN live view on the printer. Keep credentials in environment variables rather than source files or command arguments:

```powershell
$env:BAMBU_X1_HOST = "printer-address"
$env:BAMBU_X1_ACCESS_CODE = "access-code"
$env:BAMBU_X1_CERT_SHA256 = "printer-certificate-sha256-fingerprint"
dotnet run --project .\samples\BambuLab.X1Camera.Probe\BambuLab.X1Camera.Probe.csproj
```

To save the encoded stream for validation with an independent player, add `--output capture.h264`. The command uses `FileMode.CreateNew` so it cannot silently overwrite an existing capture.

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
- Bind any future restream endpoint to an explicitly configured interface, authenticate viewers, and default recording to off.
- The protocol is not an officially supported Bambu third-party contract and may change with firmware.

## Project boundaries

This folder is currently a local source project with no remote. It is MIT-licensed, but the GitHub owner, repository visibility, package visibility, and publication timing have not been selected. Do not publish it until those choices are explicit and the pre-push review is complete.
