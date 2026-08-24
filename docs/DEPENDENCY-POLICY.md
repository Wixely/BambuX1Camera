# Dependency and source policy

- Status: Confirmed requirement
- Recorded: 2026-08-24
- Source: User requirement

## Rule

All project code must be either:

1. independently authored for this project and released under this project's MIT license; or
2. taken from a dependency or source whose applicable code is licensed under the MIT license.

Code under another license must not be copied, adapted, vendored, linked, packaged, or introduced as a direct or transitive dependency. This rule also applies to sample code, generated code incorporated into the repository, browser assets, build tools shipped with the product, native binaries, and GitHub Actions used by the project.

Protocol facts and observed wire behavior may inform an independently authored implementation. Source code from a non-MIT project may be used as neither implementation material nor a copy-and-modify starting point. Record the behavioral source and the clean-room derivation where that distinction could later be unclear.

## Current position

- The repository code is independently authored and licensed under MIT.
- The projects currently declare no third-party NuGet package dependencies.
- The .NET target framework is supplied by the platform and is not bundled as third-party source in the library package.
- No Bambu binaries, camera captures, or third-party source files are included.

## Admission checklist

Before adding or updating any dependency, asset, workflow action, copied snippet, generated source input, or vendored tool:

- Verify the exact version and canonical source.
- Verify that the applicable code is MIT-licensed; permissive but different licenses such as Apache-2.0, BSD, ISC, MPL, LGPL, or public-domain dedications do not satisfy this project's stricter rule.
- Inspect transitive dependencies and bundled binaries under the same rule.
- Record the component, version, source, license evidence, purpose, and review date in `THIRD-PARTY-NOTICES.md`.
- Reject the component if its license, provenance, generated contents, or transitive closure cannot be established.
- Re-run the license check before each package release.

An exception requires a new explicit user decision that supersedes this policy.

## Temporary approved exception

- Component: `H264Sharp.Decoder` 1.0.2
- Scope: The generated CABAC initialization table attributed by H264Sharp to BSD-2-Clause OpenH264.
- Decision: The user explicitly approved using the current package temporarily on 2026-08-24 while H264Sharp's table provenance is corrected.
- Boundary: This does not permit any other non-MIT dependency, copied source, asset, tool, or generated input.
- Required follow-up: Upgrade to the corrected H264Sharp release, repeat the dependency audit, update `THIRD-PARTY-NOTICES.md`, and remove this exception before a supported package release.
- Review date: 2026-09-30

