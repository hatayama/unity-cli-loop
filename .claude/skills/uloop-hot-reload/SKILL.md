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
| `--status` | flag | - | Lists the currently active changes (patched methods and added members) without applying or reverting anything. |

## Checking what is currently patched

`uloop hot-reload --status` lists the methods whose bodies are currently replaced,
without applying or reverting anything. It cannot be combined with `--files` or
`--revert-all`. Patches are static Editor state, so the answer is authoritative: after
a domain reload it reports zero patched methods, which is exactly when an
`ActivePatchTotal` remembered from an earlier response has gone stale.

Each `Active` row's `InvocationCount` counts calls into the patched body since that patch
was applied; re-running hot reload on the same method resets it to zero. While Unity is
paused — including while a pause-point hit holds the game — the player loop does not
advance, so game-driven calls stop and the count freezes; calls you make yourself (for
example through `uloop execute-dynamic-code`) still increment it. A frozen count during a
pause only means game-driven calls are not running; it says nothing about whether call
sites reach the patch. Resume first
(`uloop control-play-mode --action Resume`, or clear the owning pause point), drive the
game, and only then read `InvocationCount` as a reachability signal.

## How it works

1. Resolves each file to its compiled assembly via `CompilationPipeline`.
2. Rewrites each editable method body into a static shim in an out-of-process Roslyn worker. When an async, iterator, lambda, local-function, or LINQ-query body touches private/internal members, those accesses are rewritten to accessor delegates so the body can compile and run from the shim assembly (the delegation shape below).
3. Compiles the shims against publicized reference copies, loads the result into the Editor domain, and binds every shim type's accessor delegates (`__BindAccessors`) before any patch is applied.
4. Patches each original method with a Harmony transpiler (ID `io.github.hatayama.uloop.hot-reload`) in one of two shapes: transplant copies the shim's IL into the original method, while delegation rewrites the original to forward its arguments to the shim, which runs as normally compiled code.

Re-running on the same method replaces its previous patch; `ActivePatchTotal` tracks the
ledger across runs.

## Scope and limits

Only ordinary method declarations and property getters with a body are patched.
Constructors, operators, and explicit event accessors are reported as `Skipped`
when edited (with a verified baseline, unchanged members of those kinds produce
no row). Finalizers and `interface` members (including default interface
implementations) are never scanned: **edits** to them produce **no per-method
entry at all** and are silently not applied — use `uloop compile` for those.
Adding a constructor, operator, or explicit event accessor is reported as
`Skipped` as well, same as an edit to an existing one.

### Added methods and fields

Hot reload can add new methods and fields alongside body edits, under one hard rule:
an added member is visible only to edited code in the same file. Compiled, unedited
code cannot see it, and neither can anything that resolves members by name at
runtime: reflection (`GetType().GetMethod("NewM")` returns `null`), Unity's message
discovery (an added `Update` or `OnCollisionEnter` on a `MonoBehaviour` is never
invoked — a `Warnings` entry names it), UnityEvent/inspector wiring, and
serialization. Referencing an added member from a different file fails that file's
hot reload with the usual new-member hint; run `uloop compile` instead.

An added method reports its own row with Kind `Added`; the edited methods that call
it report `Patched` as usual. Added `virtual`/`override`/`abstract` methods, explicit
interface implementations, and generic methods are `Skipped`; a method-group or
delegate reference to an added instance method skips the referencing method instead.
Pause points cannot bind to lines inside an added method — enabling one there fails
with the normal not-found error.

An added field's values live in a side table that follows each instance's lifetime
(statics live per domain). Its initializer does not run at construction time; it runs
on the field's first access from edited code — once per instance, or once per domain
for statics. Initializer expressions are limited to literals and externally visible
static calls (`= 5`, `= Math.Abs(x)`); anything touching the host type or instance
state skips the field's readers and writers with a per-method reason. Added `const`
values are folded into edited bodies as literals, like `nameof`.

