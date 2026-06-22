---
name: uloop-execute-dynamic-code
toolName: execute-dynamic-code
description: "Execute C# with Unity APIs when existing uloop tools cannot inspect or edit enough. Use for reachable scene/component state, scene/prefab/menu automation, and PlayMode checks"
context: fork
---

# Task

Run focused C# snippets in the active Unity Editor with `uloop execute-dynamic-code`.

For basic selected GameObject discovery or property inspection, use `find-game-objects --search-mode Selected` before this tool. Use this tool after the built-in inspection tools are not enough or when you need to modify Unity state.

This tool can inspect reachable Unity state, such as GameObjects, components, public properties, static values, and method results. It cannot directly read local variables or intermediate calculations inside an already-running method. When those values matter, add a focused `Debug.Log` immediately before `UloopPausePoint.Pause("<id>")`, then run `get-logs --search-text <id>` while Unity is paused. Do not replace that log read with execute-dynamic-code.

## Workflow

1. Read the relevant reference file(s) from the Code Examples section below
2. Construct C# code based on the reference examples
3. Execute with `--code` or `--code-file` using the active shell's quoting guidance
4. Report the execution result

## Parameters

- `--code '<code>'`: Inline C# statements to execute. Use direct statements only; `return` is optional, and `using` directives may appear at the top of the snippet.
- `--code-file <path>`: Read the C# statements from a file instead of `--code`. Use this when the active shell or launcher cannot preserve inline code exactly. Exactly one of `--code` or `--code-file` is required; combining them is an error.
- **Shell-specific quoting**: Read [references/playmode-automation-powershell.md](references/playmode-automation-powershell.md) for Windows/PowerShell multiline commands and [references/playmode-automation-zsh.md](references/playmode-automation-zsh.md) for zsh/macOS examples.
- `--parameters {}` (advanced, optional): Pass a shell-quoted JSON object literal when reusing a snippet with varying data or when keeping values outside the code. Values are exposed as `parameters["param0"]`, `parameters["param1"]`, and so on. Omit this flag for most snippets. Do not pass a JSON string value such as `"{\"param0\":\"value\"}"`.
- `--wait-for-domain-reload` (optional): Wait for Domain Reload recovery after snippets that intentionally trigger Unity script reload or import work. Omit this for normal inspection and editor-state workflows.

## Code Rules

Write direct statements only — no class/namespace/method wrappers. Return is optional.

```csharp
using UnityEngine;
float x = Mathf.PI;
return x;
```

Prefer terminal commands for file operations and keep snippets focused on Unity Editor state that existing uloop tools cannot inspect or change.

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

## Code Examples by Category

For detailed code examples, refer to these files:

- **Prefab operations**: See [references/prefab-operations.md](references/prefab-operations.md)
  - Create prefabs, instantiate, add components, modify properties
- **Material operations**: See [references/material-operations.md](references/material-operations.md)
  - Create materials, set shaders/textures, modify properties
- **Asset operations**: See [references/asset-operations.md](references/asset-operations.md)
  - Find/search assets, duplicate, move, rename, load
- **ScriptableObject**: See [references/scriptableobject.md](references/scriptableobject.md)
  - Create ScriptableObjects, modify with SerializedObject
- **Scene operations**: See [references/scene-operations.md](references/scene-operations.md)
  - Create/modify GameObjects, set parents, wire references, load scenes
- **Batch operations**: See [references/batch-operations.md](references/batch-operations.md)
  - Bulk modify objects, batch add/remove components, rename, layer/tag/material replacement
- **Cleanup operations**: See [references/cleanup-operations.md](references/cleanup-operations.md)
  - Detect broken scripts, missing references, unused materials, empty GameObjects
- **Undo operations**: See [references/undo-operations.md](references/undo-operations.md)
  - Undo-aware operations: RecordObject, AddComponent, SetParent, grouping
- **Selection operations**: See [references/selection-operations.md](references/selection-operations.md)
  - Get/set selection, multi-select, filter by type/editability
- **PlayMode automation (PowerShell/Windows)**: See [references/playmode-automation-powershell.md](references/playmode-automation-powershell.md)
  - Click UI buttons, invoke methods, set fields, tool combination workflows for PowerShell users
- **PlayMode automation (zsh/macOS)**: See [references/playmode-automation-zsh.md](references/playmode-automation-zsh.md)
  - Click UI buttons, invoke methods, set fields, tool combination workflows for zsh users
- **PlayMode UI controls**: See [references/playmode-ui-controls.md](references/playmode-ui-controls.md)
  - InputField, Slider, Toggle, Dropdown, drag & drop simulation, list all UI controls
- **PlayMode inspection**: See [references/playmode-inspection.md](references/playmode-inspection.md)
  - Scene info, game state via reflection, physics state, raycast checks, GameObject search, position/rotation
