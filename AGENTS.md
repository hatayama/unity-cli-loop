## Architecture Overview

This project provides a **CLI tool (`uloop`)** that communicates with Unity Editor via TCP.
AI agents interact with Unity through `uloop` CLI commands (e.g., `uloop get-logs`, `uloop compile`), NOT through MCP protocol directly.
The MCP server (TypeScriptServer~) exists as a separate component but is not the primary interface for AI agents.

The C# namespace is `io.github.hatayama.uLoopMCP` for historical reasons, but this is a CLI-based tool, not an MCP tool.

Comments in the code, commit messages, PR titles, and PR descriptions must all be written in English.

Every test method must have a short comment that states what behavior the test verifies.

## Generated Skill Files

Do not directly edit skill files under the project-root `.agents/` or `.claude/` directories.
These files are generated copies. Update the source skill definitions instead, then regenerate the copies through the normal workflow.

## Native Go CLI Validation

When changing files under `Packages/src/GoCli~` or any checked-in native CLI binary under `Packages/src/GoCli~/dist`, run `scripts/check-go-cli.sh` before opening or updating a pull request.
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