Added members are an Editor-session illusion. Any real compile or domain reload
drops them all: added methods disappear from the ledger and added-field values are
discarded — they do not migrate into the compiled field's initializer semantics.
Deleting an added member from the edit and re-applying (or reverting the file to its
compiled source) removes it from the ledger on that run. Deleting a *compiled*
member is reported in `Warnings`, but its IL remains callable from unedited code
until `uloop compile`.

Adding a constructor, operator, or explicit event accessor is still out of
scope and is reported as `Skipped`, same as edits to them. Adding a type
(`class`, `struct`, `enum`, `record`), a property, an event, or an indexer
is still out of scope. Added properties are reported per member: the
property's getter appears as a `Skipped` row that says to use a 'const' or
a plain added field for the value, or to run 'uloop compile'. Types, events,
and indexers are not reported per member — no `Skipped` row names them; at
most they surface as outside-body drift in `Warnings`. Treat their silence
as "not applied" and land them with `uloop compile`.

Outside method bodies, only member additions (previous section) take effect.
Every other declaration edit — changing a `const` value, a compiled field's
initializer, an attribute — leaves runtime behavior unchanged even though the
response reports `Success` — shims resolve those symbols
against the already-compiled assembly, and C# bakes `const` values into IL at compile
time. Changed `const` values (including enum member values) are detected and reported
as a `Warnings` entry naming the constant and both values. When a verified source
baseline is available (next paragraph), other outside-body drift — existing-field
initializers, attributes, and other declaration edits — is reported as a `Warnings`
entry as well (handled added members and reported removed members are excluded
from this generic warning); without a baseline it stays silent. Either way, use
`uloop compile` for such edits.

### Signature changes: return type, rename, parameters

Changing a compiled method's return type is applied as a remove-plus-add: the old
method stays in the compiled assembly (like any removed member), the new signature
becomes an added method with its own `Added` row, and the edited methods that call
it report `Patched`. Every added-member rule applies — same-file visibility, the
Editor-session illusion, and the `virtual`/generic/interface exclusions.

A gate protects compiled callers: the change applies only when every live compiled
call site of the old signature is patched by the same file's reload. A caller in
another file — even one edited in the same run — or an *unedited* method in the
same file (an implicit `int`→`long` widening can leave a caller's source
untouched) would keep calling the old method silently, so the run reports the
changed method and its edited callers as `Skipped` instead; land the change with
`uloop compile`. When every uncovered caller is in the edited file itself, the
`Skipped` reason says so: editing those callers' bodies and reloading again
applies them together without `uloop compile`.
Call sites inside methods that the same edit removes or
re-signatures do not gate: those compiled bodies are already stale, and anything
still reaching them stays on the consistent old behavior.

Renaming a method or changing its parameter list follows the delete rules rather
than the gate: the new signature is an ordinary added method, the old one is
reported removed, and a `Warnings` entry names each compiled call site of the old
signature that the reload leaves unpatched — those call sites keep the previous
behavior until `uloop compile`. Deleting a method emits the same warning when
compiled callers remain.

Field declarations are stricter: when a compiled field's type — or its `static`/
`const` modifier — differs from the edited source, every edited method that reads
or writes that field is `Skipped` with a per-method reason. Retyped storage has no
session illusion; run `uloop compile`.

### Explore with hot reload, land structure with compile

Treat hot reload as the exploration phase and `uloop compile` as the landing phase. While
diagnosing or tuning, keep every edit inside existing method bodies — inline a would-be
helper's logic at its call site for now instead of extracting it. New helper methods
and fields can now be explored directly with hot reload inside the same file. When
the change needs a new type, cross-file visibility, runtime name-based lookup, or
serialization, collect those and run `uloop compile`
once: every compile triggers a domain reload that drops all active patches and pause points
and resets the running PlayMode session, so compiling member-by-member pays that cost
repeatedly. After the one compile, re-enter PlayMode and continue exploring on the freshly
compiled code.

### One-shot code: a patch only changes the next call

