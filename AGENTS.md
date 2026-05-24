## Architecture Overview

This project provides a **CLI tool (`uloop`)** that communicates with Unity Editor via TCP.
AI agents interact with Unity through `uloop` CLI commands (e.g., `uloop get-logs`, `uloop compile`).
The Unity Editor side hosts a local project IPC server that accepts short-lived CLI command sessions.

Do not rename public package, assembly, or extension API identifiers as part of cleanup-only changes.

Comments in the code, commit messages, PR titles, and PR descriptions must all be written in English.

Every test method must have a short comment that states what behavior the test verifies.

## Commit-Time Version Checks

Before committing a C# package version bump, check whether `CliConstants.MINIMUM_REQUIRED_CLI_VERSION` must also change.
If the package release depends on behavior from a newer native CLI contract, update the minimum required CLI version and add or update focused tests.
If the minimum CLI version stays unchanged, make sure that decision is intentional before committing.

## Generated Skill Files

Do not directly edit skill files under the project-root `.agents/` or `.claude/` directories.
These files are generated copies. Update the source skill definitions instead, then regenerate the copies through the normal workflow.

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

When running `uloop` commands for this project during CLI development, do not use the `uloop` command resolved from `PATH`. Run this checkout's checked-in development binary directly so validation uses the code under review:

```bash
Packages/src/Cli~/dist/darwin-arm64/uloop compile --project-path "$(git rev-parse --show-toplevel)"
```

Before running a command with `--project-path`, confirm that the path is the intended Unity project for the current task. Do not copy a sibling checkout path from another repository or prior session. When intentionally validating a different Unity project, use an explicit placeholder in notes and replace it at execution time:

```bash
Packages/src/Cli~/dist/darwin-arm64/uloop compile --project-path <UNITY_PROJECT_ROOT>
```

If CLI source changes affect the command behavior you are validating, rebuild the development binary before running it.

When changing Go CLI source files under `Packages/src/Cli~`, run `scripts/check-go-cli.sh` before manually rebuilding checked-in binaries.
If the source checks pass and the script fails only because the checked-in native binaries are out of date, commit the regenerated binaries under `Packages/src/Cli~/dist`; use `scripts/build-go-cli.sh` only when you need to refresh those binaries explicitly.
When changing any checked-in native CLI binary under `Packages/src/Cli~/dist` directly, also run `scripts/check-go-cli.sh` before opening or updating a pull request.
This script is the local equivalent of the Go CLI CI validation: it runs formatting checks, vet, lint, tests, rebuilds the checked-in native binaries, and fails if the rebuilt binaries differ from the committed files.

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
