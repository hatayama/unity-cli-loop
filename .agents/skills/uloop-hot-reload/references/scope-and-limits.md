# Hot Reload Scope and Limits

Only ordinary method declarations, property getters, setters, and init accessors, and
indexer getters and setters with a body are patched. A property added to a compiled type applies as an added member
instead (see below).
Constructors, finalizers, operators, and explicit event accessors are reported as
`Skipped` when edited (with a verified baseline, unchanged members of those kinds
produce no row). Methods of an `interface`, including default interface
implementations, are reported as `Skipped` the same way; a default property getter or
setter of an `interface` is patched like any other. Use `uloop compile` for the
skipped ones.
Adding a constructor, finalizer, operator, or explicit event accessor is reported as
`Skipped` as well, same as an edit to an existing one.
An edited explicit interface method (`void IFoo.Run()`) or property getter or setter (`int IFoo.Value { get; set; }`) is patched like an ordinary one; its row reads `Ns.Type.Ns.IFoo.Run()` or `Ns.Type.Ns.IFoo.get_Value()`. Explicit interface indexer and `init` accessors are reported as `Skipped`.
Methods and property getters of `partial` types are patched like any other; the
other parts of the type are read from the assembly's source files.

## Added methods and fields

Hot reload can add new methods and fields alongside body edits, under one hard rule:
an added member is visible only to edited code in the same reload, within the same
compiled assembly. Files that compile into one assembly are reloaded together through
one shim assembly, so a body edited in one of them can call a member added in another
— pass the declaring file and its callers to the same command. Compiled, unedited
code cannot see it, and neither can anything that resolves members by name at
runtime: reflection (`GetType().GetMethod("NewM")` returns `null`), Unity's message
discovery (see "Added Unity messages" below), the Unity Test Runner (an added `[Test]` /
`[UnityTest]` method is not enumerated by `uloop run-tests --skip-compile` — a
`Warnings` entry names it; run `uloop compile` first), UnityEvent/inspector wiring, and
serialization. Referencing an added member from a file that is neither passed to this reload nor
already hot-reloaded, or from another assembly, fails that file's hot reload with
the usual new-member hint; run `uloop compile` instead. Unchanged files of the same
assembly that already hold active patches are re-applied automatically so they bind
to the newest shim. An edited body that reaches an added method through a
compiled type still binds when the receiver's static type name, method name, and
argument count uniquely match and the call is static only when the receiver is a
type name; the lookup uses that static type itself (not a base type), requires an
exact argument count, and does not cover `?.`.

An added method reports its own row with Kind `Added`; the edited methods that call
it report `Patched` as usual. Added `virtual`/`override`/`abstract` methods, explicit
interface implementations, and generic methods are `Skipped`; a method-group or
delegate reference to an added instance method skips the referencing method instead.
A private member added in the same reload is reached directly, not through an accessor
delegate: a body may read or write an added private static property and call an added
private method with `ref`/`out` arguments. A *compiled* private or internal static property
can also be read, assigned, and compound-assigned (through accessor delegates), but a call
to a *compiled* private method with `ref`/`out` arguments still skips the method (see the
`Skipped` table below).
Pause points cannot bind to lines inside an added method — enabling one there is
refused with a message naming the added method (see
[pause-point-interaction.md](pause-point-interaction.md)).

