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

### First-install provenance decision

The repository owner approved the following first-install trust model.

- CLI-only installation requires a preinstalled GitHub CLI (`gh`) obtained
  through the user's operating-system or package channel. The installer never
  downloads or bootstraps `gh`. The README bootstrap downloads the installer
  script and its Sigstore bundle from an immutable dispatcher Release, verifies
  the script with `gh attestation verify`, and only then executes it. The
  verified installer verifies the selected archive and its bundle before
  extraction or execution. A missing `gh` is a hard failure.
- Unity installation treats the already-installed Unity package as its trust
  root. Package-release automation verifies the dispatcher Release attestation
  and stamps its verified subject digests into the package pin. The device
  enforces those digests by SHA-256 pinning before it executes a downloaded
  installer script, so Unity installation does not require external `gh` or an
  embedded verifier.
- CLI-only verification enforces the exact repository, signer workflow
  `.github/workflows/dispatcher-publish.yml`, an allowed source ref of
  `refs/heads/main` or `refs/heads/v3-beta`, an attested source digest equal to
  the resolved immutable Release tag commit, and a subject digest equal to the
  downloaded installer or archive. Package-release automation verifies that
  same policy before it stamps Unity's pinned subject digests. A same-origin
  checksum provides integrity only and is never authentication.
- Offline first installation is unsupported. Network or GitHub API failure,
  missing verifier, missing or malformed bundle, identity mismatch, tag
  mismatch, and digest mismatch all fail closed before script or binary
  execution. There is no checksum-only fallback and no mutable-branch
  `curl | sh` or `irm | iex` path.

OS-native signing remains a later defense-in-depth release improvement; it is
not a prerequisite for this bootstrap verification work.

Dispatcher Release assets referenced by a package pin must remain available
permanently. Package releases depend on the pinned dispatcher installer in the
same way that they depend on their pinned project-runner release. Release order
is dispatcher publish, `stamp-dispatcher-pin`, then package release. If a
dispatcher release must be revoked, publish a replacement, raise the package's
`minimumDispatcherVersion`, stamp the replacement release, and publish a new
package; never silently repoint or replace an existing Release asset.

### Pinned dispatcher release lifetime

A package pin can continue to authenticate an older dispatcher Release after
that dispatcher has a known vulnerability. Pinning proves provenance and
integrity; it does not revoke already published content. The mitigation is the
package's minimum dispatcher version gate: publish a fixed dispatcher, raise
the minimum, stamp its digests, and publish an updated package. Treat Unity
projects from untrusted sources as untrusted inputs because they can select
their own pin and minimum-version requirements.

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
