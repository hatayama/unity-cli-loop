---
name: uloop-hot-reload
toolName: hot-reload
description: "Hot reload applies method-body edits and can add new methods and fields (added members are visible to edited code in the same reload within the same assembly); it can also change signatures: a return-type change applies only when the same reload (or an earlier one) covers the old signature's compiled callers, while a rename or parameter change applies as an added method and warns about compiled callers it leaves on the old signature. New top-level public types (class/struct/enum/interface/static helper) of the same assembly are introduced by the reload that declares them; other new-type shapes, use from another assembly or through Unity, asmdef changes, and members referenced from other assemblies or from files that are neither passed to the reload nor already hot-reloaded require 'uloop compile'."
---

# uloop hot-reload

Replaces method bodies in the running Editor (EditMode or PlayMode) directly from edited
project source files — no domain reload, no attributes, no source markers. Private/internal
member access, static methods, return values, async methods, and iterators all work within
the limits below, including private access inside async, iterator, lambda, local-function,
and LINQ-query bodies. Methods that cannot be patched are reported as `Skipped` or `Failed`;
one unpatchable method never aborts the rest of the run.

## Usage

```bash
uloop hot-reload --files Assets/Scripts/Enemy.cs
uloop hot-reload --files Assets/Scripts/Enemy.cs,Assets/Scripts/Boss.cs
uloop hot-reload
uloop hot-reload --revert-all
```

Multiple files are passed as one comma-separated value (or a JSON array); array options
consume exactly one value token.

A script under a brand-new `.asmdef` cannot be hot-reloaded before its first import: Unity
has not created that assembly yet. Run `uloop compile` once, then iterate with hot reload.
A new file under an existing `.asmdef` can be hot-reloaded, but it is never selected
automatically — pass it with `--files`.

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--files` | array | - | Project-relative `.cs` paths to hot-reload (method bodies, added members, and new top-level types). When omitted or empty on apply, selects compiled snapshot sources only — those whose bytes changed since the last compile snapshot, capped at 50 changed files per assembly with a warning when the cap trims the list; a file that has never been compiled is never selected and must be passed explicitly; run `uloop compile` first when no snapshot exists, or pass explicit paths when no changed source is found |
| `--revert-all` | flag | - | Remove every active hot-reload patch and added member and clear the ledger; introduced types stay loaded until the next domain reload. When set, `--files` is ignored |
| `--status` | flag | - | Lists the currently active changes (patched methods, added members, and introduced types) without applying or reverting anything. |

## Status

`uloop hot-reload --status` lists the currently active changes; it cannot be combined with
`--files` or `--revert-all`. Every kind of change is static Editor state, so after a domain
reload it authoritatively reports zero. Each `Active` row's `InvocationCount` counts calls
into the patched body since the patch was applied — a reachability signal only while the code
is being driven (`references/troubleshooting.md`).

## How It Works

The edited files are grouped by the compiled assembly they belong to. Per group an
out-of-process Roslyn worker rewrites every editable body into a static shim, the shims
compile into one shim assembly and load into the Editor domain, and each original method is
patched with a Harmony transpiler. Because a group shares one shim assembly, a body edited in
one file can call a member added in another edited file of the same assembly. Re-running after
a real edit replaces the patch; an unchanged file reports `AlreadyActive` and changes nothing
unless a sibling of the same assembly is in the reload, in which case it is re-applied so every
active patch binds to the newest shim. With a compile-time baseline only bodies that actually
changed are patched (`UnchangedTotal` counts the rest). Details:
`references/mechanism-and-lifecycle.md`.

## Scope in Brief

- Patched: ordinary method bodies and property getters with a body.
- Added members: new methods, fields, and supported properties apply as `Added` rows
  (see the scope reference for the property shapes still skipped), visible to edited code in the same reload
  within the same assembly (pass the declaring file and its callers together), and vanish
  on any compile or domain reload (an Editor-session illusion).
- New types: a top-level `public` class, struct, enum, or interface declared in an edited file
  is introduced by that reload and reported in `IntroducedTypes`. Every other shape (nested,
  `partial`, generic, `record`, non-public, `ref struct`, `unsafe`, `UnityEngine.Object`,
  `[Serializable]`, module initializer) is refused with a `Warnings` line naming the reason.
  Use from another assembly or from files outside the reload, reflection, serialization, and
  Unity message discovery still need `uloop compile`. See `references/introduced-types.md`.
- Signature changes (return type, rename, parameters) follow the added-member rules: a
  return-type change is `Skipped` unless every live compiled caller of the old signature is
  patched by this reload or an earlier one, while a rename or parameter change applies and
  warns about the call sites it leaves on the old signature.
- Constructors, operators, compiled setter/init/indexer accessors, and event accessors
  are `Skipped`; finalizers and interface members are silently not applied. `const` and
  other outside-body edits never change runtime behavior (drift is warned where
  detectable).
- A reload applies each file all-or-nothing: any `Failed` method leaves that file unapplied,
  while other files still apply; a `Failed` type leaves all files of its assembly unapplied.

Full rules and the `Skipped`/`Failed` condition tables: `references/scope-and-limits.md`.

## Workflow

Treat hot reload as the exploration phase and `uloop compile` as the landing phase:
keep edits inside the edited files, collect structural changes, and compile once —
every compile drops all patches and pause points and resets the PlayMode session (the compile response's Warning states how many were live).
While hot-reload changes are active, `AutoRefreshHeld` is true so returning focus does not
recompile; `uloop compile` releases the hold, and `--revert-all` only when no introduced type
remains.
One-shot methods (`Awake`, `Start`, initialization helpers) patch successfully but show
no effect on the call that already ran; the response marks them with `LifecycleNote`.
For values you expect to tune while playing, expose a static property getter instead of
a `const`.

## Reference Guides

All files live in `references/` beside this skill; read the one whose trigger matches:

- `references/scope-and-limits.md` — full scope rules: added members, signature changes, `Skipped`/`Failed` tables, source baselines, one-shot code, tunable getters.
- `references/mechanism-and-lifecycle.md` — patch mechanism, convergence, what survives which reload, Editor-code iteration without PlayMode.
- `references/troubleshooting.md` — `Patched` but no behavior change, JIT inlining, reading `--status` and `InvocationCount`.
- `references/pause-point-interaction.md` — how patches re-target or suppress armed pause points; one-way reachability checks.
- `references/introduced-types.md` — new types a reload can introduce: supported shapes, refusal wording, identity and lifetime, why a new file is never selected automatically.
- `references/output.md` — every response field: `ErrorCode`, `NextActions`, `Methods` rows, `Warnings`, totals.
