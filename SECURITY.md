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

`uloop` reads `Packages/src/project-runner-pin.json` (which
`CliPinSynchronizer` mirrors byte-identically to
`.uloop/project-runner-pin.json`) from the current Unity project to decide
which project-runner release to download and execute. The pin is
project-local data: opening or cloning a third-party Unity project and
running `uloop` inside it means the project's pin controls which release
`uloop` will pull down and launch.

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
- When auditing a third-party project, inspect the source pin at
  `Packages/src/project-runner-pin.json` (and its mirror at
  `.uloop/project-runner-pin.json` if only that path is present)
  before running any `uloop` command in it.
- `ULOOP_DISABLE_SELF_UPDATE=1` is still worth setting in one-off audit
  environments because it prevents the dispatcher from silently
  upgrading itself while you are investigating, but treat it as a
  layered defense, not a substitute for reading the pin.
