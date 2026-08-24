# BambuLab.X1Camera project plan

- Status: Initial plan
- Recorded: 2026-08-24
- Source: User requirements and the existing X1C camera investigation
- Review date: 2026-09-30

## Goal

Produce a reusable .NET library that connects directly to a Bambu Lab X1 Carbon camera with the printer's LAN access code, provides still images and live video without Bambu's proprietary Windows DLLs, can be embedded behind a plugin boundary, and is distributed as versioned NuGet packages through GitHub Packages. Include a basic viewer application that demonstrates the supported APIs without becoming a second production product.

The project and all independently authored code are MIT-licensed. Implementation code, dependencies, transitive dependencies, browser assets, packaged tools, and workflow actions must be MIT-licensed or independently authored for this project. Non-MIT implementation code must not be copied or adapted; protocol facts may only inform a separately authored implementation.

## Confirmed requirements

- Connect to an X1C on the local network using its LAN access code.
- Retrieve still images through a supported library API.
- Receive and display live video through a supported library API.
- Keep the transport portable C# and independent of `bambu_networking.dll` and other Bambu native binaries.
- Make the reusable functionality suitable for use by a plugin host.
- Produce installable, versioned packages through GitHub Packages.
- Include a sample viewer application.
- License the project under MIT.
- Use only independently authored or MIT-licensed implementation code and dependencies, including the complete transitive dependency graph.
- The diagnostic console probe may optionally save the printer address, access code, and certificate pin as Base64-encoded UTF-8 in ignored JSON beside its executable. Saving must be opt-in and clearly identified as reversible encoding rather than encryption. Confirmed by the user on 2026-08-24.

## Proposed architecture

```text
X1C RTSPS/RTP/H.264
        |
        v
Managed transport and parser
        |
        +--> Encoded H.264 NAL stream
        |
        +--> Managed fMP4 remux --> Browser video element
        |
        +--> Frame-decoder abstraction --> JPEG/PNG still image
```

The transport, authentication, SDP, RTP, H.264 parsing, reconnection, and fMP4 remux layers remain managed and platform-neutral. H.264 pixel decoding is a separate package capability implemented with Wixely/H264Sharp.

The still-image design keeps decoding in an explicit optional package rather than introducing it into the core transport package:

- The browser viewer uses the browser's H.264 decoder and captures a displayed frame for download.
- A library-level `CapturePngAsync` API in the imaging package waits for a complete IDR and returns PNG bytes, timestamp, dimensions, and content type.
- Headless still capture uses the optional pure-managed H264Sharp decoder package. H264Sharp 1.0.2 has no transitive package dependencies, but its BSD-attributed CABAC table is covered by the temporary user-approved exception recorded in the dependency policy and must be replaced by a corrected release before supported publication.
- During the hardware spike, check whether the current X1C firmware exposes a usable native still-image path. Prefer it if verified, but do not assume one exists.

## Proposed deliverables

### Core library

Working package name: `BambuLab.X1Camera`.

- Secure RTSPS connection with platform trust or SHA-256 certificate pinning.
- Basic and Digest authentication using username `bblp` and a caller-provided access code.
- SDP discovery, interleaved RTP, loss detection, keepalive, bounded buffering, reconnection, and cancellation.
- Encoded H.264 access-unit/NAL APIs with timing and stream metadata.
- Single upstream printer session with safe fan-out for multiple consumers.
- No logging, debugger display, or exception disclosure of the access code. Core transport packages do not persist credentials; the diagnostic probe has the explicit opt-in encoded-file exception recorded above.

### Streaming integration

Working package name: `BambuLab.X1Camera.AspNetCore`.

- Remux H.264 without transcoding into fragmented MP4 suitable for browser Media Source Extensions.
- Authenticated, explicitly bound local endpoints for a viewer or plugin host.
- Backpressure, viewer limits, bounded queues, and clean disconnection.
- No public restream endpoint by default.

This may remain part of the core package if the public API stays small. Split it only when the dependency or release boundary is useful.

### Still-image integration

Working package name: `BambuLab.X1Camera.Imaging`.

- `CapturePngAsync` returning a PNG result with timestamp, dimensions, and media type.
- `X1CameraPngDecoder` for decoding a keyframe supplied by a host-managed fan-out pipeline.
- A browser-backed implementation for the sample viewer.
- Pure-managed headless decoding through H264Sharp, pending the recorded provenance correction and Linux verification.

### Plugin boundary

- A small host-facing interface for connect, disconnect, health, live encoded stream, and still capture.
- No global static connection or configuration state.
- Caller-owned secret provider and cancellation lifetime.
- Capability reporting so a host can distinguish video transport, browser still capture, and headless still capture.
- Do not create a host-specific plugin package until the first target plugin host and its compatibility rules are identified.

### Sample viewer

Use a local Blazor Web App with server-side components. The server can reach the printer's RTSPS endpoint while the browser handles H.264 decoding. Do not AOT-compile the server.

The viewer should provide:

- Printer address, password-style access-code input, and certificate-pin input.
- Connect/disconnect controls and clear connection, certificate, stream, loss, and reconnect status.
- Live video with pause/resume and bounded latency.
- A capture button that previews and downloads a JPEG or PNG still.
- No credential persistence by default; an optional OS-protected secret-store sample may be added later.
- Localhost binding by default, authentication before non-loopback binding, locally served browser assets, and no CDN dependency.
- A normal interactive launch path on Windows and Linux plus a Docker example because it is a networked application.

## GitHub Packages and release plan

