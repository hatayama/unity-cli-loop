---
name: uloop-execute-dynamic-code
toolName: execute-dynamic-code
description: "Execute C# with Unity APIs when existing uloop tools cannot inspect or edit enough. Use for reachable scene/component state, scene/prefab/menu automation, and PlayMode checks"
context: fork
---

# Task

Run focused C# snippets in the active Unity Editor with `uloop execute-dynamic-code`.

For basic selected GameObject discovery or property inspection, use `find-game-objects --search-mode Selected` before this tool. Use this tool after the built-in inspection tools are not enough or when you need to modify Unity state.

This tool can inspect reachable Unity state, such as GameObjects, components, public properties, static values, and method results. It cannot directly read local variables or intermediate calculations inside an already-running method. When those values matter, enable a source pause point on that line instead (`uloop enable-pause-point --file <file> --line <line>`, see the `uloop-pause-point` skill): the hit response's `CapturedVariables` already contains the locals, parameters, and instance fields at that line, with no code edit or recompile. `CapturedVariables` is a pre-line snapshot; this tool during the pause sees the interrupted method's post-interrupt state instead. While Unity stays paused on the hit, use `UloopPausePoint.TryGetCapturedValue(name)` (plus `GetCapturedNames()` / `GetCapturedPausePointId()`) for live captured references such as collections. Do not try to reconstruct pre-line locals with execute-dynamic-code alone.

The reverse combination also works: to freeze Unity on the first frame where a runtime condition holds (an animation peak, HP reaching zero, an enemy spawning), enable an id-only pause point, then use this tool to register an `EditorApplication.update` watcher that calls `UloopPausePoint.Pause(id)` when the condition is met, and return immediately. Wait with `uloop await-pause-point` on the CLI side — never poll or sleep inside the snippet itself, because the body runs synchronously on the main thread and frames stop advancing. See "Catching a Runtime Condition with a Dynamic-Code Trigger" in the `uloop-pause-point` skill.

Live state injection: when a running PlayMode session ends up in an unrecoverable state (for example, a stuck end-to-end test scenario), use this tool to write directly to private fields via reflection and recover or steer the running state, instead of stopping Play mode, editing code, and recompiling. This needs no code change, no recompile, and no re-entering Play mode — the session's in-memory state (component references, in-progress test fixtures, accumulated counters) survives. Because the snippet is a one-off diagnostic that never lands in the project's source files, using reflection here does not spread reflection through production code — a useful property even in projects whose coding rules restrict reflection.

## Parameters

- `--code '<code>'`: Inline C# statements to execute. Use direct statements only; `return` is optional, and `using` directives may appear at the top of the snippet.
- `--code-file <path>`: Read the C# statements from a file instead of `--code`. Use this when the active shell or launcher cannot preserve inline code exactly. Exactly one of `--code` or `--code-file` is required; combining them is an error.
- `--parameters {}` (advanced, optional): Pass a shell-quoted JSON object literal when reusing a snippet with varying data or when keeping values outside the code. Values are exposed as `parameters["param0"]`, `parameters["param1"]`, and so on. Omit this flag for most snippets. Do not pass a JSON string value such as `"{\"param0\":\"value\"}"`.
- `--wait-for-domain-reload` (optional): Wait for Domain Reload recovery after snippets that intentionally trigger Unity script reload or import work. Omit this for normal inspection and editor-state workflows.

## Code Rules

Write direct statements from your own Unity API knowledge — no class/namespace/method wrappers. Return is optional.

```csharp
using UnityEngine;
float x = Mathf.PI;
return x;
```

Prefer terminal commands for file operations and keep snippets focused on Unity Editor state that existing uloop tools cannot inspect or change.

## Known transpiler constraints

- Literals inside recognized static local function bodies are kept inline automatically. Unsupported header shapes (generic `where` clauses, tuple return types, statement lambdas inside expression bodies) may still hoist literals and surface CS8421; remove `static` or rewrite the helper.
- Static lambdas (`static x => ...`) cannot reference hoisted literals and surface CS8820; remove `static` from the lambda or use a non-static local function.
- Integer literals are hoisted as `int` values. APIs that require `byte` components (for example `new Color32(255, 0, 0, 255)`) need explicit casts such as `(byte)255` even when plain Unity scripts accept uncast numeric literals.

## Shell Quoting

- zsh/bash: single-quote the whole snippet so C# double quotes pass through unchanged: `--code 'return "hi";'`. For a single quote inside the snippet, close and reopen the shell string with `'\''`.
- PowerShell 7 (`pwsh`): for multiline snippets, assign a single-quoted here-string (`$code = @'` ... `'@`) and pass `--code $code`. Inline, single-quoted arguments preserve C# double quotes; double an inner single quote (`''`).
- Windows PowerShell 5.1 removes unescaped double quotes from native command arguments: escape them as `\"`, or prefer `--code-file`.
- Pass `--parameters` as a single-quoted JSON object literal in both shells, for example `--parameters '{"param0":"value"}'`.
- On Windows, multiline `--code` requires the native `uloop.exe`. If `(Get-Command uloop).Source` resolves to a legacy `.cmd` shim, run `uloop install` and open a new terminal.
- If quoting still mangles the snippet, switch to `--code-file`.

## When To Use Input Simulation Tools Instead

Calling UI handlers or runtime methods directly from a snippet is the better choice for targeted automation, direct state control, or quick diagnostics. Switch to the dedicated input tools only when the input route itself is part of what you need to verify:

| Scenario | Recommended tool | Why |
|----------|------------------|-----|
| Verify that a uGUI element responds through the real EventSystem pointer path | `simulate-mouse-ui` | Fires `PointerDown` / `PointerUp` / `PointerClick` / drag events through EventSystem raycasts instead of bypassing the UI input route. |
| Test gameplay that reads `Mouse.current`, button state, delta, or scroll | `simulate-mouse-input` | Injects Input System mouse state into `Mouse.current` so game code observes it like player input. Requires the New Input System (`Input System Package (New)` or `Both`); when that is unavailable, prefer an execute-dynamic-code workaround instead of changing project settings just to use the tool. |
| Jump straight to a known callback, invoke a method, inspect state, or set up a test precondition | `execute-dynamic-code` | Direct automation without reproducing the full input pipeline. |
| Drive custom runtime behavior that does not map cleanly to the built-in input tools | `execute-dynamic-code` | Calls project-specific methods and prototypes one-off flows immediately. |

## Output

Returns JSON:

- `Success`: boolean — overall execution success
- `Result`: string — value of the snippet's `return` statement (empty when omitted)
- `Logs`: string[] — execution messages from the dynamic-code tool; read Unity Console `Debug.Log` output with `get-logs`
- `CompilationErrors`: object[] — Roslyn diagnostics with `Message`, `Line`, `Column`, `ErrorCode`, optional `Hint` and `Suggestions`
- `Error` / `ErrorMessage`: string — top-level failure summary (empty on success)
- `UpdatedCode`: string|null — the wrapped form actually compiled (handy when debugging using-statement reordering)
- `DiagnosticsSummary`: string|null — compact summary when diagnostics are available
- `Diagnostics`: object[] — structured diagnostics; same shape as `CompilationErrors`, usually populated together with it

On `Success: false`, inspect `CompilationErrors` first. If empty, read `ErrorMessage` (and `Logs` for extra context) — the failure may be a runtime exception, cancellation, or an "execution in progress" rejection, all of which return empty `CompilationErrors`. Both EditMode and PlayMode are supported targets — the snippet runs in whichever mode the Editor is currently in.
