## Architecture Overview

This project provides a **CLI tool (`uloop`)** that communicates with Unity Editor via TCP.
AI agents interact with Unity through `uloop` CLI commands (e.g., `uloop get-logs`, `uloop compile`), NOT through MCP protocol directly.
The MCP server (TypeScriptServer~) exists as a separate component but is not the primary interface for AI agents.

The C# namespace is `io.github.hatayama.uLoopMCP` for historical reasons, but this is a CLI-based tool, not an MCP tool.

Comments in the code, commit messages, PR titles, and PR descriptions must all be written in English.

## Unity Freeze Prevention

Do not add or keep Unity EditMode tests that can freeze the Editor.

- Never run multiple `uloop run-tests` commands in parallel. Treat Unity Test Runner as single-flight only.
- Do not add tests that rely on infinite waits, long-lived `TaskCompletionSource`, background fire-and-forget work, or cancellation handoff across domain reload boundaries.
- Avoid tests that intentionally cancel linked `CancellationTokenSource` instances while Unity may still dispose them during reload or teardown.
- Treat these patterns as high risk in Unity EditMode and avoid them by default:
  - Disposing runtime objects while an async execution is still in flight
  - Canceling an in-flight execution and then waiting for teardown on the same thread
  - Tests that require editor-thread continuations while the test body is synchronously waiting
  - Using `TaskCompletionSource` as a gate for execution/dispose races unless every completion path is guaranteed without Unity callbacks
  - Assuming `[Timeout]` makes a test safe even when the runner itself can deadlock first
- Prefer pure unit tests for cancellation, dispose, and race-condition coverage. Only promote them to Unity EditMode after the logic is structured so the test completes without background leftovers or main-thread blocking.
- If a new test causes `uloop run-tests` to stall, immediately remove or disable that test instead of retrying the same suite repeatedly.
- If `Editor.log` shows messages such as `Attempted to call .Dispose on an already disposed CancellationTokenSource`, treat the latest cancellation-focused test changes as suspect first.
- If Unity freezes or stops responding to `uloop`, restart the Editor with `uloop launch -r` before attempting any further compile, test, or log commands.

## Skill Description Guidelines

When writing or updating skill descriptions in `.claude/skills/*/SKILL.md`, follow the **"What → When → How"** structure:

1. **What** (first): What capability does the skill provide? (e.g., "Automate Unity Editor operations")
2. **When** (middle): When should the AI use it? Use the pattern "Use when you need to: (1) ..., (2) ..., (3) ..."
3. **How** (last): Technical implementation details (e.g., "Executes C# code dynamically via uloop CLI")

This structure follows the "inverted pyramid" principle - the most important information comes first, enabling both AI and users to quickly assess skill relevance for a given task.
