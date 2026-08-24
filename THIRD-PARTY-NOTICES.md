# Third-party notices

## H264Sharp.Decoder 1.0.2

- Source: <https://github.com/Wixely/H264Sharp/tree/v1.0.2>
- Package source: <https://nuget.pkg.github.com/Wixely/index.json>
- Package ID: `H264Sharp.Decoder`
- Version: `1.0.2`
- Release commit: `42019859ba0de5cd8290cbfc71f2e28751edfe37`
- Primary license: MIT
- Purpose: Pure-managed H.264 keyframe decoding, YUV-to-RGB conversion, and PNG encoding in the optional `BambuLab.X1Camera.Imaging` package.
- Transitive package dependencies: None. The .NET SDK supplies framework and build-time components.
- Reviewed: 2026-08-24

The release's `CabacInitTable.cs` header and `LICENSE-3RDPARTY.md` attribute its generated CABAC initialization values to Cisco OpenH264 under BSD-2-Clause. On 2026-08-24, the user approved a temporary, narrowly scoped exception to this repository's MIT-only policy for that table while H264Sharp's provenance is corrected. Upgrade to the corrected H264Sharp release and remove this exception before a supported package release.

The deterministic neutral 16x16 Annex-B fixture embedded in the protocol specifications was generated with `H264Sharp.Encoder` at the same tagged release and commit. The encoder is MIT-licensed, depends only on `H264Sharp.Decoder`, and is not a package dependency of this repository.

## Avalonia Desktop 12.1.1

- Source: <https://github.com/AvaloniaUI/Avalonia/tree/12.1.1>
- Package source: <https://www.nuget.org/>
- Direct package IDs: `Avalonia.Desktop` and `Avalonia.Themes.Fluent`
- Version: `12.1.1`
- Primary license: MIT
- Purpose: Cross-platform desktop windowing, controls, styling, accessibility, and rendering for the Windows and Linux camera sample.
- Reviewed: 2026-08-24

The audited managed Avalonia closure is `Avalonia`, `Avalonia.BuildServices` 11.3.2, `Avalonia.FreeDesktop`, `Avalonia.FreeDesktop.AtSpi`, `Avalonia.HarfBuzz`, `Avalonia.Native`, `Avalonia.Remote.Protocol`, `Avalonia.Skia`, `Avalonia.Win32`, and `Avalonia.X11`; Avalonia packages are version 12.1.1 unless noted. Other managed dependencies are `MicroCom.Runtime` 0.11.6 and `Tmds.DBus.Protocol` 0.94.1. These components are MIT-licensed.

## Avalonia native renderer dependencies

- `SkiaSharp` and its platform native-asset packages: 3.119.4
- `HarfBuzzSharp` and its platform native-asset packages: 8.3.1.3
- `Avalonia.Angle.Windows.Natives`: 2.1.27548.20260419
- Purpose: Text shaping and hardware/software rendering required by Avalonia Desktop.
- Reviewed: 2026-08-24

SkiaSharp and HarfBuzzSharp's .NET bindings are MIT-licensed. The native Google Skia renderer is BSD-3-Clause. ANGLE is distributed under a BSD-style license and carries notices for its bundled third-party components. The user explicitly approved this narrow native-renderer exception on 2026-08-24. Redistributed desktop binaries must preserve all license and third-party notices supplied by these packages. Repeat this audit before updating any listed version.

The project targets the .NET platform but does not redistribute its source as part of the library package.

