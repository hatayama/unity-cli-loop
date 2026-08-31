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
  against a published release: publishing a **stable** dispatcher release makes the
  `dispatcher-publish` workflow open a pull request that carries the new stamp (pre-releases are
  never stamped on `main`). Never edit by hand — `VerifyDispatcherPinSubjects` requires the
  manifest to match the published release's verified subjects exactly, so a hand-written value
  cannot pass CI (see `docs/dispatcher-pin-release-order.md`). Consumers: Unity's **Install CLI**
  path (via `CliPinReader` / `NativeCliCommandBuilder`) and the terminal installers
  (`scripts/install.sh` / `scripts/install.ps1`) when `ULOOP_ARCHIVE_MANIFEST` is unset — they
  fetch this pin and use `dispatcherReleaseTag` as the install target plus
  `dispatcherArchiveManifest` as the attestation digest list.

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

## Which runner actually ran

`resolveDispatcherRealCLI` picks the runner in this order, and **no response field says which
branch won**:

1. `ULOOP_PROJECT_RUNNER_PATH` (env override).
2. The sibling `uloop-project-runner` next to the dispatcher binary — taken only when the pin's
   `projectRunnerVersion` equals the version compiled into *that* dispatcher
   (`clicontract.ProjectRunnerVersion()`).
3. `<cache root>/versions/<projectRunnerVersion>/<platform>/` (already downloaded).
4. GitHub release download.

Read this before trusting a dogfooding result. All four points were measured on 2026-07-27.

- Running `dist/<platform>/uloop` **without** the override still uses your local build, because
  step 2 finds `dist/<platform>/uloop-project-runner` beside it. Verified by pointing
  `ULOOP_CACHE_DIR` at an empty directory: the command succeeded and the directory stayed empty,
  so neither the cache nor a download was involved. The override is therefore not what makes a
  run local — the dispatcher you typed is.
- Running the installed `uloop` (`~/.local/bin/uloop`, which has no sibling runner) without the
  override silently uses whatever step 3 or 4 supplies, i.e. the released runner.
- Step 2 compares against a version baked into the dispatcher at build time, so it stops
  applying as soon as the pin moves ahead of your last `scripts/build-go-cli.sh`. That does not
  necessarily fail loudly: when the cache already holds that version, step 3 quietly succeeds
  with the released runner instead.
- A version number does not identify the binary. A local build and the released runner both
  answer `3.0.0-beta.58` to `uloop-project-runner --version` while their SHA-256 differ. (`version`
  as a *subcommand* is rejected by the runner on purpose — it belongs to the dispatcher — so only
  the flag form reports the runner's own version.)

Until the dispatcher reports the resolved runner path, comparing SHA-256 by hand is the only
reliable way to establish which binary served a verification run.

## Pin format discipline

The pin evolves additively only — never delete or rename an existing field.
The forced-update instruction (`minimumDispatcherVersion`) travels inside the
pin, so an old dispatcher that cannot parse a new pin never learns it must
update. For the same reason the dispatcher must stay lenient when reading pins
written by older packages.
