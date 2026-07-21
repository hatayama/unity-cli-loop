---
name: uloop-execute-dynamic-code
toolName: execute-dynamic-code
description: "Execute C# with Unity APIs when existing uloop tools cannot inspect or edit enough. Use for reachable scene/component state, scene/prefab/menu automation, and PlayMode checks"
---

# Task

Run focused C# snippets in the active Unity Editor with `uloop execute-dynamic-code`.

For basic selected GameObject discovery or property inspection, use `find-game-objects --search-mode Selected` before this tool. Use this tool after the built-in inspection tools are not enough or when you need to modify Unity state.

This tool can inspect reachable Unity state, such as GameObjects, components, public properties, static values, and method results. It cannot directly read local variables or intermediate calculations inside an already-running method — do not try to reconstruct them with this tool alone. When those values matter, follow the `uloop-pause-point` skill instead: a pause point's `CapturedVariables` already contains the locals, parameters, and instance fields at that line with no code edit or recompile, and while Unity stays paused `UloopPausePoint.TryGetCapturedValue(name)` gives this tool live captured references. That skill also covers the reverse combination — using this tool to register an `EditorApplication.update` watcher that freezes Unity on the first frame a runtime condition holds (never poll or sleep inside the snippet itself; the body runs synchronously on the main thread).

Live state injection: when a running PlayMode session is merely in the wrong state — a stuck end-to-end scenario, a camera angle from which a raycast can never hit, a private flag blocking the path you want to test — fix the state instead of the code: write the field directly from this tool (reflection reaches private fields) and steer the session onward. Nothing recompiles and no domain reload happens, so the session's in-memory state (component references, in-progress fixtures, accumulated counters) survives intact, where stopping Play mode to edit code would throw it all away. Because the snippet is a one-off diagnostic that never lands in the project's source files, using reflection here does not spread reflection through production code — a useful property even in projects whose coding rules restrict reflection.

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

In zsh/bash, single-quote the whole snippet so C# double quotes pass through unchanged: `--code 'return "hi";'`. If a snippet fails to parse, gets mangled by the shell, or you are on Windows/PowerShell, read [references/shell-quoting.md](references/shell-quoting.md) — or switch to `--code-file`.

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
