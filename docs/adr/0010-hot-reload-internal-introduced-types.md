# Internal Introduced Types and Access to Target Assembly Internals

Date: 2026-09-26

Status: Accepted and implemented. The introduced-type preparation promotes internal and
modifier-less declarations, refuses file-local declarations and internal overrides of compiled
or retained members, keeps Unity ancestry refused behind an internal base, exposes the target's
internals to the artifact compile, and grants runtime access before publication.

## Decision

Hot reload will accept supported top-level declarations with `public`, `internal`, or no access
modifier. The other introduced-type restrictions remain: this decision does not add nested
declarations, generic type definitions, new UnityEngine.Object-derived types, or the other
declaration shapes currently refused by the planner.

The implementation has three cooperating mechanisms:

1. **Promote the generated declaration.** Rewrite the top-level access modifier to `public`
   only in the source emitted for the introduced-type artifact. Keep the user's source,
   declaration fingerprint, source hash, and original assembly identity unchanged. The
   artifact's metadata remains usable by subsequent hot-reload compilation and by callers.
   When a later reload keeps a retained declaration in the worker to patch its bodies or add
   members, the worker binds that declaration with the same promotion, so code of other types
   that names it is judged against the public type the domain runs.
2. **Expose internals in compile-time references.** Compile the artifact against copies of its
   target assembly and that target's active introduced-type artifacts with the assembly part
   of accessibility removed. Other referenced assemblies stay unchanged. These copies are
   never loaded as runtime replacements for the original assemblies.
3. **Grant runtime access before publication.** On a supported Mono runtime, set
   `skip_visibility` and `NoInlining` on every method declared by the newly loaded artifact,
   including constructors, type initializers, and methods of compiler-generated nested
   types. Complete this step before returning a prepared artifact. Activation remains the
   responsibility of the existing commit stage.

The compile-time visibility mapping is:

| Original metadata visibility | Artifact reference copy |
|---|---|
| Top-level `NotPublic` | `Public` |
| `NestedAssembly`, `NestedFamORAssem` | `NestedPublic` |
| `NestedFamANDAssem` | `NestedFamily` |
| Field or method `Assembly`, `FamORAssem` | `Public` |
| Field or method `FamANDAssem` | `Family` |
| Public, private, protected, and other visibility forms | Unchanged |

Preserve all non-visibility metadata flags and leave `<Module>` untouched. Property and event
accessors are methods and follow the same mapping. A field-like event's private backing field
stays private without a name-based exception.

Copies use their own cache,
`Library/UloopHotReload/InternalsExposedRefs/fmt1/<assembly>-<mvid>.dll`. The existing fully
publicized shim references keep their separate cache and behavior. The worker's planning,
verification, and transform inputs continue to receive the original reference paths. Source
promotion and exposed-reference generation must not change shared worker input objects.

## Runtime Probe and Failure Contract

The grant is available only when the runtime probe succeeds. The initial implementation
targets 64-bit Mono and validates the observed method-record layout against reflection.
It checks method attributes, implementation flags, metadata token, a bounded UTF-8 name,
and the generic-definition and inflated-method bits adjacent to `skip_visibility`.
Probe methods must initially have `skip_visibility` clear.

Availability is fixed for the lifetime of the services instance. A domain reload creates
new services and repeats the probe. Probe failure performs no native writes.

A grant first enumerates and validates every method record of the artifact. Only after all
records pass does it write the two flags, preserving the other bits. An ordinary validation
refusal therefore leaves every record unchanged. Repeated grants are idempotent; the count
in a successful result means the number of granted method handles, not only changed handles.

The probe detects known forms of layout mismatch. It is not a proof that arbitrary future
native layouts are memory-safe, and it does not establish support for an untested runtime.
The implementation must not catch a memory access violation and pretend that execution can
safely continue. This mechanism applies only to newly generated artifacts owned by this
hot-reload run.

If the probe is unavailable, artifact compilation uses the original references. Declaration
promotion still applies: a plain internal type that needs no internal access can be introduced,
while a type that needs unavailable access receives the compiler diagnostics. An unavailable
probe does not disable unrelated hot-reload operations.