An added field's values live in a side table that follows each instance's lifetime
(statics live per domain). Its initializer does not run at construction time; it runs
on the field's first access from edited code — once per instance, or once per domain
for statics. Initializer expressions are limited to what a static lambda on a separate
shim type can evaluate: literals, externally visible static calls (`= 5`,
`= Math.Abs(x)`), array creation whose elements are themselves such expressions
(`= new int[] { 1, 2, 3 }`), and a `public` constructor of an externally visible compiled
type (`= new List<int>()`, `= new(1f, 2f)`, `= new List<int> { 1, 2 }`), which also runs on
first access rather than when the instance is created. Anything touching the host type or
instance state skips the field's readers and writers with a per-method reason, and so does
a constructor the compiled type does not hold, an object initializer
(`new T { X = 1 }`), a non-public constructor or type, and a nested type named without its
outer type (write `new Outer.Inner()`, or `new()` when the field's type is spelled) —
including when such a creation is an array element or a constructor argument. A collection
initializer also needs a `public` `Add` on a type outside the assembly being edited, and a
type of that assembly skips any constructor when it has a non-public one and this reload was
not given its file. A
type a reload introduces applies through a constructor its retained assembly holds as
`public`, for as long as this Editor session keeps it active — including in the reload that
also edits a body of that type. A constructor this reload adds to it, or one whose
parameters the retained assembly does not hold, keeps the readers and writers `Skipped`.
Because the initializer runs on first access, one this reload adds to — or changes on — a
field an earlier reload already added never reaches a value the side table already holds:
it runs only where the field has not been read yet. That reload names those fields in
`Warnings`; the way to reach the existing instances is to assign the value inside a patched
method, rename the field, or run `uloop compile`.
To apply such a field without compiling, declare it
without an initializer — it starts at `default(T)` — and assign it inside the patched
method instead; for a reference type, guard that with
`if (_field == null) { _field = new List<int>(); }`. `??=` is not rewritable and keeps
the method `Skipped`. So do the other writes the store rewrite cannot carry: a
deconstruction into the field (`(_field, other) = pair;` — assign it in its own statement
`_field = value;` instead), an assignment whose value is consumed, a receiver that would be
evaluated twice, and a write to a member of a value-type added field or an instance method
call on it (copy the field into a local, change the local, and assign it back). When every
method that assigns such a field is `Skipped` but a
method that reads it was applied, a `Warnings` entry names the field, the skipped
writers and the reader: the reader sees `default(T)`. Fix the skip reason and reload
again, or run `uloop compile`. Accessors of properties added in the same edit count as
readers and writers too: an applied getter is a reader, and a skipped setter is a skipped
writer. An added auto-property without an initializer gets the same check, with a warning
that starts `Added auto-property`. Added `const` values are folded into edited bodies as literals,
like `nameof`. Pause-point
`CapturedVariables` never includes added fields; `enable-pause-point` warns when the
resolved type has any — their values live in the hot-reload shim, and a
`uloop execute-dynamic-code` snippet cannot name them as members (it compiles against
the compiled assembly, so that fails with CS1061). Read one there with
`HotReloadAddedFieldWiring.TryReadInstanceField` (`TryReadStaticField` for a static
field) instead. When such a
failure quotes the name of an active added member, the diagnostic's `Hint` says so
rather than leaving the error reading as a typo. Naming the field is what fails, not
reaching it: an `execute-dynamic-code` snippet can still read and write an added field
by name through the wiring entry point, which is how a value or scene reference gets
into an added `[SerializeField]` without a compile — see
[added-field-wiring.md](added-field-wiring.md).

A type a reload introduces can call added members of a compiled type from its ordinary
methods and get-only properties: its artifact compiles those bodies as stubs, and the same
reload patches the real bodies in. From a constructor, initializer, setter, indexer,
operator or event accessor such a reference still fails with CS1061/CS0117 and needs a
compile — see [introduced-types.md](introduced-types.md).

Added members are an Editor-session illusion. Any real compile or domain reload
drops them all: added methods disappear from the ledger and added-field values are
discarded — they do not migrate into the compiled field's initializer semantics.
Deleting an added member from the edit and re-applying (or reverting the file to its
compiled source) removes it from the ledger on that run. Deleting a *compiled*
member is reported in `Warnings`, but its IL remains callable from unedited code
until `uloop compile`.

Adding a constructor, finalizer, operator, or explicit event accessor is still out of
scope and is reported as `Skipped`, same as edits to them. With a verified
baseline, event declarations are compared per accessor, so only the edited
add or remove appears as a `Skipped` row. A newly added explicit event, or
an edit before the first compile snapshot, still reports both accessors.
Adding a nested type or an indexer to a compiled type is still out of scope; an added
field-like event applies within the limits described below.
A new top-level `class`, `struct`, `enum`, or `interface` is introduced instead, within the
limits in [introduced-types.md](introduced-types.md); a `record` is refused there.
A member added to a compiled enum is out of scope too: it is not folded like an added
`const`, so every body that names it fails with CS0117, including bodies in the same
reload. Write the underlying value as a cast (`(MyEnum)3`) or run `uloop compile`.
While the enum's file is in the reload, an added member that passes the enum to or takes
it from compiled code or an introduced type is skipped. The `Skipped` row names the step
for that run (pass a file, leave the enum file out, undo the edit and leave it out, or
compile), chosen from whether the file was passed, carried in, or already holds patches.
This holds for a type the reload introduces as well: its compile fails, the failure
reason says the name is an enum member this reload adds, and the enum-member and
changed-`const` warnings of the files passed to that reload stay in `Warnings` even though
that failure stops the file. The drift of a changed sibling file that was not passed is not
reported on this failure path.
When `--files` is omitted and the enum's file has no edit besides its new enum members,
the reload leaves that file out instead, so the added members of the other files apply;
a `Warnings` line names the left-out file and the enum members that still need
`uloop compile`. A file that already holds patches or declares a new type stays in the
reload.

An added property applies unless its shape is listed below. A bodied getter or setter is
emitted like an added method;
an auto-property is emitted as a pair of accessors over the added-field store, so its
value follows the same lifetime as an added field and appears in `--status` as one
`Added` row per accessor plus one `AddedField` row for the value — three rows for a
get/set auto-property, two for a getter-only one. The rows read
`Ns.Type.get_X()`, `Ns.Type.set_X(System.Int32)`, and `Ns.Type.X`. The added-fields lifetime warning covers it. Accessors are not
pause points, and the value never appears in `CapturedVariables`.

These property shapes stay `Skipped` with a per-member reason: a setter without a
getter, `virtual`/`override`/`abstract`/interface, an explicit interface
implementation, an `init` accessor, a host that is a `struct` or a generic type, a value type
the shim assembly cannot see or cannot resolve, an auto-property initializer the added-field
rules above refuse, and a name the compiled assembly already declares as a field or an event. Edited callers are skipped too when they use a
shape the accessor shim cannot carry: compound assignment or increment, an assignment
whose value is consumed, a deconstruction target, an object initializer, a property
pattern that matches the property, `nameof`, `ref`/`out`/`in`, and conditional access
on the property itself. A compound assignment or increment keeps hot reloading when it is
rewritten as a plain assignment statement (`X = X + 1;`).

A nested type added to a compiled type is named in a `Warnings` line (`Nested type
requires a compile`), and its methods are reported as `Skipped`; so are the bodied
accessors of an added indexer. An added event the store cannot hold (a struct host, a
delegate type not visible outside the assembly) gets no row of its own: the methods
that use it are `Skipped`, and an event nothing uses changes no behavior. Land all of
these with `uloop compile`.

Outside method bodies, only member additions (previous section) take effect.
Every other declaration edit — changing a `const` value, a compiled field's
initializer, an attribute — leaves runtime behavior unchanged even though the
response reports `Success` — shims resolve those symbols
against the already-compiled assembly, and C# bakes `const` values into IL at compile
time. Changed `const` values (including enum member values) are detected and reported
as a `Warnings` entry naming the constant and both values. The scan includes
changed sibling files in the same assembly, not only the file passed to
`--files`. When a verified source
baseline is available (next paragraph), other outside-body drift — existing-field
initializers, attributes, and other declaration edits — is reported as a `Warnings`
entry as well (handled added members and reported removed members are excluded
from this generic warning); without a baseline it stays silent. Either way, use
`uloop compile` for such edits. The comparison is against the source the loaded
assembly was compiled from, so an earlier edit outside method bodies keeps this
warning on every reload until `uloop compile`. Comment-only edits (line, block, and
XML documentation comments) do not count.

## Signature changes: return type, rename, parameters

Changing a compiled method's return type is applied as a remove-plus-add: the old
method stays in the compiled assembly (like any removed member), the new signature
becomes an added method with its own `Added` row, and the edited methods that call
it report `Patched`. Every added-member rule applies — same-reload visibility within
the assembly, the Editor-session illusion, and the `virtual`/generic/interface
exclusions.

A gate protects compiled callers: the change applies only when every live compiled
call site of the old signature is in the same assembly and patched by the same reload.
A caller this reload did not edit — in another file or an *unedited* method in the
edited file itself (an implicit `int`→`long` widening can leave a caller's source
untouched) — would keep calling the old method silently, so the run reports the
changed method and its edited callers as `Skipped` instead; land the change with
`uloop compile`. A caller in another assembly gates the change even when this or an
earlier reload patched it: that patch is compiled against the compiled assembly, where
the old signature still exists. When every uncovered caller is in the edited file itself, the
`Skipped` reason names those callers: editing their bodies and reloading again
applies them together without `uloop compile`.
Call sites inside methods that the same edit removes or
re-signatures do not gate: those compiled bodies are already stale, and anything
still reaching them stays on the consistent old behavior.
If an earlier reload already patched the compiled call sites in the same assembly, a later signature change applies without editing the callers; the response then carries a warning naming the call sites this run re-applied on the new signature.

Renaming a method or changing its parameter list follows the delete rules rather
than the gate: the new signature is an ordinary added method, the old one is
reported removed, and a `Warnings` entry names each compiled call site of the old
signature that the reload leaves unpatched — those call sites keep the previous
behavior until `uloop compile`. Deleting a method emits the same warning when
compiled callers remain. A caller whose patch is active when the reload ends —
patched by this reload in any assembly, or kept from an earlier reload — is left out,
because it no longer runs its compiled body. The warning does not check what the
patched body calls, and two leftovers of the compiled caller can still reach the old
method: a copy the JIT inlined into another method before the patch, and a delegate to
the old method the caller created before it. A call inside a lambda or local function
stays listed under its compiler-generated name even when the method declaring it is
patched.
An added member that a later reload re-signatures or deletes has no compiled callers, but a
hot-reloaded caller that does not apply again in that reload keeps calling the member's
earlier body; `Warnings` then names that call (see [troubleshooting.md](troubleshooting.md)).

Field declarations are stricter: when a compiled field's type — or its `static`/
`const` modifier — differs from the edited source, every edited method that reads
or writes that field is `Skipped` with a per-method reason. Retyped storage has no
session illusion; run `uloop compile`.

## Explore with hot reload, land structure with compile

Treat hot reload as the exploration phase and `uloop compile` as the landing phase. While
diagnosing or tuning, keep every edit inside existing method bodies — inline a would-be
helper's logic at its call site for now instead of extracting it. New helper methods
and fields can now be explored directly with hot reload, across the files of one
assembly: pass the files you edited; unchanged files that already hold active
patches are re-applied with that group. A top-level class, struct, enum, or interface declared `public`, `internal`,
or without an access modifier in an edited file is introduced by that reload too, and can use the
assembly's internal members (`introduced-types.md`). When the change needs
another new-type shape, visibility from another assembly or from a file outside the reload,
runtime name-based lookup, or serialization, collect those and run `uloop compile` once: every compile triggers a domain reload that drops all active patches and pause points
and resets the running PlayMode session, so compiling member-by-member pays that cost
repeatedly. After the one compile, re-enter PlayMode and continue exploring on the freshly
compiled code.

## Added Unity messages

Unity finds a `MonoBehaviour`'s messages by name on the compiled class, so a message a
reload added is not one the engine knows about. While Play Mode runs, hot reload stands a
generated proxy component next to each live instance of the target type and forwards the
message from there, so an added `Update`, `OnTriggerEnter`, or `OnMouseDown` does run. The
method's row says which answer it got in `LifecycleNote` (see Output).

- Forwarded: `Start`, `Update`, `LateUpdate`, `FixedUpdate`, `OnGUI`, the collision, trigger,
  and mouse messages (2D included), and the application/pause/focus messages. `Update`,
  `LateUpdate`, `FixedUpdate`, and `OnGUI` are forwarded only while the target itself is
  active and enabled; the event messages are forwarded as they arrive.
- Not forwarded, and listed together in one `Warnings` line: `Awake`, `OnEnable`,
  `OnDisable`, `OnDestroy`, the editor-only messages (`Reset`, `OnValidate`,
  `OnDrawGizmos`, `OnDrawGizmosSelected`), and any message declared with a return value or a
  `ref`/`out` parameter. Run `uloop compile` to have the engine dispatch those.
  When such a message's body is also skipped for a private access with no accessor rewrite,
  its `Skipped` row names `uloop compile` as the only step: no rewrite of the body would make
  the engine call it.

To re-run an edited compiled `OnEnable` on live objects — for example after adding a
subscription to it — toggle the component around the reload instead of compiling: set
`enabled = false` with `execute-dynamic-code`, run `hot-reload`, then set `enabled = true`.
Unity calls the old `OnDisable` (so the old subscription is removed) and then the patched
`OnEnable`; coroutines keep running because only the component is toggled. An edited
`OnDisable` runs the next time the component is disabled; to run it now, toggle `enabled`
after the reload. This works only for an `OnEnable` / `OnDisable` the compiled class already
has; an added one is not called.

The proxies exist only for the running session: nothing is attached outside Play Mode, and a
compile or a domain reload drops them along with every other patch. Execution order relative
to other components is not guaranteed — a proxy is its own component, so an added `Update`
does not run at the position the compiled one would. The reconcile that attaches proxies
scans the open scenes for instances of the target types on each editor update, which costs a
`FindObjectsByType` per bound type; it does nothing at all while no added message is active.

## One-shot code: a patch only changes the next call

Hot reload changes what a method does on its *next* call — it never re-runs a call that
already happened. Methods that run exactly once per session (`Awake`, `Start`, `OnEnable`,
initialization helpers called from them, anything that seeds state at startup) patch
successfully but show no effect: the one call they get is already in the past when the
patch lands. An *added* `Start` is the exception, and only because it is not a patch at all:
the proxy that carries it runs it once on each instance that already exists, at the moment
the proxy attaches. A later reload that only changes method bodies keeps the attached proxies,
so `Start` does not run again; it does when the reload changes which messages the type adds or
their signatures, because the proxy is rebuilt. The response marks these with `LifecycleNote` (see Output) — both direct one-shot
lifecycle messages and methods whose every compiled caller is a one-shot lifecycle message on a
`MonoBehaviour`. The caller check is conservative: when the scan cannot prove exclusivity (a
missing assembly, reflection, or event-driven calls), or when proving it would read more than
one compiled assembly the call-site cache does not hold yet (as on the first run after a domain
reload), the note is omitted; the run then reads those assemblies in the background, so the
next run can prove it. Only compiled callers
are counted: a method hot reload added or patched that calls the method already runs the patched
body, which the note does not see. To see an
initialization change take effect, run `uloop compile` and restart
Play Mode — with Domain Reload enabled (the default for projects created before Unity 6.6), a fresh Play entry reloads the
domain and drops the patch, so the patched body alone cannot carry the change into the
next session. Better, keep values you expect to
tune out of one-shot paths entirely: read them in a body that runs per frame or per event,
and patch that body instead.

## Tunable values: prefer a getter over a const

`const` edits never take effect through hot reload: C# bakes const values into every
call site at compile time. When you expect to tune a value while Play Mode is running
(speeds, amplitudes, sensitivities), expose it as a static property getter instead —
adding one in the same edit works, so the getter does not have to exist yet:

    public static float HeightAmplitude => 5f;

A getter body is an ordinary patchable method body, so editing the literal and running
`uloop hot-reload` updates every consumer on its next call — across all files, without
restarting Play Mode. JIT-inlined call sites are the exception — the reload response's
`Warnings` lists the at-risk methods (see [troubleshooting.md](troubleshooting.md)).
Keep `const` for values you never tune at runtime.

Add the getter under a new name rather than replacing an already compiled `const` with a
getter of the same name: to the compiled assembly that name is still a field, so the edit
is `Skipped` and the old constant keeps being inlined. Leave the `const` in place, add
`HeightAmplitudeValue` (or any unused name) beside it, and point the call sites at it.

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
to establish the baseline. A member of that file without a body (an interface,
abstract, or extern method) that has no body in the compiled assembly either is
`Skipped` with a reason saying it could not be compared: there is nothing to patch, but
an edit to only its default values, attributes, or parameter names goes undetected, so
compile only if you made one. Such a row does not make `--compile-on-skip` compile, does
not count as a skipped edit in `Outcome` (a file holding nothing else is
`NothingToApply`), and does not bring the all-skipped advice to compile. An edit that
removes a compiled body keeps the ordinary `Skipped` reasons, because the compiled
body runs until a compile. Files the reload only re-applied as siblings share one
such line per reason, `N re-applied sibling file(s) ...: <files>`, instead of one
line each. A file with a declaration that hot reload refused to introduce (its
`Warnings` line says the type requires a compile) gets no such line, because that
compile also establishes its baseline. This also holds for an existing file that
gains such a declaration, for example a nested type or a delegate.
A source whose length and last write time still equal those recorded when its baseline
was captured is taken to match it without being read, in later Editor sessions as well; a
rewrite that keeps both (for example a copy that preserves timestamps) is noticed only once
its assembly compiles again, which captures the baseline anew. A baseline captured by an
earlier package version carries no such record and is compared by bytes once per Editor
session. This applies wherever a source is compared with its baseline: finding
drifted siblings, deciding which live patches to re-apply after a skip, and selecting
files when `--files` is omitted.
A file saved while the compile was running — after the compiler read it — is marked when
the snapshot is captured: a run without `--files` selects it, and its methods are all
patched with a warning, until the next compile.

Property getters and setters with a body (including expression-bodied ones) are patched
like ordinary methods; a setter's row reads `Ns.Type.set_X(<type>)`. Indexer accessors
are patched the same way; their rows read `Ns.Type.get_Item(<index types>)` and
`Ns.Type.set_Item(<index types>,<type>)`, or use the `[IndexerName]` name. On a type an earlier reload introduced, indexer accessors stay `Skipped`; run `uloop compile`. A patched
setter changes what the next assignment does: values stored by earlier assignments stay
as they are. A property's `init` accessor is patched like a setter and changes what the next
object initializer does; one whose body assigns a readonly field or an init-only property stays `Skipped` (setting an init-only property in an object or `with` initializer is fine), and the `init` accessor of an indexer or an
explicit interface property always does. A getter, setter, `init`
accessor, or indexer accessor with no matching accessor on the compiled type is reported
per-accessor as `Skipped` (a `get` added to a write-only property, a `set` added to a
getter-only property or indexer, a `set` that was `init` at the last compile or the
reverse, or one whose property changed its type, index parameters, or `static` modifier
since then), so an edited accessor never disappears from the response silently; with a
verified baseline, accessors unchanged from it produce no row. A
property *added* in this edit is different: its accessors apply when the property has a
supported shape (see "Added members" above).

Subscribing to or unsubscribing from a field-like event (`+=`/`-=`) inside an edited
body works, and so does raising or reading one (`E?.Invoke(x)`, `E(x)`,
`if (E != null)`, `E = null`): the shim reads the event's backing field through a
Harmony accessor, which puts that method on the delegation path. Four shapes have no
backing field to reach and stay `Skipped` (see the table below): an event with custom
`add`/`remove` accessors, an `abstract`/`extern`/interface event, an event whose
delegate type is not visible outside the assembly, and an event added in this edit
that the added-field store cannot hold (see below).
Raising through a conditional receiver (`other?.E?.Invoke(x)`) and `nameof(E)` also
stay `Skipped`.

A field-like event added in this edit to a compiled, non-generic class that is visible
outside the assembly keeps its delegate in the added-field store, like an added field, so edited bodies in any file of the same reload
can subscribe, unsubscribe, raise, read, and assign it (instance or static). It is listed in
`AddedFields`, shares the added-field lifetime, and compiled code that is not patched
cannot see it. These stay `Skipped`: a struct host (the struct-field reason), a generic
host or one nested in a generic type, a delegate type not visible outside the assembly, custom `add`/`remove` accessors, a name the
compiled class already uses for another member, `E ??= h`, `nameof(E)`, `a?.E += h`, passing it by `ref`, an initializer an added
field could not have, and `Get().E += h` (the receiver would be evaluated twice).
A handler that is a method group of an added method or of a compiled private method is
`Skipped` too; subscribe a lambda that calls it instead (`E += x => OnValue(x);`). When a
compiled method already holds `E += OnValue;`, keep that line and add the lambda
subscription beside it: the method applies, only the private accesses inside the lambda are
rewritten, and a compiled `-= OnValue` still removes the kept handler. Do not wrap the kept
line itself in a lambda, since the `-=` would stop removing it.
Subscriptions live on the store's delegate, not on Unity objects: `+=` is not atomic, so
subscribe and raise on the main thread only. A handler subscribed by an earlier reload
keeps running the body it was subscribed with until it is removed and subscribed again
(for a subscription made in `OnEnable`, toggle `enabled` as described under "Added Unity
messages"). Changing the event's delegate type in a later reload drops its subscribers unless the
new type can still read the old list (a variance-compatible change), and that reload names
it in `Warnings`. The store keeps subscriptions until `--revert-all`, a compile, or a domain
reload: an instance event's subscriptions go with the object, while a static event's outlive
Play Mode when Domain Reload is off, as a compiled static event's do. Deleting the event from
the source removes it from `AddedFields`, but its subscribers stay in the store until
`--revert-all`, so adding it back delivers to them again.

A method that calls a base method (`base.Method(args)`) in its own statements is
patched: the call runs the base implementation exactly as compiled code would,
including a base method declared in another assembly or on a generic base class. The
other uses of `base` listed in the table below are `Skipped`.

A `Skipped` row never undoes what an earlier reload applied to the same method: that
patch keeps running, so the method matches neither the compiled assembly nor the
source on disk. When a run skips a method it had patched before, `Warnings` names it.

## Skipped — reported per method and in `Warnings`, never flips `Success`

| Condition | Why |
|-----------|-----|
| Method on a `partial` type when another part of the type changed since the last compile and was not passed, or when a file that names the type has syntax errors (passed or not) | Hot reload binds against the compiled type; pass that file with `--files` too, or run `uloop compile`. For a file with syntax errors, fix it and run hot reload again |
| Method on a `partial` type when the other parts could not be checked against the last compile (no source snapshot yet, or more than 50 changed files in the assembly) | Run `uloop compile` |
| Method on a `partial` type whose body names a member no source file of the assembly declares | A part generated at compile time (a source generator's output) is not visible to hot reload; run `uloop compile` |
| Method or getter whose body uses an `internal` member of a type the reload was not given, by its bare name, inside a lambda, local function, query, iterator or async method, in a body where a lambda, local function or query works with a value hot reload could not resolve (such as the member's result), or in a body patched through a delegating shim | Hot reload reaches such a member only in the method's own statements, written with its receiver (`this.Name`, `Type.Name`, `value.Name`); qualify a bare name, or run `uloop compile` |
| Method or getter on a `partial` type whose body passes an `internal` method of such a type as a delegate, uses an `internal` event of it, or names an `internal` member of it in an object initializer or a property pattern | On a `partial` type hot reload patches such a member only as a field, a property or a method call written with its receiver; run `uloop compile` |
| Method or getter on a `partial` type whose body uses an `internal` member of a type in another assembly (through `InternalsVisibleTo`) | Reported as a name no source file of the `partial` type declares; hot reload does not patch this use from a `partial` type yet. Run `uloop compile` |
| Method on a struct (value type) | Value-type patching is out of scope |
| Generic method, or method on a generic type | Harmony cannot safely patch open generics |
| Explicit interface method or property accessor the compiled assembly does not hold yet, or an accessor of an explicit interface indexer | Only the bodies of explicit interface methods and property getters and setters the compiled type already implements are patched. Run `uloop compile` |
| No body (`abstract` / `extern`) | Nothing to transplant |
| Body uses `base.` for something other than a direct call to a base method (a base property, indexer, event, or method group) | C# cannot express that outside the type; run `uloop compile` |
| Base method call in a lambda, local function, query, async or iterator body, a property accessor, a member or type hot reload added, or a body patched through a delegating shim | A base call is applied only in a compiled method's own statements; move the call there, or run `uloop compile` |
| Base call to a generic or ref-returning method, to one with a `params` parameter or a pointer type in its signature, or with named or omitted optional arguments | For named or omitted optional arguments, write every argument positionally; otherwise run `uloop compile` |
| Base call to a method marked `[Conditional]` | The compiler removes the call, and the evaluation of its arguments, when the symbol is not defined; hot reload cannot do the same. Run `uloop compile` |
| Base call to a method whose base type declares another overload with the same parameter types once its type arguments are filled in (for example `Pick(T)` and `Pick(int)` on `Base<int>`) | Hot reload finds the base method by name and parameter types, which cannot tell these overloads apart. Run `uloop compile` |
| Base call to a method the compiled assembly does not hold yet (added or re-signatured since the last compile) | Run `uloop compile` |
| Private/internal access inside an async/iterator/closure body has no accessor-delegate shape | Conditional access (`?.`), indexers, initializer member assignments, and calls with `ref`/`out`/`in`, named, omitted optional, or expanded `params` arguments (or to generic, by-ref-returning, or `[Conditional]` methods, methods with a ref struct parameter or return type, or an extension method whose class is not public) cannot be rewritten to accessor delegates. Neither can ref-returning properties, nor these writes to a property through a private/internal setter: `??=`, a compound write whose receiver could be evaluated twice, an assignment whose value is used (a lambda or `=>` member that returns nothing and is only the assignment applies), and a deconstruction. A compound write (`+=`, `??=`, and the like) to a property whose getter is private/internal and whose setter is not is refused as well. The same writes to a private/internal field declared on a class apply, static fields included. A private/internal instance member declared on a struct is refused. An inaccessible method the compiler calls for `await`, `foreach`, a deconstruction or positional pattern, a collection initializer, a query expression, a ref struct's `using`, or `await using` is refused as well. One case is not caught yet: a call that passes a ref struct (for example a `ReadOnlySpan<T>`) to a private/internal method is reported `Patched` and throws `MethodAccessException` when it runs, so run `uloop compile` for that edit. These limits apply to compiled members; a method added in the same reload is reached directly, whatever its arguments, except a generic, by-ref-returning, or `[Conditional]` added method, which is refused as a compiled one is, and an added extension method called on a receiver, which is refused: call it in its static form, `Extensions.Method(receiver, ...)`, as the reason shows. A compiled private/internal static property can be read, assigned, and compound-assigned |
| An async/iterator/closure body of a type not visible outside its assembly reaches a compiled member | An accessor delegate has to name its host, which a shim cannot do for an `internal` class or a class nested in one, so this refuses even a `public` member of that class. A body reaching only methods added in the same reload applies. In a lambda or local function, copy the compiled member into a local outside it; otherwise run `uloop compile` |
| An async/iterator/closure body references a private/internal type | Accessor delegates rescue member access, not type references; the body still cannot JIT-compile from the shim assembly |
| A declared return or parameter type cannot be resolved (a new type this reload could not introduce, a missing using, or a typo) | Skipped; a supported new type declared in an edited file of the same assembly is introduced by this reload, so check `Warnings` for the refusal reason (`introduced-types.md`); otherwise add the type or the `using`, or fix the typo, then run `uloop compile` |
| An added member's body cannot be fully bound in the hot-reload compilation | Hot reload cannot verify a member it cannot bind. A common cause: another file of the same reload, passed or pulled back in because it holds active patches, declares a compiled type from source, while a compiled API the body calls still names the compiled copy (for example, a lambda handed to a compiled `Register(Action<T>)`). The reason then names both types and the file declaring the compiled API. When the called member belongs to a type an earlier reload introduced and its signature was bound to the compiled copy, the reason names the introduced type and that compiled type instead. Either way the `Skipped` row names the step for that run (pass the file declaring the compiled API, leave the file declaring the type out, undo its edit and leave it out, or `uloop compile`), chosen from whether each file was passed, carried in, or already holds patches; a row about an added property's body points to the row of its accessor instead. A file passed this way is brought back by every later reload of the assembly while it stays unchanged, including the reload that re-applies after `--revert-all` or Play entry, until the next successful compile. When the skip deactivated added members an earlier reload applied, the next reload of the assembly retries their unchanged file once, so passing only the declaring file applies them again |
| Getter, setter, `init`, or indexer accessor with no matching accessor on the compiled type, or the `init` accessor of an indexer or an explicit interface property | Accessor patching covers getters, setters, property `init` accessors, and indexer getters and setters; `uloop compile` applies these edits. Accessors of a property added in this edit are emitted instead when the property has a supported shape |
| `init` accessor whose body assigns a readonly field, or an init-only property outside an object or `with` initializer | Only the type's own constructors and `init` accessors may assign those, and the patch body is compiled outside the type. Run `uloop compile` |
| Constructor (instance or static), finalizer, operator, conversion operator, or explicit event accessor (add/remove) | Skipped; `uloop compile` applies these edits |
| Method raises or reads a field-like event that has no reachable backing field | Custom `add`/`remove` accessors, an `abstract`/`extern`/interface event, a delegate type that is not visible outside the assembly, or an added event the added-field store cannot hold leave nothing for the shim's Harmony accessor to bind |
| Method raises or reads a field-like event through a conditional receiver (`other?.E`) | The shim has no name for the conditional receiver to pass to the accessor call |
| Method names a field-like event inside `nameof` | The shim is a different type and cannot keep the bare event name |
| Method subscribes (`+=`/`-=`) to an event added in this edit that the added-field store cannot hold | The shim binds the subscription against the compiled assembly, which has no such event yet; an added field-like event of a class is subscribed through the store instead |

## Failed — flips `Success` to `false`

| Condition | Notes |
|-----------|-------|
| File does not belong to any compiled assembly | Per-file entry with `Method` = `(file)`; only `Assets/` and `Packages/` sources resolve |
| Resolved assembly name is missing from CompilationPipeline | Per-file entry with `Method` = `(file)`; Unity may have mapped a not-yet-imported `.asmdef` onto a predefined assembly. Run `uloop compile` first |
| Script is not in the last compiled assembly's source list and its assembly membership cannot be confirmed | Per-file entry with `Method` = `(file)`; a new file passed with `--files` is hot-reloadable when its membership in an existing, unchanged compiled assembly is confirmed (`.asmdef` / `.asmref` boundaries are checked when present; a predefined assembly with none also passes), but fails when the Editor is not ready or an `.asmdef` / `.asmref` on its path was added, deleted, or changed since the last import — run `uloop compile` first |
| The Editor is a Multiplayer Play Mode Virtual Player and the main Editor's project has not compiled the assembly | Per-file entry with `Method` = `(file)`; a Virtual Player reads the main Editor's `Library/ScriptAssemblies`, and the reason says to compile the main Editor's project first. Until then the edit reaches that player only through a compile: the CLI's compile fallback brings it in when `--compile-on-skip` lets the compile run, and `auto` holds it while that player is in Play Mode (`CompileFallback` is `HeldForPlayMode`). A patch applied to the main Editor does not reach Virtual Players (each is a separate Editor process) |
| The Editor is compiling or importing when the request arrives, or starts to before the reload is applied | Per-file entry with `Method` = `(file)`; nothing in the source needs a change. The response sets `RetryAfterEditorReady`, and the CLI waits for the Editor to settle (up to 10 minutes) and applies the same request again once; if Unity's own compile already took the edit in, the second apply reports `NothingToApply`. A second refusal falls through to `--compile-on-skip`. |
| Another uloop command still holds the Editor when the request arrives (for example a `uloop compile` sent a moment earlier) | The request is not run; the CLI does not retry against the busy Editor and does not bring it to the front. It waits for that command to finish and the Editor to be ready (up to 10 minutes), then sends the same request once. `EditorReadyRetryNote` names the command it waited for and `Timing.EditorReadyWaitMs` is the wait. A busy answer to that one request is reported as `UNITY_SERVER_BUSY` without another wait. While a cancelled `execute-dynamic-code` request still holds the Editor, the wait sends the request again every 5 seconds, because the Editor takes such a request's slot back only when another request arrives. When the command that ran was a compile that already took the edit in, give `--files`: with no files the Editor selects changed files anew, finds none, and fails validation instead of reporting `NothingToApply` |
| Loaded assembly differs from the one on disk (pending compile) | Run `uloop compile` first, then retry |
| Source file fails to parse | Per-file `Failed` entry with `Method` = `(file)` carrying the parse errors; nothing from that file is applied, its earlier patches stay active, and `Success` is false |
| Method signature not found in the loaded assembly | Usually a stale assembly; run `uloop compile`. In-file renames and signature changes are classified as added members before reaching this point |
| Shim compile error (e.g. the body calls a member that does not exist yet) | The error is attributed to the file it came from: that file reports `Failed` with its own compiler errors (plus the `uloop compile` hint when they indicate a missing member) and the rest of the file is `Skipped`, while the other files of the assembly are recompiled without it and applied. Bodies elsewhere that call an added method this reload left out — its shim failed to compile, or another method of its file did — are `Skipped` with a reason naming that method; its own row says which. When errors cannot be attributed to a file, every file of that assembly reports one `(shim-compile)` entry; if only one method was edited, the failure is attributed to that method's name instead |
| Patch rejected or crashed at apply time (e.g. `[BurstCompile]`, a patch-engine emit failure) | The entry carries the rejection reason or the underlying engine error |
| Accessor binding failed for a shim type | The source references a member the compiled assembly does not have yet; the first method of that shim type reports the binder error and the rest of its file is `Skipped` — run `uloop compile` and retry |
| The signature-change gate could not finish the run safely — the retry that skips a gated change failed, or shim-compile isolation dropped an edited caller that had covered a change | Every file of that assembly reports `Method` = `(signature-change-gate)` carrying the specific cause; nothing from those files is applied, because the reload has no retry budget left to split them — fix the failing edit or run `uloop compile`. Files of other assemblies in the same command are unaffected |

A reload applies each file all-or-nothing: when any method in a file fails to compile or validate, nothing from that file is applied and patches from earlier reloads stay active. The other files of the same assembly are still applied, except a body that calls an added method the reload left out, whether its own shim failed to compile or another method of its file did — that body is `Skipped` until the method applies. The one exception to all-or-nothing is a Harmony patch-engine failure in the middle of applying a validated file; that run reports itself as partially applied and recommends 'uloop hot-reload --revert-all'. A `Failed` row in `IntroducedTypes` widens the unit from the file to the assembly: type preparation runs once per assembly before any of its method bodies is transformed, so every file sharing that assembly is left unapplied, while files in other assemblies still apply.
