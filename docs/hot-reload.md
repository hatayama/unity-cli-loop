# Hot Reload: Design Notes and Spike Findings

Status: feasibility spike completed (PR-1). This document records the design, what the spike
proved and refuted on the Editor Mono runtime, and the mechanism decision derived from it.
The spike tests live in `Assets/Tests/Editor/HotReload/` and stay in the repository as
executable pins of the runtime behavior this design depends on.

## Goal

Reload edited method bodies into a running Unity Editor (including Play Mode) without domain
reload, without requiring any attribute on user code, driven by `uloop hot-reload --files
<edited .cs files>`. The input is the real source file, so a later `uloop compile` (real
compilation + domain reload) naturally converges to the same behavior.

Editor-only. Players (Mono or IL2CPP) are out of scope.

## Pipeline Overview

```text
edited .cs files
  │ (1) resolve each file's owning assembly, defines, and references via
  │     CompilationPipeline; load a PDB-checksum-verified source snapshot when one
  │     exists for the file
  ▼
group by assembly              (1b) files that compile into the same assembly form one
  │                                 group, processed sequentially; steps (2)-(4) run once
  │                                 per group, step (5) applies file by file
  ▼
transform worker (external process: Unity-bundled Roslyn on the Unity-bundled .NET host)
  │ (2) parse + semantic analysis of every source in the group; when a verified snapshot
  │     is available, diff method declarations against it and emit shims only for edited
  │     methods (unchanged methods are counted, not listed); convert each eligible edited
  │     method body into a static shim method source (bare member references qualified via
  │     `__uloopInstance.` / `global::Type.`), plus a manifest and per-method skip reasons
  ▼
shim compilation (existing external csc infrastructure, RoslynCompilerBackend)
  │ (3) compile the group's shim source into one assembly against publicized reference
  │     assemblies (Cecil visibility rewrite, same assembly name) → dll + pdb bytes;
  │     members added in one file of the group are therefore visible to the bodies
  │     edited in its siblings
  ▼
Assembly.Load(bytes, pdb)      (4) load the shim assembly into the Editor domain
  ▼
Harmony transpiler transplant  (5) patch the original method with a transpiler that discards
                                   the original instructions and emits the shim method's IL,
                                   so the body runs inside Harmony's skip-visibility
                                   DynamicMethod replacement
```

Skip reasons and introduced-type diagnostics leave the worker as a reason `code` plus its
`args` (and an optional free-form detail), never as a sentence. Only the Editor's
`HotReloadWorkerReasonText` turns those into the English a caller reads, so a wording change
touches one renderer instead of both processes. `TransformWorkerDtoSyncTests` compares the
worker's payload types against the Editor's data transfer objects field by field, so a field
added on one side alone fails a test rather than silently serializing to nothing.

