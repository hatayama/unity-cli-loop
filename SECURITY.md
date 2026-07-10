# Security Policy

## Reporting a Vulnerability

Please do not create a public GitHub issue for security vulnerabilities.

Use GitHub's [Private Vulnerability Reporting](https://github.com/hatayama/unity-cli-loop/security/advisories/new) feature instead.

When reporting a vulnerability, please include:

- A description of the vulnerability
- Steps to reproduce the issue
- Potential impact
- Any suggested fixes, if available

## Trust Model and Known Risks

### Untrusted Unity projects can drive the runner version

At runtime `uloop` looks up the project-runner pin from the current
Unity project to decide which project-runner release to download and
execute. The read order is `.uloop/project-runner-pin.json` first, then
a fallback to `Packages/src/project-runner-pin.json`, and finally the
installed package copy under `Packages/<package>/`. The authoring source
is `Packages/src/project-runner-pin.json`; `CliPinSynchronizer` copies
it byte-identically to `.uloop/project-runner-pin.json` when Unity
opens the project, so the runtime lookup usually resolves inside
`.uloop/` before the source ever gets consulted. The pin is
project-local data: opening or cloning a third-party Unity project and
running `uloop` inside it means the project's pin controls which
release `uloop` will pull down and launch.

Concrete implications:

- A hostile project can pin the runner to a known-vulnerable published
  release and downgrade you to it. `uloop` verifies the release's Sigstore
  attestation before extracting the archive, so an attacker cannot forge a
  new asset — but they *can* select an older release already published by
  this repository.
- Setting `ULOOP_DISABLE_SELF_UPDATE=1` only disables the dispatcher's own
  auto-update. It does not disable pin-driven runner selection, because the
  runner version is part of the project contract, not the dispatcher's
  update policy.
- Attestation verification (`Sigstore` bundles) rules out unpublished
  binaries: an attacker cannot make `uloop` execute an archive that was
  not built and signed by the official release workflow.

Guidance:

- Only run `uloop` inside Unity projects whose maintainers you trust to
  choose your runner version.
- When auditing a third-party project, inspect **both**
  `.uloop/project-runner-pin.json` and
  `Packages/src/project-runner-pin.json` before running any `uloop`
  command in it. The two files must be byte-identical — any divergence
  is itself a red flag, because the runtime path (`.uloop/`) is what
  actually decides which release runs, and a mismatched source
  (`Packages/src/`) means the mirror step never completed or was
  bypassed.
- `ULOOP_DISABLE_SELF_UPDATE=1` is still worth setting in one-off audit
  environments because it prevents the dispatcher from silently
  upgrading itself while you are investigating, but treat it as a
  layered defense, not a substitute for reading the pin.