Reference exposure and the compiler request's need for a grant must agree. A request that
requires the grant while the grant is unavailable is a contract violation, rejected before
compilation or source-file writes.

If references were exposed and the loaded artifact's grant is refused, return a failed
introduced-type result with the refusal reason. Do not return a prepared artifact, activate it,
or retry it as a successful ungranted artifact. Preserve previously active types and callers.
The assembly already loaded into the AppDomain can remain there without publication; this
decision does not promise individual assembly unloading. Per-run unique assembly names
prevent it from replacing the identity of a later artifact.

The existing cancellation and commit gates remain responsible for source-hash changes,
target MVID changes, Editor state, and publication. A successful grant alone does not imply
that the run is allowed to commit.

Keep the existing exception boundary for filesystem, metadata, and assembly-loader failures.
Do not relabel a thrown loader exception as an ordinary grant refusal or retry it as raw success.
Reference files can disappear after worker preparation; an exposed-reference read then fails
before compilation and publication. A cache copy already produced for another reference can
remain on disk without representing a prepared or active artifact.

Reference-copy construction uses Unity APIs, including `Application.dataPath` and platform
information. The preparation stage must switch to the main thread before building those
references. Loading and granting remain consecutive synchronous operations after compilation,
without an intervening await or invocation of artifact code.

## Refused Shapes

File-local types remain unsupported. They have a file-scoped identity that cannot be
represented by promoting a reusable top-level declaration. When the Editor's compiler parses
the syntax, report `File-local introduced type requires a compile: <type>`. Older bundled
Roslyn versions can reject the syntax before planning; preserve that parse diagnostic.

Overrides declared `internal`, `protected internal`, or `private protected` are refused when
their base member belongs to a compiled or retained type. Exposing the base member changes
the accessibility that the artifact compiler sees, so emitting the source unchanged would
violate C# override rules. Report
`Internal override in an introduced type requires a compile: <type>.<member>`.
The rule includes method, property, indexer, and event accessors.

Such an override can remain supported when its base is a new declaration in the same
planning batch and both declarations are compiled into the same artifact. A retained type
does not become a new same-batch type merely because its owner source appears in the run.
Public and protected overrides retain their ordinary behavior.

Before enabling internal reference exposure, fix the planner's UnityEngine.Object ancestry
check to traverse an inaccessible internal base symbol when Roslyn supplies exactly one
named-type candidate. Otherwise reference exposure could remove the compile-time refusal
that currently prevents an unsupported Unity-derived artifact from being emitted.

Module initializers remain refused before loading. The existing attribute-name check stays;
add a regression case for an internal compiled attribute polyfill to preserve that gate.

## Context

Introduced types are emitted into separately loaded assemblies. Their original source
belongs conceptually to a target project assembly, but that is not their runtime assembly
identity. Keeping a declaration internal in the artifact prevents ordinary callers from
naming it, and references to the original target's internal members fail compilation.

Promoting only the declaration resolves the first boundary. It does not resolve the second.
Compile-time reference exposure resolves compilation, but the Editor's Mono JIT still
checks access when the artifact's ordinary methods execute.

The existing access spikes show that an assembly compiled against publicized references
can still throw access exceptions at runtime. They also record that the tested Mono runtime
does not honor `IgnoresAccessChecksTo` as a solution for those calls.

The S7 spike investigated the per-method Mono flag. It covers direct accesses, generated
async and closure methods, generic methods, internal base classes and interfaces, and
internal virtual and abstract dispatch through the base.

A later Release-mode probe exposed an additional requirement: an unflagged caller can inline
a flagged method, after which access is checked in the caller's context. Setting
`NoInlining` on the granted method prevents that transition, including when source code
requested aggressive inlining. Debug execution does not exercise this failure mode.

## Rejected Alternatives

- **Keep public-only introduced declarations.** This excludes ordinary declarations that omit
  an access modifier, and leaves their original-assembly internal dependencies inaccessible.
- **Only promote the artifact declaration.** This solves naming the new type but leaves both
  compile-time and JIT access checks for target internals.
- **Use fully publicized references, including private members, for artifact compilation.**
  That accepts additional source access that would not be valid in the original assembly.
  The selected mapping removes only the assembly restriction and preserves private and
  protected constraints.
