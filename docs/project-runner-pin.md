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
  `dispatcher-publish` workflow push the new stamp straight to `main` (pre-releases are
  never stamped on `main`), and CI on `main` fails through `check-dispatcher-pin-freshness` for as
  long as `dispatcherReleaseTag` lags the newest published stable dispatcher release. Never edit by hand — `VerifyDispatcherPinSubjects` requires the
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

## Periodic dispatcher self-update

Separately from the `minimumDispatcherVersion` floor, the dispatcher checks for the newest
release on its own, independent of any pin, V2 or V3 project, or working directory.

- **When**: on every command, at most once per 24 hours. Excluded are `uloop update`,
  `install`, `uninstall`, `version`, and help and version requests (including a project-scoped
  `--version` that is forwarded to the runner). `install` and `uninstall` are excluded because
  installers and the Unity settings window run them, and a second installer must not start
  from inside the first.
- **Effect**: a newer release is installed while the command runs, but that command finishes
  on the old dispatcher; the next command uses the new one.
- **Where it runs**: only when the running `uloop` sits in the install directory
  (`ULOOP_INSTALL_DIR`, otherwise the OS default). The installer only replaces that
  directory, so a dev build or a hand-extracted copy would rewrite the global install every
  day while staying old.
- **Never runs**: under Homebrew or winget, or with `ULOOP_DISABLE_SELF_UPDATE=1|true`. The
  installer child process always gets `ULOOP_DISABLE_SELF_UPDATE=1`.
- **Record and lock**: the cache root (`ULOOP_CACHE_DIR`, otherwise the OS cache directory)
  holds `dispatcher-update.json` (the last check, written *before* the lookup; no lookup runs
  when it cannot be written) and `dispatcher-update.lock`. The lock is an OS file lock on that
  file (`flock` on macOS / Linux, `LockFileEx` on Windows), so it is released when the holding
  process exits and never goes stale; the file itself stays in place. The required update for
  `minimumDispatcherVersion` takes the same lock; when another process holds it, the command
  fails with a retryable `CLI_UPDATE_REQUIRED` instead of starting a second installer.
  `uloop update` takes it before touching the hold and exits with a retryable error while
  another process holds it. When the periodic update
  already installed a release in the same process, the required check asks for a retry
  instead of installing again.
- **Required update**: below the `minimumDispatcherVersion` floor, the dispatcher installs the
  newest release on its channel, or the floor itself when that release is older (a beta floor
  while the newest stable release is older installs the beta, and later automatic updates then
  follow the beta channel). If the dispatcher is still below the floor afterwards, the command
  fails with a manual-update error instead of asking for a retry that would repeat forever.
- **Time limits**: the release lookup is cut off after 10 seconds. The installer has no time
  limit, because killing it can leave Windows without `uloop.exe`; a stderr line announces the
  install so a slow download is visible.

### Version hold

`uloop update --to-version <version>` installs that release and records a version hold in
the per-user config directory (`ULOOP_CONFIG_DIR`, otherwise
`~/Library/Application Support/uloop` on macOS, `%APPDATA%\uloop` on Windows,
`$XDG_CONFIG_HOME/uloop` or `~/.config/uloop` elsewhere) as `dispatcher-version-hold.json`.
While the file exists — even if it cannot be read — the periodic self-update does not run.

- Plain `uloop update` removes the hold and updates to the newest release.
- Reinstalling with `install.sh` / `install.ps1` does not remove the hold.
- When a held dispatcher is below a project's `minimumDispatcherVersion`, the command fails
  with a manual-update error naming the hold instead of updating automatically. A Homebrew or
  winget install, or `ULOOP_DISABLE_SELF_UPDATE`, takes precedence and is the reason that error
  names instead.
