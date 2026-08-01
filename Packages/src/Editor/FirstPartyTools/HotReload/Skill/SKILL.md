---
name: uloop-hot-reload
toolName: hot-reload
description: "Apply method-body hot reload to edited C# sources in a running Unity Editor without domain reload. Use after small method edits when you need PlayMode/EditMode feedback without uloop compile."
---

# uloop hot-reload

DRAFT — prose wording will be finalized by the Fable session. Parameter table below must stay aligned with the implemented schema.

Reload method bodies from edited project source files into the running Editor (EditMode or PlayMode) without a domain reload. No `[HotReload]` attribute is required. Private/internal access, static methods, return values, async, and iterators are supported within v1 skip rules. Methods that cannot be patched are reported as Skipped with a reason (not as a hard tool failure for that method alone).

## Usage

```bash
uloop hot-reload --files Assets/Scripts/Enemy.cs
uloop hot-reload --files Assets/Scripts/Enemy.cs Assets/Scripts/Boss.cs
uloop hot-reload --revert-all
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--files` | array | - | Project-relative `.cs` paths whose method bodies should be hot-reloaded. Required when `--revert-all` is not set |
| `--revert-all` | flag | - | Remove every active hot-reload transplant and clear the patch ledger. When set, `--files` is ignored |

## What it does

1. Resolves each file to its compiled assembly via `CompilationPipeline`.
2. Transforms edited method bodies into static shim methods in an out-of-process worker.
3. Compiles shims against publicized reference assemblies and loads them into the Editor domain.
4. Transplants each shim's IL into the original method with Harmony (ID `io.github.hatayama.uloop.hot-reload`).

## v1 skip conditions

| Condition | Reason (summary) |
|-----------|------------------|
| Body contains `base.` call | Cannot express `base` from outside the type |
| Method on a `partial` type | Single-file semantic model is incomplete |
| Generic method or method on a generic type | Harmony cannot safely patch these |
| Method on a struct (value type) | Value-type transplant semantics are out of v1 scope |
| Property/indexer accessor, constructor, finalizer, operator | v1 covers ordinary methods only |
| Async/iterator body with private/internal access | State machine `MoveNext` is normally JIT-checked |
| Lambda/local-function body with private/internal access | Closure methods are normally JIT-checked |
| Signature missing from the loaded assembly | New/renamed/changed members need `uloop compile` |
| abstract / extern / `[BurstCompile]` | Not patchable |
| Shim compile error (e.g. calling a newly added helper) | Needs `uloop compile`; response includes the csc error and hint |

## Convergence and lifecycle

- Input is the real project source file. A later `uloop compile` (real compile + domain reload) converges to the same behavior as the patch.
- Active Harmony patches and loaded shim assemblies are static state and disappear on domain reload by design. There is no persistence or automatic re-apply.
- Field additions, field initializers, and new types are never reflected by hot reload.

## Pause point interaction

Hot-reload transplant discards the original IL and any prior transpiler output on that method. A source pause point on a hot-reloaded method will not fire until the patch is reverted (`--revert-all` or a later revert of that method) or a domain reload restores the original IL. Apply responses include this as a `Warnings` entry when any method was patched.

## Output

Returns JSON with:

- `Success` (boolean): `false` on validation failure or when any method outcome is `Failed`; skips alone do not force failure
- `Methods` (array): Per-method `{ Kind, Method, Reason, FilePath }` where `Kind` is `Patched`, `Skipped`, or `Failed`
- `Warnings` (array): Inlining risk, pause-point interaction, and other non-fatal notes
- `PatchedTotal` (number): Methods patched in this run
- `ActivePatchTotal` (number): Methods still patched after this run
- `ClearedCount` (number): Patches removed by `--revert-all` (0 on apply)
- `Message` (string): Short summary