- **Rely on `IgnoresAccessChecksTo`.** The existing spike does not establish it as effective on
  the tested Editor Mono runtime.
- **Add `InternalsVisibleTo` to the target assembly.** Changing the already loaded target's
  compiled friend-assembly metadata requires a compile and does not provide the intended
  no-compile introduction workflow.
- **Transplant all artifact method bodies through Harmony.** This broadens patching to generated
  methods, constructors, generic instantiations, and type initialization; Harmony may also need
  to JIT an original method before patching it. The grant directly addresses the tested access
  boundary while leaving the existing patch lifecycle in place.
- **Rewrite all internal accesses through accessors.** This would add another transformation
  mechanism for inheritance, interfaces, generics, and compiler-generated bodies, with a wider
  surface than the selected reference-plus-grant design.

## Consequences

- An internal or modifier-less introduced type is public in its generated artifact. Dynamic-code
  snippets can name it, and the public-type index can include it for automatic using resolution.
- Declaration fingerprints still represent the original source. Changing a retained declaration
  from internal to public is a declaration-header change, not an unchanged artifact.
- Source-level accessibility consistency diagnostics can differ from a regular project compile
  because target internals are public in a compile-time copy. This is a hot-reload capability,
  not a replacement for validating the project with a normal compile.
- Friend-assembly relationships to other target assemblies are not expanded by this decision.
- Artifact methods do not inline, even when their source requested aggressive inlining.
  This has a runtime cost and also keeps later method replacement observable by callers.
- Body edits of, and members added to, a retained internal or modifier-less introduced type end
  like the same edits of a public introduced type, and the members other types add that name it
  stay applied. The initial introduction succeeding still does not promise every later body
  shape is patchable.
- Members that other types add cannot use a non-public member of a retained introduced type
  until a compile. Only the reloads that edit that type can bind such a member, so it is skipped
  on every reload instead of being applied on some and dropped on the next. A property counts by
  the accessor the use calls: the other reloads keep a property with its public accessors only,
  so reading one whose setter is internal stays applied while writing it is skipped.
- Internal reference copies incur a Cecil rewrite per MVID and are cached separately from
  fully publicized shim references. Copy files are compile-time artifacts, not active types.
- The runtime mechanism depends on Mono internals. A supported Editor for which the probe
  unexpectedly fails requires investigation; a fallback-only passing test is not evidence that
  internal access works there.
- No IPC wire incompatibility is introduced. Do not bump the protocol version for this feature.

## Verification Evidence and Acceptance Gates

The predecessor's execution records report the seven original S7 cases passing on
2022.3.62f3, 6000.3.15f1, 6000.5.8f1, and 6000.7.0b2. The later NoInlining probe reports
nine cases passing in 2022.3 Release. The additional generic-bitfield probe was run on
2022.3. The final spike contains ten test declarations; these observations do not mean all
ten passed on every Editor version or operating system.

Production evidence so far comes from scoped EditMode runs on 2022.3.62f3, macOS, with Debug
code optimization unless a point says otherwise:

- The grant tests pass. Under Debug the Release-only inlining tests are skipped. When the
  grant was added, before any production route used it, the grant tests including that inlining
  test and the ten S7 tests also passed with Release code optimization (30 of 30, 2022.3.62f3,
  macOS). After the grant was connected to the production route, the same 30 tests passed again
  with Release code optimization on 2022.3.62f3 and 6000.7.0b2 (macOS).
- The production preparation-to-caller route passes for internal static members, internal
  interfaces, lambda closures, async methods, iterators, generic methods with reference and
  value type arguments, internal members of a retained artifact, a later body edit, and an
  unchanged internal constant. Each case asserts the value the patched caller returns.
- A private member stays unreachable. The compiler reports it as missing (CS0117) rather than
  inaccessible (CS0122), because it does not import the private members of a referenced
  assembly.
- Unavailable-probe fallback and post-load grant refusal are exercised separately. Both keep the
  earlier caller value and the active registry contents; refusal also keeps a type an earlier
  reload activated.
- The worker inputs and the context of the shim compilation carry no exposed reference path, and
  the preparation leaves its worker input unchanged.