Hot reload changes what a method does on its *next* call — it never re-runs a call that
already happened. Methods that run exactly once per session (`Awake`, `Start`, `OnEnable`,
initialization helpers called from them, anything that seeds state at startup) patch
successfully but show no effect: the one call they get is already in the past when the
patch lands. The response marks Unity's one-shot lifecycle messages with `LifecycleNote`
(see Output). To see an initialization change take effect, run `uloop compile` and restart
Play Mode — with Domain Reload enabled (the default), a fresh Play entry reloads the
domain and drops the patch, so the patched body alone cannot carry the change into the
next session. Better, keep values you expect to
tune out of one-shot paths entirely: read them in a body that runs per frame or per event,
and patch that body instead.

### Tunable values: prefer a getter over a const

`const` edits never take effect through hot reload: C# bakes const values into every
call site at compile time. When you expect to tune a value while Play Mode is running
(speeds, amplitudes, sensitivities), expose it as a static property getter instead:

    public static float HeightAmplitude => 5f;

A getter body is an ordinary patchable method body, so editing the literal and running
`uloop hot-reload` updates every consumer on its next call — across all files, without
restarting Play Mode. Keep `const` for values you never tune at runtime.

This works only for consumers that read the getter on a live call path — a per-frame
`Update`, a physics step, an event handler. A consumer that read the getter once during
initialization and cached the value in a field never observes the new value: the patch
lands, but nothing reads the getter again (the one-shot rule above).

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

Property getters with a body (including expression-bodied properties) are patched
like ordinary methods. Setter, init, and indexer accessors with explicit bodies are
reported per-accessor as `Skipped`, so an edited accessor never disappears from the
response silently; with a verified baseline, accessors unchanged from it produce no row.

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
| Property setter, init, or indexer accessor with an explicit body | Accessor patching covers getters only; `uloop compile` applies setter/init/indexer edits |
| Constructor (instance or static), operator, conversion operator, or explicit event accessor (add/remove) | Out of scope for v1; `uloop compile` applies these edits |
| Method raises, invokes, or reads a field-like event (anything beyond `+=`/`-=`) | C# only allows `+=`/`-=` on an event outside its declaring type, so the raising body cannot compile from the shim assembly |

### Failed — flips `Success` to `false`

| Condition | Notes |
|-----------|-------|
| File does not belong to any compiled assembly | Per-file entry with `Method` = `(file)`; only `Assets/` and `Packages/` sources resolve |
| Loaded assembly differs from the one on disk (pending compile) | Run `uloop compile` first, then retry |
| Source file fails to parse | Per-file entry carrying the parse errors |
| Method signature not found in the loaded assembly | Usually a stale assembly; run `uloop compile`. In-file renames and signature changes are classified as added members before reaching this point |
| Shim compile error (e.g. the body calls a member that does not exist yet) | Failing methods are isolated: each reports `Failed` with its own compiler errors (plus the `uloop compile` hint when they indicate a missing member) while the remaining methods still patch. When errors cannot be attributed per method, the whole file reports one `(shim-compile)` entry; if only one method was edited, the failure is attributed to that method's name instead |
| Patch rejected or crashed at apply time (e.g. `[BurstCompile]`, a patch-engine emit failure) | The entry carries the rejection reason or the underlying engine error; other methods in the run still apply |
| Accessor binding failed for a shim type | The source references a member the compiled assembly does not have yet; every delegation-patched method in that shim type reports the binder error — run `uloop compile` and retry |
| The signature-change gate could not finish the run safely — the retry that skips a gated change failed, or shim-compile isolation dropped an edited caller that had covered a change | Per-file entry with `Method` = `(signature-change-gate)` carrying the specific cause; nothing from the file is applied — fix the failing edit or run `uloop compile` |

## When a patch reports `Patched` but behavior does not change

Run `uloop get-logs` first. An exception thrown inside the patched body, or an
error logged while the reload applied, appears there immediately and explains
"Patched but no visible change" faster than any marker-based digging.

