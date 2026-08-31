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

The repository owner approved the following first-install trust model
(issue #2080 replaces the earlier decision that CLI-only install required `gh`
and forbade a mutable-branch `curl | sh` / `irm | iex` path).

- The default CLI-only install trusts the repository pin
  (`Packages/src/project-runner-pin.json` → `dispatcherArchiveManifest`).
  Release automation stamps that digest list after verifying the published
  release's attestation subjects, and CI requires an exact match. The
  installer fetches the pin over TLS from `raw.githubusercontent.com` on a
  protected branch (default `main`; override with `ULOOP_REF`) and enforces
  those digests by SHA-256 before extraction.
- An explicit `ULOOP_ARCHIVE_MANIFEST` (typically from a Sigstore-verified
  attestation, as produced by `gh attestation verify` on the installer's
  `.sigstore.json` bundle) always takes precedence over the pin, so a
  release tag other than the pin can be installed as a hardened option.
- Unity installation uses the same pin fields from the already-installed
  package, so terminal and GUI share one trust root. Remaining gaps (Sigstore
  chains to the signing workflow; repository trust chains to branch
  protection) are identical across those paths — this change introduces no
  new regression relative to the Unity GUI install.
- Offline first installation is unsupported. Network failure, a missing or
  malformed pin, an invalid `ULOOP_REF`, a tag/`ULOOP_VERSION` mismatch, and
  digest mismatch all fail closed before script or binary execution. There is
  no checksum-only fallback.

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
