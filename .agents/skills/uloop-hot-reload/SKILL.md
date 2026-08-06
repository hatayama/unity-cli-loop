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

Hot reload never adds members. A refactor that extracts a new helper method cannot be
applied piecewise — keep iterating inside existing method bodies, then run
`uloop compile` once to introduce the new member for real.

Edits outside method bodies never take effect: changing a `const` value, a field
initializer, or any declaration other than a method body leaves runtime behavior
unchanged even though the response reports `Success` — shims resolve those symbols
against the already-compiled assembly, and C# bakes `const` values into IL at compile
time. Changed `const` values (including enum member values) are detected and reported
as a `Warnings` entry naming the constant and both values. When a verified source
baseline is available (next paragraph), other outside-body drift — fields,
initializers, attributes, added or removed members — is reported as a `Warnings`
entry as well; without a baseline it stays silent. Either way, use `uloop compile`
for such edits.

Each `uloop compile` also establishes a per-assembly source baseline: a snapshot of
the sources exactly as they were compiled, captured after the compile's domain reload
and adopted only once it verifies against the compiled assembly's PDB checksums. With
a baseline, hot reload patches only the methods whose bodies actually changed;
unchanged methods are left untouched and counted in `UnchangedTotal` (formatting,
comments, and line-ending differences count as unchanged). A run where every method
is unchanged succeeds with nothing patched.
Convergence works in both directions: a currently patched method whose body matches
the baseline again is unpatched on that run — the compiled IL comes back,
`ActivePatchTotal` drops, and its pause-point block lifts.
Without a baseline — for example before
the first compile after installing or updating the package — every editable method in
the file is patched and a `Warnings` line reports the fallback; run `uloop compile`
to establish the baseline.

Property and indexer accessors with explicit bodies are reported per-accessor as
`Skipped`, so an edited getter never disappears from the response silently; with a
verified baseline, accessors unchanged from it produce no row.

Subscribing to or unsubscribing from a field-like event (`+=`/`-=`) inside an edited
body works. Methods that raise the event are reported as `Skipped` (see the table
below) — raising is only expressible inside the declaring type, which a shim is not.

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
| Method raises, invokes, or reads a field-like event (anything beyond `+=`/`-=`) | C# only allows `+=`/`-=` on an event outside its declaring type, so the raising body cannot compile from the shim assembly |

### Failed — flips `Success` to `false`

| Condition | Notes |
|-----------|-------|
| File does not belong to any compiled assembly | Per-file entry with `Method` = `(file)`; only `Assets/` and `Packages/` sources resolve |
| Loaded assembly differs from the one on disk (pending compile) | Run `uloop compile` first, then retry |
| Source file fails to parse | Per-file entry carrying the parse errors |
| Method signature not found in the loaded assembly | New, renamed, or re-signatured members need `uloop compile` |
| Shim compile error (e.g. the body calls a member that does not exist yet) | Failing methods are isolated: each reports `Failed` with its own compiler errors (plus the `uloop compile` hint when they indicate a missing member) while the remaining methods still patch. When errors cannot be attributed per method, the whole file reports one `(shim-compile)` entry |
| Patch rejected or crashed at apply time (e.g. `[BurstCompile]`, a patch-engine emit failure) | The entry carries the rejection reason or the underlying engine error; other methods in the run still apply |
| Accessor binding failed for a shim type | The source references a member the compiled assembly does not have yet; every delegation-patched method in that shim type reports the binder error — run `uloop compile` and retry |

## When a patch reports `Patched` but behavior does not change

`Patched` means the method body was replaced, not that the method ran. Before suspecting
the patch, confirm the method is actually reached: arm `uloop enable-pause-point --mode
trace` on a line inside the edited method body — it resolves against the patched body
directly (see the pause point interaction below) — drive the game, and check the hit
count: zero hits means the calling path never reached the method, which no patch (or
compile) can fix. To chase an early return inside the method, arm a second marker on the
suspected early-return line. The other known cause is JIT inlining of tiny methods, which
the response already flags with a single aggregated warning listing the at-risk methods.

## Convergence and lifecycle

- The input is the real project source file, so a later `uloop compile` (real compile +
  domain reload) lands the exact same edit permanently. There is nothing to undo first;
  behavior converges by construction.
- Patches and loaded shim assemblies are static Editor state and disappear on the next
  domain reload. There is no persistence and no automatic re-apply.
- Never reflected by hot reload: new fields, field initializer changes, new types, and
  signature changes. Those always need `uloop compile`.
- A run with `Failed` outcomes still applies every other patch — outcomes are
  per-method and there is no run-level rollback. `Methods` is the authoritative
  record of which bodies changed.

## Editor-code iteration without PlayMode

Hot reload also patches static methods in Editor assemblies. Combined with
`uloop execute-dynamic-code` invoking the patched method, this gives a
compile-free loop for editor tooling: edit the method body, run
`uloop hot-reload --files <file>`, then re-invoke it via `execute-dynamic-code`
and read the returned value.

## Pause point interaction

Both patch shapes discard the original IL and any prior transpiler output on the patched
method, so armed source pause points cannot survive a patch unchanged. Instead of
enforcing exclusivity, every patch transition re-targets them:

- Applying a patch re-resolves each armed marker on the edited method against the
  patched body. A marker whose line still resolves keeps firing at the edited line —
  the apply response reports those ids in a `Warnings` entry and `pause-point-status`
  shows `RetargetedToHotReloadPatch: true`. A marker whose line no longer resolves is
  suppressed instead: the apply response lists it, and status shows
  `SuppressedByHotReload: true` with the reason in `SuppressedByHotReloadReason`.
- Enabling a new pause point on a currently patched method resolves against the patched
  body directly. `PAUSE_POINT_PATCHED_BY_HOT_RELOAD` is returned only when the line
  cannot be mapped onto it (a stale line map or a superseded generation).
- `uloop hot-reload --revert-all` (or reverting a method's patch) re-targets armed
  markers back onto the compiled body; a marker whose line no longer resolves there
  stays suppressed with a reason until `uloop compile` and a re-enable.

Suppressed markers are never cleared automatically — they keep their identity and fire
again as soon as a transition restores their line. The practical workflow: iterate with
hot reload and place pause points on edited lines in either order — enable then patch,
or patch then enable. `uloop compile` is needed only when a marker stays suppressed
because its line no longer resolves in any live body.

## Output

Returns JSON with:

- `Success` (boolean): `false` on parameter validation failure or when any method outcome is `Failed`. `Skipped` outcomes alone never force `false`
- `Methods` (array): Per-method `{ Kind, Method, Reason, FilePath }` where `Kind` is `Patched`, `Skipped`, or `Failed` on apply runs, or `Active` on `--status` runs; empty on `--revert-all` runs
- `Warnings` (array): Non-fatal notes — one aggregated line listing the patched methods whose pre-patch bodies were small enough (or marked `[AggressiveInlining]`) to have been JIT-inlined into existing callers (the change may not show at those call sites), the pause-point interaction above, and the const drift, outside-body drift, and missing-baseline entries described in "Scope and limits"
- `PatchedTotal` (number): Methods patched in this run
- `UnchangedTotal` (number): Methods left untouched because their bodies match the source baseline from the last compile; `0` when no baseline was available
- `ActivePatchTotal` (number): Methods still patched after this run
- `ClearedCount` (number): Patches removed by `--revert-all` (0 on apply)
- `Message` (string): Short summary
