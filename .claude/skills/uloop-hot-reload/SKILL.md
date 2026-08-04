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
| `--files` | array | - | Project-relative `.cs` paths whose method bodies should be hot-reloaded. Required when neither `--revert-all` nor `--status` is set |
| `--revert-all` | flag | - | Remove every active hot-reload patch and clear the patch ledger. When set, `--files` is ignored |
| `--status` | flag | - | Lists the currently patched methods without applying or reverting anything. |

## Checking what is currently patched

`uloop hot-reload --status` lists the methods whose bodies are currently replaced,
without applying or reverting anything. It cannot be combined with `--files` or
`--revert-all`. Patches are static Editor state, so the answer is authoritative: after
a domain reload it reports zero patched methods, which is exactly when an
`ActivePatchTotal` remembered from an earlier response has gone stale.

## How it works

1. Resolves each file to its compiled assembly via `CompilationPipeline`.
2. Rewrites each editable method body into a static shim in an out-of-process Roslyn worker. When an async, iterator, lambda, local-function, or LINQ-query body touches private/internal members, those accesses are rewritten to accessor delegates so the body can compile and run from the shim assembly (the delegation shape below).
3. Compiles the shims against publicized reference copies, loads the result into the Editor domain, and binds every shim type's accessor delegates (`__BindAccessors`) before any patch is applied.
4. Patches each original method with a Harmony transpiler (ID `io.github.hatayama.uloop.hot-reload`) in one of two shapes: transplant copies the shim's IL into the original method, while delegation rewrites the original to forward its arguments to the shim, which runs as normally compiled code.

Re-running on the same method replaces its previous patch; `ActivePatchTotal` tracks the
ledger across runs.

## Scope and limits

Only ordinary method declarations are patched. Constructors, finalizers, operators, event
accessors, and `interface` members (including default interface implementations) are never
scanned: edits to them produce **no per-method entry at all** and are silently not applied —
use `uloop compile` for those.

Edits outside method bodies never take effect: changing a `const` value, a field initializer, or any declaration other than a method body leaves runtime behavior unchanged even though the response reports `Success` — shims resolve those symbols against the already-compiled assembly, and C# bakes `const` values into IL at compile time. Changed `const` values (including enum member values) are detected and reported as a `Warnings` entry naming the constant and both values; other outside-body edits stay silent. Use `uloop compile` for such edits.

Property and indexer accessors with explicit bodies are reported per-accessor as
`Skipped`, so an edited getter never disappears from the response silently.

### Skipped — reported per method, never flips `Success`

| Condition | Why |
|-----------|-----|
| Method on a `partial` type (including a type nested inside a partial outer type) | A single file cannot provide a complete semantic model |
| Method on a struct (value type) | Value-type patching is out of scope |
| Generic method, or method on a generic type | Harmony cannot safely patch open generics |
| Explicit interface implementation | Dotted metadata names cannot be expressed as shim identifiers |
| No body (`abstract` / `extern`) | Nothing to transplant |
| Body contains a `base.` call | `base` cannot be expressed from outside the type |
| Private/internal access inside an async/iterator/closure body has no accessor-delegate shape | Conditional access (`?.`), `??=`, indexers, static field writes, initializer member assignments, compound writes whose receiver could be evaluated twice, assignments whose value is consumed, and calls with `ref`/`out`/`in`, named, optional, or `params` arguments (or to extension/generic/by-ref-returning methods) cannot be rewritten to accessor delegates |
| An async/iterator/closure body references a private/internal type | Accessor delegates rescue member access, not type references; the body still cannot JIT-compile from the shim assembly |
| Property or indexer accessor with an explicit body | Accessor patching is out of scope for v1; `uloop compile` applies accessor edits |

### Failed — flips `Success` to `false`

