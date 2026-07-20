## Architecture Overview

This project provides a **CLI tool (`uloop`)** that communicates with Unity Editor over local IPC
(a Unix domain socket on macOS/Linux, a named pipe on Windows — not TCP).
AI agents interact with Unity through `uloop` CLI commands (e.g., `uloop get-logs`, `uloop compile`).
The Unity Editor side hosts a local project IPC server that accepts short-lived CLI command sessions.

## Repository Map

Directory-level responsibilities. Kept deliberately coarse — check the directory itself for file-level detail.

- `Packages/src/` — the Unity package (C#). Each first-party tool lives in `Editor/FirstPartyTools/<Tool>/` with its implementation and agent skill (`Skill/SKILL.md`, optional `Skill/references/`).
- `Packages/src/Editor/CliOnlyTools~/<Tool>/Skill/` — skills for CLI-only commands (launch, pause-point, etc.) that have no Unity tool class. Tilde-suffixed folders are ignored by Unity, so files there need no `.meta`; files under `FirstPartyTools` do (run `uloop compile` to let Unity generate them).
- `Assets/` — the development/test Unity project, including custom-command samples under `Assets/Editor/CustomCommandSamples/`.
- `cli/dispatcher/` — the globally installed `uloop` entry command (also owns `uloop skills install` and skill syncing).
- `cli/project-runner/` — the per-project CLI runner that talks to the Unity-side IPC server.
- `cli/common/` — Go modules shared by dispatcher and project runner. Tool parameter schemas live in `cli/common/tools/default-tools.json`; skill discovery in `cli/common/skillscan/`.
- `cli/release-automation/` — Go logic backing GitHub Actions release/CI workflows.
- `tools/UnityCliLoop.DeadCodeScanner/` — the C# dead-code scanner described below.
- `dist/` — locally built development binaries (`dist/darwin-arm64/uloop`, etc.); never committed.
- `.claude/`, `.agents/` — generated skill copies; never edit directly (see Generated Skill Files).
- `.uloop/` — runtime state and command outputs (screenshots, test results, hierarchy dumps).

Do not rename public package, assembly, or extension API identifiers as part of cleanup-only changes.

Comments in the code, commit messages, PR titles, and PR descriptions must all be written in English.

Every test method must have a short comment that states what behavior the test verifies.

## CLI / Unity Package Compatibility

Runtime compatibility between the Unity package and the native CLI is gated on an integer
protocol version, not on release numbers. Two declarations must always stay equal:

- Go side: `protocolVersion` in `cli/common/clicontract/contract.json` (the generation the CLI advertises over IPC).
- C# side: `CliConstants.REQUIRED_CLI_PROTOCOL_VERSION` (the exact generation the package accepts).

`TestProtocolVersionMatchesUnityPackage` fails the build if they diverge, so never bump one alone.
The runtime gate expects equality because the protocol version is a contract generation, not a
minimum-compatible range.
Pull request CI also runs a non-blocking IPC protocol reminder when IPC-facing files changed
without protocol declaration changes; treat it as a review prompt, not as proof that a bump is
required.

Bump both, together, in the same PR only when the IPC contract changes in a way that makes
CLI and package builds from different protocol generations unable to interoperate — for example renaming
or removing a request field, changing the readiness/dispatch handshake, or altering a response
shape the other side parses. Ordinary CLI features and bug fixes that keep the wire format
compatible must not bump it.

Do not touch the protocol version to "keep up with releases":

- `cli/common/clicontract/contract.json` `projectRunnerVersion`, the pin files'
  `projectRunnerVersion`, and `cli/dispatcher/dispatchercontract/dispatcher-contract.json`
  `dispatcherVersion` are stamped by release-please only. Never edit them by hand in a feature PR.
- When a protocol bump changes `CliConstants.REQUIRED_CLI_PROTOCOL_VERSION`, prepare the matching
  project runner release first. PR CI (`check-protocol-minimum-version`) fails until the pin's
  `projectRunnerVersion` points at a published project runner release that advertises the
  required protocol; release-please advances that value when the runner release is cut.
- Runtime protocol mismatch guidance must use the unpinned CLI update path for older clients and
  tell newer clients to align the package and CLI releases.

## Project Runner Pin

`Packages/src/project-runner-pin.json` (mirrored byte-identically to `.uloop/project-runner-pin.json`
by `CliPinSynchronizer`) is the single source for cross-component version requirements. Its
required fields:

- `projectRunnerVersion` — the project runner release the dispatcher must run for this package.
  Stamped by release-please; never edit by hand.
- `minimumDispatcherVersion` — the semver floor the package requires of the globally installed
  dispatcher. The dispatcher force-updates itself when it is older than this value, and the
  package reads it (via `CliPinReader`) for setup and installation checks. This is the only
  manually maintained minimum-version declaration; raise it only when the package genuinely
  needs a newly published dispatcher, not because the dispatcher implementation changed.
- `dispatcherReleaseTag` and `dispatcherArchiveManifest` — the provenance-pinned dispatcher
  release used for first installation and its verified asset hashes. Stamped by automation
  against a published release; never edit by hand (see `docs/dispatcher-pin-release-order.md`).

There is no dispatcher⇄package integer contract generation; the pin's semver floor is the only
dispatcher gate. The IPC `protocolVersion` pair described above is the only integer generation
in the system.

Pin format discipline: the pin evolves additively only — never delete or rename an existing
field. The forced-update instruction (`minimumDispatcherVersion`) travels inside the pin, so an
old dispatcher that cannot parse a new pin never learns it must update. For the same reason the
dispatcher must stay lenient when reading pins written by older packages.

## Generated Skill Files

Do not directly edit skill files under the project-root `.agents/` or `.claude/` directories.
These files are generated copies. Update the source skill definitions instead, then regenerate the copies.

- Sources: `Packages/src/Editor/FirstPartyTools/<Tool>/Skill/SKILL.md` and `Packages/src/Editor/CliOnlyTools~/<Tool>/Skill/SKILL.md` (plus each skill's `references/` files, which are copied along with it).
- Regenerate: `dist/darwin-arm64/uloop skills install --claude --agents` from the project root, substituting the binary for your platform (e.g. `dist/windows-amd64/uloop.exe` on Windows). Only `.claude/` and `.agents/` are tracked in git; other targets are local-only.

## CI Automation Language

Write GitHub Actions and release automation logic in Go when it needs JSON parsing, workflow polling, state transitions, or non-trivial branching.
Shell scripts are acceptable only as thin wrappers or simple command sequences.

## Shared Release Inputs and Triggers

All three components release through release-please; `dispatcherVersion` and component
changelogs are stamped by release PRs — never bump them by hand. Changes to shared release
inputs outside the package roots (non-test `cli/common/**` sources, `scripts/install.sh`,
`scripts/install.ps1`) need matching trigger changes and a `scripts/stamp-release-inputs.sh`
run in the same PR; CI (`check-release-triggers`) fails otherwise. Rules and rationale:
`docs/shared-release-inputs.md`.

## Windows Compatibility Guardrails

Most day-to-day development happens on macOS, but this project must keep working on Windows.
Before changing scripts, skill files, generated-file synchronization, path handling, or text
parsing, read `docs/windows-compatibility.md` (encoding, line endings, path separators,
PowerShell validation). Add a regression test whenever a fix depends on encoding, line endings,
or separator normalization — a passing macOS test alone is not enough for these cases.

## Dead Code Scanner

Before deleting apparently unreferenced C# code, or before adding comments explaining why an
apparently unreferenced type must stay, run the scanner and interpret its output conservatively
as described in `docs/dead-code-scanner.md` (commands, and what `KeptByUnityOrReflection` /
`PublicCandidate` do and do not prove).

## Native Go CLI Validation

When running `uloop` commands for this project during CLI development, do not use the `uloop` command resolved from `PATH`. Run this checkout's built development binary directly so validation uses the code under review:

```bash
dist/darwin-arm64/uloop compile --project-path "$(git rev-parse --show-toplevel)"
```

Before running a command with `--project-path`, confirm that the path is the intended Unity project for the current task. Do not copy a sibling checkout path from another repository or prior session. When intentionally validating a different Unity project, use an explicit placeholder in notes and replace it at execution time:

```bash
dist/darwin-arm64/uloop compile --project-path <UNITY_PROJECT_ROOT>
```

If CLI source changes affect the command behavior you are validating, rebuild the development binary before running it.

When changing Go source files under any of the Go modules (`cli/common`, `cli/dispatcher`, `cli/project-runner`, `cli/release-automation`), run `scripts/check-go-cli.sh`.
Use `scripts/build-go-cli.sh` when you need to refresh local development binaries under `dist`; generated binaries are ignored and must not be committed.
This script is the local equivalent of the Go CLI CI validation: it runs formatting checks, vet, lint, tests, rebuilds the built native binaries, and verifies that required platform binaries exist.

## Unity Freeze Prevention

Unity EditMode tests can freeze the Editor. Never run multiple `uloop run-tests` commands in
parallel — Unity Test Runner is single-flight only. Before adding or modifying Unity EditMode
tests (especially anything touching async execution, cancellation, threads, or dynamic-code
runtime paths), read `docs/unity-editmode-test-guardrails.md` and follow its rules. If a new
test makes `uloop run-tests` stall, remove or disable it instead of retrying the suite. If
Unity freezes or stops responding to `uloop`, restart the Editor with `uloop launch -r`.