- Keep package identifiers provisional until the GitHub owner/repository is selected and name ownership is checked.
- Build deterministic `.nupkg` and symbol packages with README, XML documentation, repository metadata, Source Link, and dependency/license notices.
- Publish every package with the SPDX license expression `MIT` and include the repository's dependency notices.
- Reject any direct or transitive dependency, vendored asset, packaged tool, generated source input, or workflow action that is not verified as MIT-licensed.
- Use semantic prerelease versions until the X1C hardware matrix and public API are stable.
- Run build and protocol tests on Windows and Linux for every pull request and release candidate.
- On an approved version tag, build once, retain the packages as workflow artifacts, and publish the exact verified files to GitHub Packages using scoped workflow permissions.
- Install the published packages into a clean consumer project as a release gate.
- Do not publish a repository or package until repository and package visibility are explicitly decided, the MIT-only dependency audit passes, and the full pre-push privacy review is complete.

## Milestones

### 0.1 - Transport proof

- [x] Create the managed .NET 10 library and probe. - Owner: agent
- [x] Implement the initial RTSPS, authentication, SDP, RTP, and H.264 depacketisation path. - Owner: agent
- [x] Add bounded protocol specifications and VS Code debugging. - Owner: agent
- [x] Add interactive probe configuration, optional encoded local settings, timed tests, and first-contact certificate pin confirmation. - Owner: agent
- [ ] Complete hardware validation. A 35-second Windows run on 2026-08-24 confirmed TLS pinning, legacy Digest authentication, SDP, setup/playback, six keepalive acknowledgements scheduled from the server's 10-second timeout, acknowledged teardown, repeated IDR output, and zero observed packet gaps, malformed packets, queue drops, or reconnects. Interruption/restart, longer duration, and Linux remain. - Owner: agent
- [ ] Capture sanitized protocol fixtures for deterministic regression tests. - Owner: TBD

### 0.2 - Reliable video

- [x] Add `CSeq` response dispatch, bounded interleaved-frame buffering, adaptive acknowledged keepalive/teardown, stream-health metrics, and bounded reconnect policy. - Owner: agent
- [x] Add bounded access-unit assembly, SPS/PPS handling, timestamp normalization, and discontinuity signaling. A 10-second Windows hardware run on 2026-08-24 emitted 239 complete access units with repeated parameter-set-equipped IDRs and no observed transport discontinuity. - Owner: agent
- [ ] Add bounded fan-out. - Owner: TBD
- [ ] Implement managed fragmented-MP4 remuxing without transcoding. - Owner: TBD
- [ ] Prove 30-minute playback and repeated viewer connections on Windows and Linux browsers. - Owner: TBD

### 0.3 - Still images

- [ ] Confirm whether the tested X1C firmware offers a native still endpoint. - Owner: TBD
- [x] Define the PNG still-image result contract and optional imaging-package boundary. - Owner: agent
- [x] Implement `CapturePngAsync` and `X1CameraPngDecoder` using H264Sharp. A Windows hardware run on 2026-08-24 decoded and visually validated a 1760x1080 frame; the private test image was not retained. - Owner: agent
- [ ] Implement browser-backed JPEG/PNG capture. - Owner: TBD
- [x] Select H264Sharp for headless still capture and audit version 1.0.2. A narrow temporary BSD-table exception was approved by the user on 2026-08-24. - Owner: user and agent
- [ ] Upgrade to the corrected H264Sharp release and remove the temporary dependency-policy exception. - Owner: user and agent - Review: 2026-09-30

### 0.4 - Viewer and plugin contract

- [ ] Build the local Blazor sample viewer and its Docker example. - Owner: TBD
- [ ] Extract the smallest host-neutral plugin contract after both video and still APIs are proven. - Owner: TBD
- [ ] Add security, reconnect, malformed-input, and multi-consumer tests. - Owner: TBD

### 0.5 - Package release

- [x] License the project under MIT and adopt an MIT-only/self-authored source policy. - Owner: user
- [ ] Decide GitHub owner, repository visibility, package visibility, and final package identifiers. - Owner: user
- [ ] Add GitHub Actions verification and package publishing workflows with least-privilege permissions. - Owner: TBD
- [ ] Audit every direct and transitive dependency, browser asset, packaged tool, generated source input, and workflow action against the MIT-only policy. - Owner: TBD
- [ ] Complete Windows/Linux consumer installation and physical-X1C release tests. - Owner: TBD
- [ ] Complete privacy, binary provenance, license, package-content, and outgoing-history review before the first push. - Owner: TBD
- [ ] Publish the approved prerelease packages and verify installation from GitHub Packages. - Owner: TBD

## Acceptance criteria for the first supported release

- A clean consumer can install the packages from GitHub Packages and connect to the tested X1C firmware using an access code.
- Live video starts, remains bounded in latency, recovers from an interrupted connection, and can be consumed without a Bambu DLL.
- The public still-image API returns a valid JPEG or PNG and clearly reports when the selected runtime has no decoder capability.
- The sample viewer displays video, captures a still, never persists the access code by default, and runs on verified Windows and Linux configurations.
- Core transport packages contain no proprietary Bambu binaries, platform-specific decoder binaries, credentials, captures, or private device metadata.
- Every shipped code component, direct and transitive dependency, browser asset, packaged tool, and workflow action is independently authored or has verified MIT license evidence recorded in `THIRD-PARTY-NOTICES.md`.
- Certificate rejection, invalid credentials, malformed SDP/RTP, packet loss, cancellation, printer restart, and viewer churn are covered by automated or documented physical tests.
- Package versions, repository metadata, symbols, documentation, license notices, and release notes correspond to the exact tested source revision.

## Open decisions

- Which GitHub owner and repository will host the source and packages, and will each be public or private?
- Should the first package release expose only PNG, or add JPEG after the PNG API and viewer are stable?
- Which plugin host should define the first compatibility target?
- Which exact X1C firmware, camera settings, and network isolation model form the supported test matrix?
- Is macOS support required, or should the first release claim only verified Windows and Linux support?
