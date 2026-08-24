# Dependency and source policy

- Status: Confirmed requirement
- Recorded: 2026-08-24
- Updated: 2026-08-24
- Source: User requirements

## Rule

All project code must be either:

1. independently authored for this project and released under this project's MIT license; or
2. taken from a dependency or source whose applicable code is licensed under the MIT license; or
3. taken from a dependency or source whose applicable code is licensed under the BSD-3-Clause license.

Code under any other license must not be copied, adapted, vendored, linked, packaged, or introduced as a direct or transitive dependency without a separately recorded explicit user approval. This rule also applies to sample code, generated code incorporated into the repository, browser assets, build tools shipped with the product, native binaries, and GitHub Actions used by the project.

Protocol facts and observed wire behavior may inform an independently authored implementation. Source code under a license other than MIT or BSD-3-Clause may be used as neither implementation material nor a copy-and-modify starting point unless explicitly approved. Record the behavioral source and the clean-room derivation where that distinction could later be unclear.

## Current position

- The repository code is independently authored and licensed under MIT.
- The imaging package declares the MIT-licensed `H264Sharp.Decoder` dependency described below.
- The non-packable desktop sample declares Avalonia 12.1.1 and its audited renderer closure. Google Skia is admitted under the BSD-3-Clause rule; the separately approved ANGLE closure is described below. Neither NuGet library package references Avalonia.
- The .NET target framework is supplied by the platform and is not bundled as third-party source in the library package.
- No Bambu binaries, camera captures, or third-party source files are included.

## Admission checklist

Before adding or updating any dependency, asset, workflow action, copied snippet, generated source input, or vendored tool:

- Verify the exact version and canonical source.
- Verify that the applicable code is MIT-licensed or BSD-3-Clause-licensed. Similar but distinct licenses such as Apache-2.0, BSD-2-Clause, ISC, MPL, LGPL, or public-domain dedications do not satisfy this project's stricter rule without explicit approval.
- Inspect transitive dependencies and bundled binaries under the same rule.
- Record the component, version, source, license evidence, purpose, and review date in `THIRD-PARTY-NOTICES.md`.
- Reject the component if its license, provenance, generated contents, or transitive closure cannot be established.
- Re-run the license check before each package release.

An exception for any other license requires a new explicit user decision that supersedes this policy for the recorded scope.

## H264Sharp licensing determination

- Component: `H264Sharp.Decoder` 1.0.2
- Primary license: MIT, as declared by the tagged release's `LICENSE` and package metadata.
- Attribution: The tagged `LICENSE-3RDPARTY.md` preserves a precautionary BSD-2-Clause attribution for CABAC context-initialization values transcribed through OpenH264.
- Specification status: The same file states that formulas, syntax structures, and lookup tables defined directly by ITU-T H.264 are not subject to third-party copyright. The current upstream README identifies the values as mandated by ITU-T H.264 Tables 9-12 through 9-24 and says the precautionary attribution does not alter the MIT license.
- Decision: Admit H264Sharp as an MIT dependency and preserve its precautionary attribution. No separate dependency-policy exception is required.
- Boundary: No OpenH264 source or binary is copied, linked, packaged, or distributed by this project.
- Reviewed: 2026-08-24

## Avalonia renderer determination and remaining exception

- Components: Google Skia native renderer under BSD-3-Clause and ANGLE Windows native renderer under its BSD-style license and bundled third-party notices.
- Scope: Native renderer binaries transitively required by Avalonia Desktop 12.1.1 for the non-packable Windows and Linux sample application. Neither NuGet library package references or distributes Avalonia.
- Decision: The user approved BSD-3-Clause generally on 2026-08-24, so Google Skia is admitted by the base policy and no longer requires an exception. The previously approved narrow exception remains in force for ANGLE and its bundled third-party closure until every applicable license is classified independently.
- Boundary: Avalonia's managed packages and the remaining transitive packages must remain MIT-licensed or BSD-3-Clause-licensed, except for the recorded ANGLE closure. This exception does not authorize unrelated dependencies, source, assets, tools, or generated inputs under other licenses.
- Required review: Repeat the complete renderer and transitive-license audit before updating Avalonia or publishing desktop binaries.
- Review date: 2026-09-30

