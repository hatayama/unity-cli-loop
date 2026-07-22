# Project runner pin

`Packages/src/project-runner-pin.json` (mirrored byte-identically to
`.uloop/project-runner-pin.json` by `CliPinSynchronizer`) is the single source
for cross-component version requirements. Its required fields:

- `projectRunnerVersion` — the project runner release the dispatcher must run for this package.
  Stamped by release-please; never edit by hand.
- `minimumDispatcherVersion` — the semver floor the package requires of the globally installed
  dispatcher. The dispatcher force-updates itself when it is older than this value, and the
  package reads it (via `CliPinReader`) for setup and installation checks. This is the only
  manually maintained minimum-version declaration; raise it only when the package genuinely
  needs a newly published dispatcher, not because the dispatcher implementation changed.
- `dispatcherReleaseTag` and `dispatcherArchiveManifest` — the provenance-pinned dispatcher
  release used for first installation and its verified asset hashes. Stamped by automation
  against a published release; never edit by hand — `VerifyDispatcherPinSubjects` requires the
  manifest to match the published release's verified subjects exactly, so a hand-written value
  cannot pass CI (see `docs/dispatcher-pin-release-order.md`).

There is no dispatcher⇄package integer contract generation; the pin's semver
floor is the only dispatcher gate. The IPC `protocolVersion` pair (see
`docs/protocol-version.md`) is the only integer generation in the system.

## Local override: `ULOOP_PROJECT_RUNNER_PATH`

The environment variable `ULOOP_PROJECT_RUNNER_PATH` (defined in
`cli/dispatcher/internal/nativepath/path.go`) makes the dispatcher run the
project runner binary at that path instead of resolving one from the pin.
`resolveDispatcherRealCLI` checks it before everything else — pin validation,
the sibling binary next to the dispatcher, the version cache, and the GitHub
release download are all skipped. The path must point at an existing executable
file, or the dispatcher fails with an explicit error rather than falling back.

This override exists for dogfooding checkouts: release-please stamps
`projectRunnerVersion` ahead of the matching GitHub release, so the normal
download path 404s until the release is published. Pointing the variable at a
locally built binary (e.g. `dist/darwin-arm64/uloop-project-runner`, refreshed
via `scripts/build-go-cli.sh`) lets you exercise unreleased project-runner and
`cli/common` changes against real Unity projects before merge. Unset the
variable to return to normal pin-resolved behavior.

Related overrides in the same file: `ULOOP_INSTALL_DIR` (dispatcher install
directory) and `ULOOP_CACHE_DIR` (project runner download cache).

## Pin format discipline

The pin evolves additively only — never delete or rename an existing field.
The forced-update instruction (`minimumDispatcherVersion`) travels inside the
pin, so an old dispatcher that cannot parse a new pin never learns it must
update. For the same reason the dispatcher must stay lenient when reading pins
written by older packages.
