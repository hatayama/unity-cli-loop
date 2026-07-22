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
- `Assets/RegressionHarness/<TrapName>/` — permanent manual repro scenes for verification-round "traps" (pause-point, simulate-keyboard, physics callbacks, etc.), paired with a driver script under `scripts/regression-harness-<trap-name>.sh`. See `docs/regression-harness.md`.
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
protocol version. `protocolVersion` in `cli/common/clicontract/contract.json` and
`CliConstants.REQUIRED_CLI_PROTOCOL_VERSION` must always stay equal — never bump one alone,
and bump them (together, in the same PR) only when the IPC wire format becomes incompatible
between generations; ordinary features and fixes must not bump it. Release version fields
(`projectRunnerVersion`, `dispatcherVersion`, changelogs) are stamped by release-please only —
never edit them by hand in a feature PR (sole exception: the documented version-series
realignment; see `docs/version-series-realignment.md`). Bump criteria and release sequencing:
`docs/protocol-version.md`.

## Project Runner Pin

`Packages/src/project-runner-pin.json` (mirrored byte-identically to
`.uloop/project-runner-pin.json`) is the single source for cross-component version
requirements. `minimumDispatcherVersion` is the only manually maintained field — raise it only
when the package genuinely needs a newly published dispatcher. All other fields are stamped by
automation; never edit them by hand. The pin evolves additively only — never delete or rename
an existing field. Field reference and rationale: `docs/project-runner-pin.md`.

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
changelogs are stamped by release PRs — never bump them by hand (sole exception: the
documented version-series realignment; see `docs/version-series-realignment.md`). Changes to shared release
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

When running `uloop` commands for this project during CLI development, do not use the `uloop`
resolved from `PATH`. Run this checkout's built development binary (rebuilt after relevant CLI
source changes) so validation uses the code under review:

```bash
dist/darwin-arm64/uloop compile --project-path "$(git rev-parse --show-toplevel)"
```

Substitute the binary for your platform (e.g. `dist/windows-amd64/uloop.exe` on Windows).

Before running a command with `--project-path`, confirm the path is the intended Unity project
for the current task — do not copy a sibling checkout path from another repository or session.

When changing Go source files under any Go module (`cli/common`, `cli/dispatcher`,
`cli/project-runner`, `cli/release-automation`), run `scripts/check-go-cli.sh` — the local
equivalent of Go CLI CI (format, vet, lint, tests, binary rebuild). Use `scripts/build-go-cli.sh`
to refresh `dist` binaries; they are git-ignored and must not be committed.

To validate an unreleased project runner from an external Unity project, set the
`ULOOP_PROJECT_RUNNER_PATH` environment variable to a locally built binary — it overrides the
pin-based resolution entirely (see `docs/project-runner-pin.md`).

## Unity Freeze Prevention

Unity EditMode tests can freeze the Editor. Never run multiple `uloop run-tests` commands in
parallel — Unity Test Runner is single-flight only. Before adding or modifying Unity EditMode
tests (especially anything touching async execution, cancellation, threads, or dynamic-code
runtime paths), read `docs/unity-editmode-test-guardrails.md` and follow its rules. If a new
test makes `uloop run-tests` stall, remove or disable it instead of retrying the suite. If
Unity freezes or stops responding to `uloop`, restart the Editor with `uloop launch -r`.
