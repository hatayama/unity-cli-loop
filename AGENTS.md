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
by `CliPinSynchronizer`) is the single source for cross-component version requirements. It
currently has two required fields:

- `projectRunnerVersion` — the project runner release the dispatcher must run for this package.
  Stamped by release-please; never edit by hand.
- `minimumDispatcherVersion` — the semver floor the package requires of the globally installed
  dispatcher. The dispatcher force-updates itself when it is older than this value, and the
  package reads it (via `CliPinReader`) for setup and installation checks. This is the only
  manually maintained minimum-version declaration; raise it only when the package genuinely
  needs a newly published dispatcher, not because the dispatcher implementation changed.

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

The dispatcher is released through release-please like the project runner and the Unity
package: `cli/dispatcher/dispatchercontract/dispatcher-contract.json` `dispatcherVersion` and `cli/dispatcher/CHANGELOG.md`
are stamped by release-please release PRs. Never bump `dispatcherVersion` by hand.

release-please attributes a commit to a component only when the commit touches that package
root (`Packages/src/`, `cli/dispatcher/`, `cli/project-runner/`). Shared release inputs living outside
those roots therefore need explicit trigger updates in the same PR:

- Common module sources (non-test `cli/common/**/*.go`, `cli/common/go.mod`, `cli/common/go.sum`) must be
  accompanied by changes under both `cli/project-runner/` and `cli/dispatcher/`.
- Installer scripts (`scripts/install.sh`, `scripts/install.ps1`) must be accompanied by a
  change under `cli/dispatcher/`, because installers ship as dispatcher release assets.

Run `scripts/stamp-release-inputs.sh` to refresh `cli/project-runner/shared-inputs-stamp.json` and
`cli/dispatcher/shared-inputs-stamp.json`, and commit the stamp updates with the change. Pull
request CI runs `check-release-triggers` (authoritative rules: `releaseTriggerRules` in
`cli/release-automation/internal/automation/release_trigger_guard.go`) and fails when shared
release inputs changed without the matching triggers.

## Windows Compatibility Guardrails

Most day-to-day development happens on macOS, but this project must keep working on Windows.
Before changing scripts, skill files, generated-file synchronization, path handling, or text parsing, assume Windows will expose bugs that macOS hides.

- Treat encoding as explicit input. When PowerShell reads UTF-8 repository files, pass `-Encoding UTF8`; Windows PowerShell 5.1 otherwise uses a legacy default that can corrupt non-ASCII text and even report wrong line numbers.
- Repository text files should use LF by default. Only keep CRLF when a specific tool or file format requires it. Preserve expected line endings when writing generated files, and normalize line endings before comparison only when logical text equality is intended. If a script fails only under bash, WSL, or Git Bash, check CRLF before changing logic.
- Normalize relative paths at API boundaries. Do not compare raw path strings that may contain `/` on one side and `\` on another. Convert separators before storing, comparing, deleting, or syncing generated files.
- Prefer forward slashes in JSON `file:` paths and other cross-platform config values. Use escaped backslashes only when the target format explicitly requires them.
- Validate Windows-facing PowerShell with both `pwsh` and Windows PowerShell when practical, especially for multiline arguments, here-strings, UTF-8 files, and native executable calls.
- When validating this checkout on Windows, use the repo-local native binary (`dist/windows-amd64/uloop.exe`) instead of a `PATH`-resolved `uloop`. If a bash validation command cannot see the expected Go toolchain on Windows, retry through a login shell such as `bash -lc`.
- Add or update a regression test whenever a fix depends on encoding, line endings, or separator normalization. A passing macOS test alone is not enough for these cases.

## Dead Code Scanner

Use the C# dead-code scanner before deleting apparently unreferenced C# code or before adding comments to explain why an apparently unreferenced type must stay.

For type-level review, especially when checking classes that may be kept by Unity, serialization, reflection, release automation, or external package APIs, run:

```bash
dotnet run --project tools/UnityCliLoop.DeadCodeScanner -- --scope public --include-types true --include-members false --include-locals false --include-test-only true --include-kept true --format table
```

For a broader member/local-variable pass, run:

```bash
dotnet run --project tools/UnityCliLoop.DeadCodeScanner -- --scope public --include-types true --include-members true --include-locals true --include-test-only true --include-kept false --format table
```

Interpret scanner output conservatively:

- `KeptByUnityOrReflection` usually means the symbol is intentionally reachable through Unity callbacks, attributes, serialization, or reflection-style discovery. Do not add explanatory comments for every such symbol when the attribute/base type already makes the reason obvious.
- `PublicCandidate` means Roslyn found no direct references. Check non-C# references such as `release-please-config.json`, checked-in JSON contracts, Unity assets, generated files, and documented public APIs before removing or commenting the symbol.
- If a symbol is referenced only by non-C# tooling, verify that the tool reads it for runtime or release behavior. If the tool only rewrites the symbol and no code reads it, remove the marker instead of documenting it.

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

Do not add or keep Unity EditMode tests that can freeze the Editor.

- Never run multiple `uloop run-tests` commands in parallel. Treat Unity Test Runner as single-flight only.
- Do not add tests that rely on infinite waits, long-lived `TaskCompletionSource`, background fire-and-forget work, or cancellation handoff across domain reload boundaries.
- Avoid tests that intentionally cancel linked `CancellationTokenSource` instances while Unity may still dispose them during reload or teardown.
- Do not add Unity EditMode tests that use `Task.Run`, raw `Thread` work, or cross-thread coordination primitives such as `ManualResetEventSlim` unless the test is explicitly reviewed as unavoidable.
- Do not block the main thread inside Unity EditMode tests with `.Wait()`, `.Result`, `Task.WaitAll`, `Thread.Sleep`, or similar synchronous waiting APIs.
- Do not add Unity EditMode tests that execute real dynamic-code compile-and-run flows through `ExecuteDynamicCodeTool`, `DynamicCodeCompiler`, or similar end-to-end runtime paths when a pure unit test or compile-only test can cover the behavior.
- Do not add Unity EditMode tests that start nested test execution flows or any other long-running editor orchestration from inside a test body.
- Treat these patterns as high risk in Unity EditMode and avoid them by default:
  - Disposing runtime objects while an async execution is still in flight
  - Canceling an in-flight execution and then waiting for teardown on the same thread
  - Tests that require editor-thread continuations while the test body is synchronously waiting
  - Scheduling work onto background threads and then waiting for Unity main-thread continuations to complete
  - Cross-thread registration/cancellation tests that depend on exact frame timing or teardown order
  - Dynamic-code execution tests that compile code and then await timers, continuations, or runtime callbacks inside Unity EditMode
  - Using `TaskCompletionSource` as a gate for execution/dispose races unless every completion path is guaranteed without Unity callbacks
  - Assuming `[Timeout]` makes a test safe even when the runner itself can deadlock first
- Prefer pure unit tests for cancellation, dispose, and race-condition coverage. Only promote them to Unity EditMode after the logic is structured so the test completes without background leftovers or main-thread blocking.
- If a new test causes `uloop run-tests` to stall, immediately remove or disable that test instead of retrying the same suite repeatedly.
- If `Editor.log` shows messages such as `Attempted to call .Dispose on an already disposed CancellationTokenSource`, treat the latest cancellation-focused test changes as suspect first.
- If Unity freezes or stops responding to `uloop`, restart the Editor with `uloop launch -r` before attempting any further compile, test, or log commands.