- References that cannot be exposed fail the declaration without compiling or granting, and the
  run keeps its already-active rows, notices, and drift warnings.
- Declaration promotion passes the planner and the preparation-to-caller route: internal and
  modifier-less classes, enums, interfaces, and structs; other modifiers keep their tokens and
  order; attributes, comments, documentation, and alias bindings survive with and without a
  namespace; LF and CRLF sources with non-ASCII names keep their text; only the modifier the
  defines leave active is rewritten. The fingerprint follows the written modifier, so an
  unchanged internal rerun is `AlreadyActive` and a change to `public` fails as a header change
  that keeps the earlier type. A top-level `private` or `protected` declaration is not repaired
  (CS1527). Internal declarations of the other refused shapes keep their own reasons. Without the
  grant a plain internal type is still introduced and one reading internals fails with CS0122.
  A body edit of an introduced internal type ends like the same edit of a public introduced
  type: `Patched` for a plain body and for a closure reading its own private field, `Skipped`
  for a closure that uses a compiled internal type. Methods and fields that compiled types add
  and that name a retained internal or modifier-less type stay applied across its body edits;
  before the worker bound the kept declaration as public, such an edit skipped them, which
  usability testing reported. Members added to the retained type apply as on a public introduced
  type, and a compiled type sharing its file keeps its added properties. A method a compiled type
  adds that calls an internal method of the retained type is skipped on the reload that
  introduces the type, on one that edits it, and on one that leaves its file out. So are added
  methods that call an internal accessor of its properties (a write, a parenthesized write, an
  increment, or a deconstruction through the setter; a read, a compound assignment, or a nested
  initializer through the getter; a write through an internal ref-returning property), while
  those calling only the public accessor, or naming a property with an internal getter in
  nameof, stay applied on all three reloads.
- The file-local refusal reason is not reached on 2022.3: its bundled compiler (Roslyn 4.3.1)
  rejects `file` while parsing (CS0116), and the test asserts that path instead.

The EditMode workflow on the integration branch passed on every leg with no failed or
inconclusive tests: the full suite on 2022.3.62f3 and the hot reload suites on 6000.3.15f1,
6000.5.8f1, and 6000.7.0b2 (Linux, Debug). The skipped tests were the Release-only inlining
tests, the path case that needs a platform with two directory separators, and
environment-dependent tests outside hot reload on 2022.3. The worker parses with the latest
language version of the Editor's bundled compiler, so on 6000.5.8f1 and 6000.7.0b2, whose
compilers parse `file`, the file-local test takes the refusal-reason path. The hot reload
suites also passed on 6000.7.0b2 on macOS under Debug.

Not verified yet: a Release run of the production-route tests, Release runs on 6000.3 and
6000.5, and Windows. The Release passes above ran on macOS only; no workflow leg runs the
Release-only tests with Release code optimization.

Before merging the production feature:

- Run the grant tests and the full production preparation-to-caller route, including generic
  and generated methods, retained artifacts, later body edits, and failed publication.
- Exercise unavailable-probe fallback and post-load grant refusal separately. Assert old caller
  values and active registry contents, not only result messages.
- Verify that private access remains rejected and that the exposed references never replace
  worker or shim reference inputs.
- Verify original-source fingerprints, unchanged reruns, header changes, source/MVID changes
  during a run, and the existing commit gates.
- Run Release-only inlining tests and distinguish them from Debug runs, where they are skipped.
- Verify UTF-8 method names, platform path handling, and source rewriting with LF and CRLF.
  Record Windows and Editor-version coverage explicitly.

Use the repository's scoped local tests and the existing EditMode workflow for the integration
gate. Do not repeatedly open the same project with different Editor versions to obtain coverage.

## Reversal Condition

Reopen this decision if the Editor moves to CoreCLR, a supported Mono Editor fails the probe or
the execution checks, NoInlining does not preserve the intended access context, or field
experience shows that the internal-override refusals prevent routine use.

Also revisit it if an officially supported runtime mechanism can provide the same capability
without depending on native method layout. A new runtime requires a new implementation and
evidence; success of the raw-reference fallback is not proof that the grant is portable.
