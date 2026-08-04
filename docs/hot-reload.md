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
edited .cs file
  │ (1) resolve owning assembly, defines, and references via CompilationPipeline
  ▼
transform worker (external process: Unity-bundled Roslyn on the Unity-bundled .NET host)
  │ (2) parse + semantic analysis; convert each eligible method body into a static shim
  │     method source (bare member references qualified via `instance.` / `global::Type.`),
  │     plus a manifest and per-method skip reasons
  ▼
shim compilation (existing external csc infrastructure, RoslynCompilerBackend)
  │ (3) compile the shim source against publicized reference assemblies
  │     (Cecil visibility rewrite, same assembly name) → dll + pdb bytes
  ▼
Assembly.Load(bytes, pdb)      (4) load the shim assembly into the Editor domain
  ▼
Harmony transpiler transplant  (5) patch the original method with a transpiler that discards
                                   the original instructions and emits the shim method's IL,
                                   so the body runs inside Harmony's skip-visibility
                                   DynamicMethod replacement
```

Harmony ID: `io.github.hatayama.uloop.hot-reload` (distinct from the pause point's ID).
Caches: `Library/UloopHotReload/PublicizedRefs/<assemblyName>-<mvid>.dll` and
`Library/UloopHotReload/Worker/<sourceHash>/`.

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
  and its static shim `(instance, args…)` occupy identical slots.
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
  (`this` → `instance`, bare member references qualified); no per-access-kind accessor
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

- Adding fields, types, or methods; changing signatures; changing field initializers or
  `const` values — all require `uloop compile`. Shim compile errors caused by references to
  newly added members are reported with that hint, and changed `const` values (including
  enum members) are compared against the compiled target assembly and reported as a
  response warning; other outside-body edits stay silent.
- In-flight async methods and coroutines keep running the old code until re-entered.
- Callers whose call sites were JIT-inlined may not observe the detour (`IsLikelyJitInlined`
  heuristic produces a warning, as with pause points).
- A pause point armed before a hot reload of the same method stops firing. The apply
  response lists the affected marker ids, `pause-point-status` reports
  `SuppressedByHotReload: true`, and enabling a new marker on a currently patched
  method is rejected with `PAUSE_POINT_PATCHED_BY_HOT_RELOAD`. Reverting the patch
  restores armed markers and clears the flag.

## Open Questions Tracked for Implementation

- Transplant of methods with exception handlers (`try/finally`, `using`) rides on Harmony's
  standard `CodeInstruction` block round-trip; PR-3's end-to-end tests must include such a
  body.
- Struct (value type) methods stay skipped in v1 (same restriction the prefix design had).
