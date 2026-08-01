---
name: uloop-hot-reload
toolName: hot-reload
description: "Apply method-body hot reload to edited C# sources in a running Unity Editor without domain reload. Use after small method edits when you need PlayMode/EditMode feedback without uloop compile."
---

# uloop hot-reload

Replaces method bodies in the running Editor (EditMode or PlayMode) directly from edited
project source files — no domain reload, no attributes, no source markers. Private/internal
member access, static methods, return values, async methods, and iterators all work within
the limits below — including private access inside async, iterator, lambda, local-function,
and LINQ-query bodies. Methods that cannot be patched are reported per method as `Skipped`
or `Failed`; one unpatchable method never aborts the rest of the run.

## Usage

```bash
uloop hot-reload --files Assets/Scripts/Enemy.cs
uloop hot-reload --files Assets/Scripts/Enemy.cs,Assets/Scripts/Boss.cs
uloop hot-reload --revert-all
```

Multiple files are passed as one comma-separated value (or a JSON array); array options
consume exactly one value token.

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--files` | array | - | Project-relative `.cs` paths whose method bodies should be hot-reloaded. Required when `--revert-all` is not set |
| `--revert-all` | flag | - | Remove every active hot-reload patch and clear the patch ledger. When set, `--files` is ignored |

## How it works

1. Resolves each file to its compiled assembly via `CompilationPipeline`.
2. Rewrites each editable method body into a static shim in an out-of-process Roslyn worker.
3. Compiles the shims against publicized reference copies and loads the result into the Editor domain.
4. Transplants each shim's IL into the original method with a Harmony transpiler (ID `io.github.hatayama.uloop.hot-reload`).

Re-running on the same method replaces its previous patch; `ActivePatchTotal` tracks the
ledger across runs.

## Scope and limits (v1)

Only ordinary method declarations are scanned. Property and indexer accessors, constructors,
finalizers, operators, and event accessors are never scanned: edits to them produce **no
per-method entry at all** and are silently not applied — use `uloop compile` for those.

Edits outside method bodies never take effect: changing a `const` value, a field initializer, or any declaration other than a method body leaves runtime behavior unchanged even though the response reports `Success` — shims resolve those symbols against the already-compiled assembly, and C# bakes `const` values into IL at compile time. Use `uloop compile` for such edits.

### Skipped — reported per method, never flips `Success`

| Condition | Why |
|-----------|-----|
| Method on a `partial` type (including a type nested inside a partial outer type) | A single file cannot provide a complete semantic model |
| Method on a struct (value type) | Value-type transplant is out of v1 scope |
| Generic method, or method on a generic type | Harmony cannot safely patch open generics |
| Explicit interface implementation | Dotted metadata names cannot be expressed as shim identifiers |
| No body (`abstract` / `extern`) | Nothing to transplant |
| Body contains a `base.` call | `base` cannot be expressed from outside the type |
| Private/internal access inside an async/iterator/closure body has no accessor-delegate shape | Conditional access (`?.`), `??=`, indexers, static field writes, initializer member assignments, compound writes whose receiver could be evaluated twice, assignments whose value is consumed, and calls with `ref`/`out`/`in`, named, optional, or `params` arguments (or to extension/generic/by-ref-returning methods) cannot be rewritten to accessor delegates |
| An async/iterator/closure body references a private/internal type | Accessor delegates rescue member access, not type references; the body still cannot JIT-compile from the shim assembly |

### Failed — flips `Success` to `false`

| Condition | Notes |
|-----------|-------|
| File does not belong to any compiled assembly | Per-file entry with `Method` = `(file)`; only `Assets/` and `Packages/` sources resolve |
| Loaded assembly differs from the one on disk (pending compile) | Run `uloop compile` first, then retry |
| Source file fails to parse | Per-file entry carrying the parse errors |
| Method signature not found in the loaded assembly | New, renamed, or re-signatured members need `uloop compile` |
| Shim compile error (e.g. the body calls a member that does not exist yet) | Response carries the compiler error and a hint to run `uloop compile` |
| Patch rejected at apply time (e.g. `[BurstCompile]`) | Not patchable by Harmony transplant |
| Accessor binding failed for a shim type | The source references a member the compiled assembly does not have yet; every delegation-patched method in that shim type reports the binder error — run `uloop compile` and retry |

## Convergence and lifecycle

- The input is the real project source file, so a later `uloop compile` (real compile +
  domain reload) lands the exact same edit permanently. There is nothing to undo first;
  behavior converges by construction.
- Patches and loaded shim assemblies are static Editor state and disappear on the next
  domain reload. There is no persistence and no automatic re-apply.
- Never reflected by hot reload: new fields, field initializer changes, new types, and
  signature changes. Those always need `uloop compile`.

## Pause point interaction

A hot-reload transplant discards the original IL and any prior transpiler output on that
method, so a source pause point on a hot-reloaded method stops firing until the patch is
reverted (`--revert-all`) or a domain reload restores the original IL. Apply responses
include this as a `Warnings` entry whenever any method was patched.

## Output

Returns JSON with:

- `Success` (boolean): `false` on parameter validation failure or when any method outcome is `Failed`. `Skipped` outcomes alone never force `false`
- `Methods` (array): Per-method `{ Kind, Method, Reason, FilePath }` where `Kind` is `Patched`, `Skipped`, or `Failed`
- `Warnings` (array): Non-fatal notes — a patched method that is small enough to have been JIT-inlined into existing callers (the change may not show at those call sites), and the pause-point interaction above
- `PatchedTotal` (number): Methods patched in this run
- `ActivePatchTotal` (number): Methods still patched after this run
- `ClearedCount` (number): Patches removed by `--revert-all` (0 on apply)
- `Message` (string): Short summary
