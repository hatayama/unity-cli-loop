---
name: uloop-execute-dynamic-code
description: "Execute C# code dynamically in Unity Editor. Use find-game-objects first for basic selected GameObject discovery or property inspection. Use when you need to: (1) Wire prefab/material references and AddComponent operations, (2) Edit SerializedObject properties and reference wiring, (3) Perform scene/hierarchy edits and batch operations, (4) Execute Unity Editor menu commands through EditorApplication.ExecuteMenuItem, (5) PlayMode automation (click buttons, invoke methods, tweak runtime state), (6) PlayMode UI controls (InputField, Slider, Toggle, Dropdown), (7) PlayMode inspection that requires custom Unity API code beyond existing tools. NOT for file I/O or script authoring."
context: fork
---

# Task

Execute the following request using `uloop execute-dynamic-code`: $ARGUMENTS

For basic selected GameObject discovery or property inspection, use `find-game-objects --search-mode Selected` before this tool. Use this tool after the built-in inspection tools are not enough or when you need to modify Unity state.

## Workflow

1. Read the relevant reference file(s) from the Code Examples section below
2. Construct C# code based on the reference examples
3. Execute via Bash: `uloop execute-dynamic-code --code '<code>'`
4. If execution fails, adjust code and retry
5. Report the execution result

## Parameters

- `--code '<code>'` (required): Inline C# statements to execute. Use direct statements only; `return` is optional, and `using` directives may appear at the top of the snippet.
- **Shell quoting**: bash/zsh uses single quotes, for example `uloop execute-dynamic-code --code 'using UnityEngine; return Mathf.PI;'`. PowerShell doubles inner quotes (`'Debug.Log(""Hello!"");'`).
- `--parameters {}` (advanced, optional): Pass an object when reusing a snippet with varying data or when keeping values outside the code. Values are exposed as `parameters["param0"]`, `parameters["param1"]`, and so on. Omit this flag for most snippets, and pass an object instead of a JSON string.
- `--compile-only true` (optional): Compile the snippet without executing it. Use this when you want Roslyn diagnostics before running new code.

## Code Rules

Write direct statements only — no class/namespace/method wrappers. Return is optional.

```csharp
using UnityEngine;
float x = Mathf.PI;
return x;
```

**Forbidden** — these will be rejected at compile time: `System.IO.*`, `AssetDatabase.CreateFolder`, creating/editing `.cs`/`.asmdef` files. Use terminal commands for file operations instead.

## Output

Returns JSON:
- `Success`: boolean — overall execution success
- `Result`: string — value of the snippet's `return` statement (empty when omitted)
- `Logs`: string[] — `Debug.Log` / `Debug.LogWarning` / `Debug.LogError` messages emitted during the run
- `CompilationErrors`: object[] — Roslyn diagnostics with `Message`, `Line`, `Column`, `ErrorCode`, optional `Hint` and `Suggestions`
- `ErrorMessage`: string — top-level failure summary (empty on success)
- `Error`: string — alias of `ErrorMessage`
- `SecurityLevel`: string — dynamic-code security level active for the request
- `UpdatedCode`: string|null — the wrapped form actually compiled (handy when debugging using-statement reordering)
- `DiagnosticsSummary`: string|null — compact summary when diagnostics are available
- `Diagnostics`: object[] — structured diagnostics; same shape as `CompilationErrors`, usually populated together with it

On `Success: false`, inspect `CompilationErrors` first. If empty, read `ErrorMessage` (and `Logs` for extra context) — the failure may be a runtime exception, security violation, cancellation, or an "execution in progress" rejection, all of which return empty `CompilationErrors`. Both EditMode and PlayMode are supported targets — the snippet runs in whichever mode the Editor is currently in.

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
- **PlayMode automation (zsh)**: See [references/playmode-automation-zsh.md](references/playmode-automation-zsh.md)
  - Click UI buttons, invoke methods, set fields, tool combination workflows for zsh users
- **PlayMode automation (PowerShell)**: See [references/playmode-automation-powershell.md](references/playmode-automation-powershell.md)
  - Click UI buttons, invoke methods, set fields, tool combination workflows for PowerShell users
- **PlayMode UI controls**: See [references/playmode-ui-controls.md](references/playmode-ui-controls.md)
  - InputField, Slider, Toggle, Dropdown, drag & drop simulation, list all UI controls
- **PlayMode inspection**: See [references/playmode-inspection.md](references/playmode-inspection.md)
  - Scene info, game state via reflection, physics state, raycast checks, GameObject search, position/rotation
