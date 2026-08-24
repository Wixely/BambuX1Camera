# Third-party notices

## H264Sharp.Decoder 1.0.2

- Source: <https://github.com/Wixely/H264Sharp/tree/v1.0.2>
- Current licensing clarification: <https://github.com/Wixely/H264Sharp#licensing>
- Package source: <https://nuget.pkg.github.com/Wixely/index.json>
- Package ID: `H264Sharp.Decoder`
- Version: `1.0.2`
- Release commit: `42019859ba0de5cd8290cbfc71f2e28751edfe37`
- Primary license: MIT
- Purpose: Pure-managed H.264 GOP and keyframe decoding, YUV-to-RGB conversion, and PNG encoding in the optional `BambuLab.X1Camera.Imaging` package.
- Transitive package dependencies: None. The .NET SDK supplies framework and build-time components.
- Reviewed: 2026-08-24

The tagged release's `LICENSE` declares H264Sharp under MIT. Its `CabacInitTable.cs` header and `LICENSE-3RDPARTY.md` preserve a precautionary BSD-2-Clause attribution to Cisco OpenH264 for CABAC context-initialization values corresponding to ITU-T H.264 Tables 9-12 through 9-24. The same attribution file states that formulas, syntax structures, and lookup tables defined directly by the H.264 specification are not subject to third-party copyright. The current upstream README further clarifies that these values are standard-mandated facts, OpenH264 merely transcribes them, and the precautionary attribution does not change H264Sharp's MIT licensing. This project retains the attribution; no OpenH264 source or binary is copied, linked, packaged, or distributed.

The deterministic neutral 16x16 Annex-B fixture embedded in the protocol specifications was generated with `H264Sharp.Encoder` at the same tagged release and commit. The encoder is MIT-licensed, depends only on `H264Sharp.Decoder`, and is not a package dependency of this repository.

## Application icon

- Source: Text-only generation using OpenAI built-in image generation; no input images or third-party marks were used.
- Generated: 2026-08-24
- Purpose: Executable and Avalonia window icon for the desktop sample.
- License: Distributed as a project-owned asset under MIT.
- Provenance and prompts: `samples/BambuLab.X1Camera.Desktop/Assets/README.md`

The generated source was resampled into the committed metadata-free PNG and multi-resolution ICO using the platform `System.Drawing` APIs. No image-generation or conversion tool is shipped with the project.

## Avalonia Desktop 12.1.1

- Source: <https://github.com/AvaloniaUI/Avalonia/tree/12.1.1>
- Package source: <https://www.nuget.org/>
- Direct package IDs: `Avalonia.Desktop` and `Avalonia.Themes.Fluent`
- Version: `12.1.1`
- Primary license: MIT
- Purpose: Cross-platform desktop windowing, controls, styling, accessibility, and rendering for the Windows and Linux camera sample.
- Boundary: Avalonia is referenced only by the non-packable desktop sample and is not a dependency of either NuGet library package.
- Reviewed: 2026-08-24

The audited managed Avalonia closure is `Avalonia`, `Avalonia.BuildServices` 11.3.2, `Avalonia.FreeDesktop`, `Avalonia.FreeDesktop.AtSpi`, `Avalonia.HarfBuzz`, `Avalonia.Native`, `Avalonia.Remote.Protocol`, `Avalonia.Skia`, `Avalonia.Win32`, and `Avalonia.X11`; Avalonia packages are version 12.1.1 unless noted. Other managed dependencies are `MicroCom.Runtime` 0.11.6 and `Tmds.DBus.Protocol` 0.94.1. These components are MIT-licensed.

## Avalonia native renderer dependencies

- `SkiaSharp` and its platform native-asset packages: 3.119.4
- `HarfBuzzSharp` and its platform native-asset packages: 8.3.1.3
- `Avalonia.Angle.Windows.Natives`: 2.1.27548.20260419
- Purpose: Text shaping and hardware/software rendering required by Avalonia Desktop.
- Reviewed: 2026-08-24

SkiaSharp and HarfBuzzSharp's .NET bindings are MIT-licensed. The native Google Skia renderer is BSD-3-Clause and is admitted under the repository's general BSD-3-Clause policy approved on 2026-08-24. ANGLE is distributed under a BSD-style license and carries notices for its bundled third-party components; its previously approved narrow exception remains in force until every applicable license in that closure is classified independently. Redistributed desktop binaries must preserve all license and third-party notices supplied by these packages. Repeat this audit before updating any listed version.

The project targets the .NET platform but does not redistribute its source as part of the library package.

## GitHub Actions workflow

- Source: Independently authored repository workflow.
- Purpose: Windows/Linux verification, tagged NuGet package publishing, and GitHub prereleases containing the standalone Avalonia sample executables.
- External action dependencies: None. The workflow intentionally contains no `uses:` steps.
- Hosted tools: Git, PowerShell, and the .NET SDK supplied by GitHub-hosted `windows-2025` and `ubuntu-24.04` runner images.
- License: Project workflow code is released under MIT with the repository.
- Reviewed: 2026-08-24

The hosted runner operating systems and preinstalled development tools are build infrastructure rather than code, binaries, or dependencies distributed by this project. Package contents are produced only from the audited repository projects and their declared package dependencies. Desktop release assets are produced from the audited sample dependency closure, and each release includes a separate archive containing the project license plus the exact platform renderer notices copied from the published outputs. Release preparation removes absolute build-machine paths from non-runtime CodeView PDB references embedded by dependencies, retaining only each PDB filename; executable code and dependency attribution are unchanged.