| Condition | Notes |
|-----------|-------|
| File does not belong to any compiled assembly | Per-file entry with `Method` = `(file)`; only `Assets/` and `Packages/` sources resolve |
| Loaded assembly differs from the one on disk (pending compile) | Run `uloop compile` first, then retry |
| Source file fails to parse | Per-file entry carrying the parse errors |
| Method signature not found in the loaded assembly | New, renamed, or re-signatured members need `uloop compile` |
| Shim compile error (e.g. the body calls a member that does not exist yet) | Response carries the compiler error and a hint to run `uloop compile` |
| Patch rejected or crashed at apply time (e.g. `[BurstCompile]`, a patch-engine emit failure) | The entry carries the rejection reason or the underlying engine error; other methods in the run still apply |
| Accessor binding failed for a shim type | The source references a member the compiled assembly does not have yet; every delegation-patched method in that shim type reports the binder error — run `uloop compile` and retry |

## When a patch reports `Patched` but behavior does not change

`Patched` means the method body was replaced, not that the method ran. Before suspecting
the patch, confirm the method is actually reached. A currently patched method rejects new
pause points with `PAUSE_POINT_PATCHED_BY_HOT_RELOAD` (see the pause point interaction
below), so run `uloop compile` first — it makes the edits real and its domain reload
clears every patch — then arm `uloop enable-pause-point --mode trace` on the method's
first line, drive the game, and check the hit count: zero hits means the calling path
never reached the method, which no patch (or compile) can fix. To chase an early return
inside the method, arm a second marker on the suspected early-return line. The other
known cause is JIT inlining of tiny methods, which the response already flags with a
single aggregated warning listing the at-risk methods.

## Convergence and lifecycle

- The input is the real project source file, so a later `uloop compile` (real compile +
  domain reload) lands the exact same edit permanently. There is nothing to undo first;
  behavior converges by construction.
- Patches and loaded shim assemblies are static Editor state and disappear on the next
  domain reload. There is no persistence and no automatic re-apply.
- Never reflected by hot reload: new fields, field initializer changes, new types, and
  signature changes. Those always need `uloop compile`.

## Pause point interaction

Both patch shapes discard the original IL and any prior transpiler output on the patched
method — delegation replaces the body with a forward to the shim. Source pause points
instrument methods through the same transpiler chain, so the two tools enforce a strict
contract instead of composing silently:

- A pause point armed **before** a hot reload of the same method stops firing — its
  instrumentation was part of the discarded IL. The apply response lists the affected
  marker ids in `Warnings`, and `pause-point-status` reports `SuppressedByHotReload: true`
  for those markers until the patch is reverted.
- Enabling a pause point **on a currently patched method is rejected** with
  `PAUSE_POINT_PATCHED_BY_HOT_RELOAD`. Marker positions and local-variable capture
  resolve against the pre-patch compiled body, which no longer exists at runtime; there
  is no line on a patched method where a new marker would be trustworthy.
- `uloop hot-reload --revert-all` (or any domain reload) restores the original IL;
  previously armed pause points fire again and their `SuppressedByHotReload` flag clears.

So the workflow is: iterate on behavior with hot reload, and when you need pause-point
inspection of an edited method, run `uloop compile` to make the edits real (the compile's
domain reload clears every patch), then enable the marker. Use `--revert-all` instead
when you want the on-disk build back without recompiling.

## Output

Returns JSON with:

- `Success` (boolean): `false` on parameter validation failure or when any method outcome is `Failed`. `Skipped` outcomes alone never force `false`
- `Methods` (array): Per-method `{ Kind, Method, Reason, FilePath }` where `Kind` is `Patched`, `Skipped`, or `Failed` on apply runs, or `Active` on `--status` runs; empty on `--revert-all` runs
- `Warnings` (array): Non-fatal notes — a patched method that is small enough to have been JIT-inlined into existing callers (the change may not show at those call sites), the pause-point interaction above, and const drift entries described in "Scope and limits"
- `PatchedTotal` (number): Methods patched in this run
- `ActivePatchTotal` (number): Methods still patched after this run
- `ClearedCount` (number): Patches removed by `--revert-all` (0 on apply)
- `Message` (string): Short summary