`Patched` means the method body was replaced, not that the method ran. Before suspecting
the patch, confirm the method is actually reached: arm `uloop enable-pause-point --mode
trace` on a line inside the edited method body — it resolves against the patched body
directly (see the pause point interaction below) — drive the game, and check the hit
count: zero hits means the calling path never reached the method, which no patch (or
compile) can fix. To chase an early return inside the method, arm a second marker on the
suspected early-return line. The other known cause is JIT inlining, which the response flags
with a single aggregated warning listing the at-risk methods: `[AggressiveInlining]` methods
always, tiny bodies only when the Editor's Code Optimization mode is Release (the default
Debug mode does not inline them). If `uloop hot-reload --status` shows the method's
`InvocationCount` increasing, the calls you exercised are reaching the patched body and the
warning did not apply to them — call sites you have not exercised may still run inlined old
code. Take both readings while the code is actually being driven — PlayMode running, or your own
`uloop execute-dynamic-code` invocation for Editor-assembly methods; a count frozen during
a pause is not evidence either way.

## Convergence and lifecycle

- The input is the real project source file, so a later `uloop compile` (real compile +
  domain reload) lands the exact same edit permanently. There is nothing to undo first;
  behavior converges by construction.
- Patches and loaded shim assemblies are static Editor state and disappear on the next
  domain reload — that includes entering Play Mode with Domain Reload enabled (the
  default), not just `uloop compile`. `uloop control-play-mode --action Play` warns with
  the counts when it is about to drop patches or pause points. There is no persistence
  and no automatic re-apply.
- Never reflected by hot reload: initializer changes on compiled fields and new
  types. Those always need `uloop compile`. Signature changes — return type,
  rename, parameter list — are handled through the added-member rules and the
  return-type gate above: same file, same Editor session, compiled callers
  protected by skip or warning.
  (Added methods and fields are reflected per the rules above, but only for the
  current Editor session and only within their own file.)
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

A pause point inside a Unity physics message (`OnCollisionEnter2D`, `OnTriggerEnter`,
and similar) or inside a method already bound into a delegate before enable can stay
at zero hits even though the body runs: Unity may have resolved that dispatch path
before the marker was armed (the pause-point skill's troubleshooting covers recovery).
Hot-reloading a temporary log line into the same body gives a one-way reachability
check — the log appearing (read it with `uloop get-logs`) proves the body ran even
though the marker missed. The log staying absent proves nothing, because the same
cached dispatch can bypass a hot-reload patch too.

## Output

Returns JSON with:

- `Success` (boolean): `false` on parameter validation failure or when any method outcome is `Failed`. `Skipped` outcomes alone never force `false`
- `Methods` (array): Per-method `{ Kind, Method, Reason, FilePath, InvocationCount, LifecycleNote }` where `Kind` is `Patched`, `Skipped`, `Failed`, or `Added` on apply runs, and `Active` or `Added` on `--status` runs; empty on `--revert-all` runs. `InvocationCount` is meaningful on `Active` rows (calls since the current patch was applied); it is `0` on apply/revert outcomes. `LifecycleNote` is set when a patched method is a Unity one-shot lifecycle message (`private void Awake`/`Start`/`OnEnable`/`OnDisable`/`OnDestroy` on a `MonoBehaviour`); empty otherwise — it does not change `Kind`. `Added` rows carry the added member's signature and file; their `InvocationCount` is always `0` (added-member calls are not instrumented). Example `--status` row: `{ "Kind": "Added", "Method": "Ns.Host.NewHelper(System.Int32)", "Reason": "", "FilePath": "Assets/Scripts/Host.cs", "InvocationCount": 0, "LifecycleNote": "" }`
- `Warnings` (array): Non-fatal notes — one aggregated line listing the patched methods at risk of being already JIT-inlined into existing callers — those marked `[AggressiveInlining]`, plus (only when Code Optimization is Release) those with tiny pre-patch bodies — meaning the change may not show at those call sites, the pause-point interaction above, and the const drift, outside-body drift, and missing-baseline entries described in "Scope and limits"
- `PatchedTotal` (number): Methods patched in this run
- `UnchangedTotal` (number): Methods left untouched because their bodies match the source baseline from the last compile; `0` when no baseline was available
- `ActivePatchTotal` (number): Active changes after this run — patched methods plus added members. `--revert-all` clears both and reports the combined count in `ClearedCount`
- `ClearedCount` (number): Patches removed by `--revert-all` (0 on apply)
- `Message` (string): Short summary