Harmony ID: `io.github.hatayama.uloop.hot-reload` (distinct from the pause point's ID).
Caches: `Library/UloopHotReload/PublicizedRefs/fmt2/<assemblyName>-<mvid>.dll`,
`Library/UloopHotReload/Worker/<sourceHash>/`, and
`Library/UloopHotReload/SourceSnapshot/<assemblyName>-<mvid>/`.
When a verified snapshot marks a currently patched method as unchanged, the
orchestrator reverts that patch to the compiled IL instead of re-emitting a shim.

## Spike Findings

### S1 — access mechanics on the Editor Mono runtime (pivotal)

Test file: `HotReloadSpikeS1PublicizedAccessTests.cs` (Unity 2022.3.62f3).

What the spike **refuted** (each pinned by a test so a Unity upgrade that changes the
behavior surfaces immediately):

1. **This Mono enforces IL accessibility at JIT time.** A snippet compiled against a
   publicized reference copy compiles and loads fine, but invoking it throws
   `FieldAccessException` when the method is JIT-compiled. The original design assumption
   ("Mono does not re-check accessibility for loaded IL") is false on this runtime.
2. **`IgnoresAccessChecksToAttribute` is not honored.** Embedding the attribute (declared
   locally, listing the target assembly) changes nothing; the same exception is thrown.
3. **A private-poking method cannot even be Harmony-patched.** Harmony must JIT-compile the
   patch target to detour it, so `Harmony.Patch` on such a method throws
   `FieldAccessException` at patch time. Consequence: shim IL containing inaccessible
   references must never be JIT-compiled at all.
4. The async variant fails identically: the private accesses live in the compiler-generated
   `MoveNext` body, which fails JIT accessibility checks on first execution.

What the spike **proved**:

- **Publicized reference copies work for compile time.** A Cecil visibility rewrite
  (types/fields/methods to public, `<Module>` untouched, assembly name preserved) lets the
  external csc compile snippets that read/write private fields, call private methods, and
  use internal types, with a reference set of only mscorlib + the publicized copy.
- **Transplant mechanism (chosen).** Patching a normal method of the target assembly with a
  Harmony transpiler that discards the original instructions and returns
  `PatchProcessor.GetOriginalInstructions(shimMethod, generator)` executes the shim's body
  inside Harmony's skip-visibility DynamicMethod. Private field write, private method call,
  and internal type access all succeed. The shim method itself is never JIT-compiled; its IL
  is only read as data. Argument slots line up because an instance method `(this, args…)`
  and its static shim `(__uloopInstance, args…)` occupy identical slots.
- **Accessor mechanism (proven; committed as the v2 stage).** Rewriting private accesses to
  Harmony accessor delegate fields keeps the shim IL JIT-legal; this works even inside async
  state machine bodies. Verified for private field access (`AccessTools.FieldRefAccess`
  delegates) and for private method calls (open-instance delegates built by
  `AccessTools.MethodDelegate`).
- **Delegation transpiler (proven; the v2 patch shape).** Patching an original async method
  with a transpiler that replaces its body with "load arguments, call the accessor-rewritten
  shim, return" routes calls to the shim, whose state machine JIT-compiles legally. Verified
  end-to-end on an async original.

### S2 — transform worker bootstrap

Test file: `HotReloadSpikeS2WorkerBootstrapTests.cs`.

- The Unity-bundled csc (`csc.dll` on the bundled .NET host) compiles a standalone worker
  executable against the bundled shared framework (`-nostdlib+ -target:exe`, references =
  every managed assembly in `NetCoreRuntimeSharedDirectoryPath` + the two bundled Roslyn
  assemblies; on Windows that directory also ships native PE images, which are filtered out
  because csc rejects them with CS0009).
- The worker resolves `Microsoft.CodeAnalysis*` at runtime from the compiler directory via
  an `AssemblyLoadContext.Default.Resolving` hook registered in `Main` before any Roslyn
  type is touched (Roslyn usage lives in a separate method so `Main`'s JIT does not trigger
  the load early).
- Copying `csc.runtimeconfig.json` verbatim as the worker's runtimeconfig pins the same
  bundled runtime the compiler itself runs on.
- All paths come from the existing `ExternalCompilerPathResolver`; no new resolution logic
  was needed.

### S3 — Harmony detour behavior

Test file: `HotReloadSpikeS3PrefixDelegationTests.cs`.

Skipping prefixes fully replace instance void, instance-with-return, static, async, and
iterator methods; a delegate captured before patching still hits the detour (the detour
rewrites the method entry in place); private methods resolve and patch via `AccessTools`;
`UnpatchAll` restores original behavior. These tests document the detour semantics the
transplant mechanism inherits (it uses the same patching machinery with a transpiler instead
of a prefix).

### S4 — patching a method inside a retained artifact assembly

Test file: `HotReloadSpikeS4ArtifactPatchTests.cs`.

What the spike **proved** (an artifact assembly is built the way production builds one:
external Roslyn to a dll and a pdb, then the two-argument `Assembly.Load(dllBytes, pdbBytes)`
the production loader uses, described by a `RetainedArtifact` type home):

- `MethodMatcher_ResolvesMethodInsideRetainedArtifactHome` — the existing matcher resolves a
  public method of such an assembly through its Cecil metadata token and the Mvid guard, the
  resolved method belongs to the byte-loaded assembly, and the patcher's preflight check
  accepts it.
- `Transpiler_ReplacesBodyOfRetainedArtifactMethod` — a Harmony transpiler replaces the body.
- `UnpatchAll_RestoresRetainedArtifactMethod` — unpatching restores the original body.
- `MethodMatcher_ResolvesPrivateMethodInsideRetainedArtifactHome` — a private method takes the
  same route, and its caller observes the replaced body.

**Nothing refuted.** Carrying a pdb into the load does not affect any of the above.

### S5 — structured fingerprint on a body-only edit

Test file: `HotReloadSpikeS5RetainedFingerprintTests.cs`.

What the spike **proved**:

- `Fingerprint_BodyOnlyEdit_ComparesAsBodyOnlyAndNamesTheMember` — editing only a method body
  of an introduced type, planned twice from the same target generation, compares as body-only
  and names exactly that member key.
- `Fingerprint_DeclarationEdit_ComparesAsDeclarationChanged` — changing that member's
  declaration compares as a declaration change instead.
- `Plan_BodyOnlyEditAgainstRecordedDeclaration_StillRequiresACompile` — planning the body-only
  edit against the recorded declaration still emits the "changed introduced type requires a
  compile" diagnostic. This pins today's outcome, which is what a later change has to move.
- The other half of the premise — that a type's fingerprint stays stable while a type it
  depends on moves from source into an artifact — is already pinned by
  `PrepareIntroducedTypes_DependencyMovedToArtifact_KeepsFingerprint`,
  `PrepareIntroducedTypes_IndirectDependencyMovedToArtifact_KeepsFingerprint` and
  `Transform_DeclarationReadingRetainedType_StillMatchesItsRecord` in
  `TransformWorkerIntroducedTypeBindingTests` (14 tests, all passing).

**Nothing refuted.** One caveat for anyone writing more of these: a fingerprint's header
carries the target assembly Mvid, so all fingerprints under comparison must come from the same
fixture generation — rebuilding the fixture makes every comparison a declaration change.

**Open items the spike found.** The spike changed no production code; Phase 1 closed the first
three of the gaps it named:

1. **Resolved in Phase 1.** A worker row now names the assembly its method lives in, and one
   resolver turns that name into a home, so a method of a retained declaration reaches the
   artifact instead of a project assembly of the same name.
2. **Resolved in Phase 1.** The shim compile references a publicized copy of each introduced-type
   assembly, which is what lets a shim compiled for an edited body read that type's private
   members; engine and system assemblies are still never rewritten.
3. **Resolved in Phase 1.** The worker classifies a body-only edit of an already-introduced type
   and emits its methods against the retained declaration, instead of skipping every method of a
   type with no compiled counterpart.
4. Nothing records the artifact assembly's own Mvid:
   `IntroducedType/HotReloadIntroducedTypeArtifact.cs` holds only the assembly, the dll and
   pdb paths and the descriptors, and a descriptor's Mvid
   (`IntroducedType/HotReloadIntroducedTypeDescriptor.cs:12`) is the original assembly's.

### S6 — a MonoBehaviour type introduced from a byte-loaded assembly

Test file: `HotReloadSpikeS6IntroducedMonoBehaviourTests.cs`. The spike changed no production
code. Hot reload refuses an introduced type deriving from `UnityEngine.Object` with "Unity
object introduced type requires a compile", and this spike measures what the Editor would
actually do if that refusal were lifted — nothing here says it should be.

The snippet is compiled by the external compiler and loaded with the two-argument
`Assembly.Load(dllBytes, pdbBytes)`, exactly the shape every introduced type has. It declares
two MonoBehaviours that differ only in `[ExecuteAlways]`, so a message count measured on one
cannot be explained away by edit mode alone.

What the spike **proved** (edit mode, pinned by tests):

- `Q1_AddComponent_AcceptsByteLoadedMonoBehaviour` — `AddComponent(type)` returns a live
  component of that exact type and logs nothing. Having no script asset is not what
  `AddComponent` refuses.
- `Q1_CreateInstance_AcceptsByteLoadedScriptableObject` — `ScriptableObject.CreateInstance`
  accepts such a type too, equally silently.
- `Q2_ExecuteAlwaysIntroducedType_ReceivesLifecycleMessagesInEditMode` — Unity itself delivers
  `Awake` and `OnEnable` on `AddComponent`, `OnDisable`/`OnEnable` when the `enabled` flag is
  driven, and `OnDisable` then `OnDestroy` on `DestroyImmediate`. Native message dispatch
  reaches a type with no script asset behind it.
- `Q2_PlainIntroducedType_ReceivesNoLifecycleMessagesInEditMode` — the sibling without the
  attribute receives none of them, which is the ordinary edit-mode rule rather than anything
  about the assembly.
- `Q3_IntroducedComponent_IsFoundByComponentLookups` — `GetComponent(type)` and
  `GetComponents<MonoBehaviour>()` both find it.
- `Q3_StartCoroutine_RunsUntilTheFirstYieldInEditMode` — `StartCoroutine` accepts a coroutine
  declared on the type and runs it to the first yield; edit mode does not drive it further.
- `Q4_Instantiate_ClonesTheComponentWithItsFieldValues` — `Object.Instantiate` clones the
  component and carries both the `[SerializeField]` private field and the public field across.
  Unity's serializer handles the type's fields despite the missing script asset.
- `Q4_JsonUtility_SerializesTheIntroducedComponentFields` — `JsonUtility.ToJson` writes both
  fields.
- `Q5_TwoGenerationsOfTheSameTypeName_CoexistAndStayDistinct` — two artifact assemblies
  declaring the same fully qualified type name yield two distinct `Type` objects; both
  components sit on one object at the same time, and `GetComponent` returns each generation's
  own instance. Nothing merges or replaces the older one by name.

What the spike **refuted**:

- The premise that a component with no script asset cannot exist is wrong for every operation
  above. The refusal is not enforced by the Unity runtime at `AddComponent` time.
- `Q3_MonoScript_ExistsWithoutAnAssetBehindIt` — the expectation that
  `MonoScript.FromMonoBehaviour` returns null is refuted: it returns a `MonoScript` whose
  `GetClass()` is the introduced type. What is missing is the asset behind it — empty `name`,
  empty asset path, empty `text`. This is the one measured difference from a compiled
  MonoBehaviour, whose script path resolves to its source file.

**Manual measurement** (play mode, not covered by any test, measured once through
`execute-dynamic-code` while the Editor was in play mode; the objects were created at runtime
and the scene was never saved):

- The same artifact loaded in play mode and added to a new object: `Awake` and `OnEnable`
  arrived synchronously on `AddComponent`, then `Start` once and `Update` on every frame
  (13,739 `Update` calls across 13,739 frames). Both behaviours were delivered to, the
  `[ExecuteAlways]` one and the plain one alike — in play mode the attribute is irrelevant.
  So Unity's native per-frame dispatch works for a byte-loaded MonoBehaviour.
- A `Reflection.Emit` MonoBehaviour subclass — the shape the existing Unity-message proxy
  builder produces — was measured the same way in the same session and behaved identically:
  `Awake` on add, then `Start` once and `Update` every frame. **No difference between the two
  routes was observed**, so the proxy mechanism has no delivery advantage over loading the
  type from bytes.
- After leaving play mode the probe objects were gone (`find-game-objects` found none) and the
  active scene was not dirty. That only reflects the ordinary discarding of play-mode scene
  state; it is not evidence about what a *saved* scene would do.

The fallback the plan held in reserve — forwarding messages through the existing proxy builder
instead — **is not needed**: native delivery works, and the proxy route was measured to behave
identically rather than better.

**Production work items if this becomes a feature.** None of this is implemented; the list is
what the measurements say would have to be built.

1. Decide what happens to instances at the next real compile. After a compile the artifact
   assembly is gone and the type is a normal compiled type; nothing today transfers a live
   component from the artifact type to the compiled one.
2. Give the introduced type a script asset, or accept that it has none. Everything that
   resolves a component through `m_Script` — the Inspector, prefab and scene serialization,
   `AddComponent` from the UI — goes through the asset that the measurements show is absent.
3. Handle the two-generation case explicitly. Q5 shows a second generation coexists rather
   than replacing the first, so re-introducing an edited declaration would leave the old
   component alive on the object unless something removes it.
4. Decide the refusal's new boundary: `MonoBehaviour` and `ScriptableObject` behave the same
   in these measurements, but nothing here covers types Unity instantiates from an asset
   (`Editor`, `EditorWindow`, `ScriptedImporter`).

**Expected limitations.** Marked as inference where they were not measured:

- *Inference.* A scene or prefab saved while such a component is attached would write no script
  reference, because the `MonoScript` has no asset and therefore no guid; reopening it would
  show a missing script. Not measured — measuring it means saving a scene, which this spike
  did not do.
- *Inference.* The Inspector cannot draw the component the way it draws a compiled one, for the
  same missing-asset reason.
- *Inference.* An assembly loaded from bytes lives only in the current domain, so a domain
  reload leaves any component of an introduced type without its type. This spike did not
  measure a domain reload.
- Not covered: play-mode behaviour is a single manual measurement, not a regression test. If
  this becomes a feature it needs a play-mode test of its own.

## Mechanism Decision

**Transplant-primary.** Stage (5) applies a Harmony transpiler per patched method that
replaces the original instructions with the shim method's instructions.

The transpiler reads the shim body without the patch `ILGenerator`, so both `LocalBuilder`
operands and branch `Label`s arrive owned by a throwaway generator; the patcher re-declares
the locals and re-defines the labels on the real generator and rebinds every reference
(including `switch` target arrays and instruction label marks) before emitting. A foreign
label otherwise NREs at emit, or silently branches to the wrong target when indices happen
to collide.

Why transplant over the accessor rewrite:

- The worker-side transform stays exactly the qualification rewrite stage (2) already needs
  (`this` → `__uloopInstance`, bare member references qualified); no per-access-kind accessor
  machinery, whose corner cases (compound assignments, ref arguments, internal types in
  locals and signatures, events, object creation of internal types) would each become a skip
  or a bug.
- Inside the skip-visibility DynamicMethod, every access kind works uniformly — the spike's
  pinned tests show the alternative mechanisms fail wholesale, not per-kind.
- No prefix wrapper generation is needed at all, and Harmony binding conventions
  (`__instance`, `ref __result`) drop out of the generated code entirely.

Boundaries the mechanism imposes (enforced as per-method skips, detected by the worker's
semantic model):

- **Async/iterator bodies that touch inaccessible members are skipped in v1.** The transplant
  replaces the visible method (the async/iterator stub); the shim's compiler-generated
  `MoveNext` body still JIT-compiles normally and would throw (pinned by the S1 async test).
  Async/iterator methods whose bodies only touch accessible members are supported.
- **Lambdas and local functions that touch inaccessible members are skipped in v1** for the
  same reason: their bodies compile into closure methods of the shim assembly, which
  JIT-compile normally when the delegate is invoked.

### v2: accessor delegation for async, iterator, and closure bodies

Status: implemented. The worker rewrite path (PR-6) and the orchestrator bind + delegation
patch path (PR-7) are wired; EditMode e2e covers async, iterator, lambda-capture, and private
property round-trips, plus the internal-type skip.

v2 lifts the two v1 boundaries above by rewriting the inaccessible accesses instead of
transplanting the IL. For a method that v1 would skip only because its async/iterator/closure
body touches inaccessible members, the worker rewrites each such access into a call through a
generated accessor delegate field (`AccessTools.FieldRef` for fields; open-instance delegates
for methods and property accessors). The rewritten shim is JIT-legal, so the original method
is patched with the **delegation** transpiler: discard the original body and emit
"load every argument slot → `Call` the shim → `Ret`". The shim itself JIT-compiles normally;
its accessor delegates reach members the shim assembly boundary would otherwise forbid.

Wire details:

- Manifest `patchKind` is `"delegation"` when the worker applied accessor rewrite, otherwise
  `"transplant"` (or absent — treated as transplant).
- After `Assembly.Load` of the shim assembly and **before** any Harmony patch is applied, the
  orchestrator reflects over each shim type and invokes `__BindAccessors()` once when present
  (parameterless `public static`). Types with no accessor delegates simply have no binder.
- Bind failure (for example the source names a member the compiled assembly does not have yet)
  fails **only that shim type's delegation entries** with a remediation hint to run
  `uloop compile`. Transplant entries that share the same shim type are not taken down —
  they never read accessor delegates.
- Scope constraint: the rewrite applies only when the containing type and every type
  appearing in an accessor signature (instance type, field type, parameter and return types)
  is accessible to a foreign assembly. Inaccessible member *names* are fine; inaccessible
  *types as types* (locals, casts, `new`, signatures) are not rewritable and keep the skip.
- Still skipped in v2: bodies using internal types as types (above), private accesses with no
  accessor-delegate shape (conditional access, indexers, static field writes, and the other
  condition-b cases in the skill), and event add/remove on inaccessible events.

## Convergence and Lifecycle

- Input is the real source file; a later real compile converges to identical behavior. No
  separate "diff file" workflow exists.
- The patch ledger and loaded shim assemblies are static state; both are cleared by domain
  reload by design (no persistence, no auto-reapply). Shim assemblies cannot be unloaded and
  accumulate until the next domain reload; that is accepted.
- Mvid guard before patching: if the on-disk `Library/ScriptAssemblies/<asm>.dll` Mvid
  differs from the loaded module's `ModuleVersionId`, the assembly has already been rebuilt
  and reloaded — hot reload is refused with a pointer to `uloop compile`.
- Re-reloading the same method unpatches the previous transpiler before applying the new one.

## Known Limits (documented, not worked around)

- New top-level `public` classes, structs, enums, and interfaces of the same assembly are
  introduced by the reload that declares them; every other new-type shape, and any use of an
  introduced type from another assembly or through Unity, still requires `uloop compile`
  (`docs/hot-reload-introduced-types.md`). Added members referenced from another assembly or
  from a file that is neither passed to this reload nor already hot-reloaded, and changed
  field initializers or `const` values — all require `uloop compile`.
  Added fields, methods, and properties themselves apply, and are visible to the bodies
  edited in any file of the same assembly passed to the same reload — including on a type an
  earlier reload introduced, where they are applied on the artifact that already carries it. An
  added auto-property is backed by the added-field store, so its value shares that lifetime.
  Added events, indexers, and the property shapes listed in the skill's scope reference
  (set-only, virtual, explicit interface, `init`, struct host) still require `uloop compile`. Unchanged files of that assembly that
  already hold active patches are re-applied so they bind to the newest shim. Shim compile errors caused by references to
  members that are still missing are reported with that hint, and changed `const` values
  (including enum members) are compared against the compiled target assembly and reported as
  a response warning; other outside-body edits stay silent.
- A Unity message added to an existing `MonoBehaviour` is delivered by a generated proxy
  component that hot reload attaches to each live instance while Play Mode runs, because
  Unity's own message discovery only sees the compiled class. `Start`, `Update`,
  `LateUpdate`, `FixedUpdate`, `OnGUI`, and the collision, trigger, mouse, and
  application messages are forwarded; `Awake`, `OnEnable`, `OnDisable`, `OnDestroy`, the
  editor-only messages, and any message declared with a return value or a `ref`/`out`
  parameter are not, and the run names those in one warning pointing at `uloop compile`.
  An added `Start` runs once on each instance that already exists, at the moment the proxy
  attaches. The proxies live only for the running session — nothing is attached outside
  Play Mode, and a compile or domain reload drops them — and execution order relative to
  the target's other components is not guaranteed.
- In-flight async methods and coroutines keep running the old code until re-entered.
- Callers whose call sites were JIT-inlined may not observe the detour (`IsLikelyJitInlined`
  heuristic produces a warning, as with pause points).
- Pause points on a hot-reload patched method follow the patch instead of being
  rejected: applying a patch re-targets armed markers onto the patched body
  (`RetargetedToHotReloadPatch: true`) and reverting re-targets them back onto the
  compiled body. The residual limit is a marker whose requested line no longer
  resolves in the code now executing — it is suppressed (`SuppressedByHotReload: true`,
  reason in `SuppressedByHotReloadReason`) rather than cleared, and stays silent until
  a later patch transition restores the line or `uloop compile` runs. Enabling a new
  marker on a patched method is rejected with `PAUSE_POINT_PATCHED_BY_HOT_RELOAD` only
  when the line cannot be mapped onto the patched body.
  When the compiled line range of the patched method is known, the failure message also reports it, so you can see how far the edited file's line numbers have shifted from the compiled source.

## Open Questions Tracked for Implementation

- Transplant of methods with exception handlers (`try/finally`, `using`) rides on Harmony's
  standard `CodeInstruction` block round-trip; PR-3's end-to-end tests must include such a
  body.
- Struct (value type) methods stay skipped (same restriction the prefix design had).
