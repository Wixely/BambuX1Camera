# Agent instructions

- Keep the core library managed C# and free of Bambu proprietary binaries, P/Invoke, and external processes.
- License all independently authored project code under MIT. Third-party code and dependencies may be MIT-licensed or BSD-3-Clause-licensed; do not copy, adapt, vendor, link, package, or depend directly or transitively on code under another license without explicit approval.
- Treat protocol documentation and observed behavior as facts only. Independently implement the behavior; do not copy implementation code from sources whose licences are not approved by this policy.
- Before adding any dependency, source snippet, generated code input, browser asset, binary, tool distributed with the project, or GitHub Action, follow `docs/DEPENDENCY-POLICY.md` and update `THIRD-PARTY-NOTICES.md`.
- Target .NET 10. Treat Windows as the primary platform and Linux as the secondary platform.
- Do not add a decoder to the core transport library. Emit encoded H.264 Annex-B NAL units.
- Never log or persist printer access codes. Treat printer addresses, serials, and certificate fingerprints as private configuration.
- Require normal certificate validation or an explicitly configured SHA-256 certificate pin by default. Insecure certificate acceptance is for isolated diagnostic use only.
- Use PowerShell for repository scripts. Do not introduce Node.js or Python tooling.
- Add bounded parser tests for every new wire-format feature.
