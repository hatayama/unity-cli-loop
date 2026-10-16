# Offline compile check

`uloop compile-check` reports C# compile errors and warnings without launching the Unity Editor and
without contacting a running one. It is a dispatcher-owned command: it needs no IPC connection and
no project runner.

Shown since ADR 0018 (`docs/adr/0018-compile-check-is-its-own-command-and-is-shown.md`); what it
detects and what stays undetected is listed under Limitations.

It is not a replacement for `uloop compile`. Nothing it produces is loaded by Unity, so use it only
when you want to know whether the code compiles — not when you need the result to be live in the
Editor.

## How it works

Unity does not compile C# itself. It hands the work to Bee, which writes one Roslyn response file
per assembly under `Library/Bee/artifacts/<dag>/<Assembly>.rsp`, listing that assembly's sources,
references, scripting defines and analyzers. `compile-check` replays those response files.

1. **Pick the build to replay.** `Library/Bee/tundra.log.json` names the dag file the Editor last
   opened; that is the authoritative answer. When the log is unreadable and the project has exactly
   one dag directory, that one is used. When there are two (a debug and a release dag) the choice
   falls back to `m_ScriptDebugInfoEnabled` in `Library/EditorOnlyScriptingSettings.json`. If none of
   these can decide, the run stops with `COMPILE_CHECK_UNITY_BUILD_REQUIRED`. Beside each `.rsp` Bee
   writes a second response file, `.rsp2`, carrying a `/pathmap` that maps the project root to `.` —
   empty for the assemblies that compile sources from `Assets`. The check replays whatever it finds
   there, verbatim, so an assembly whose attributes bake source paths into metadata produces the same
   output here as it does in Unity's own build.
2. **Find the compiler.** The Editor version comes from `ProjectVersion.txt` (or `--editor-version`),
   and the Editor install is located the same way `uloop launch` locates it. The compiler is the one
   bundled with that install: `csc.dll` under the Editor's Roslyn directory, run through the
   `dotnet` host bundled beside it. Nothing is downloaded, and no system-wide .NET SDK is used. csc is
   started through the Roslyn compiler server the way Unity's Bee starts it, so several assemblies in
   a row reuse the server's JIT-compiled code and its cached reference metadata. The server process
   stays up for a while after the run, exactly as it does after a build Unity ran itself; leave it
   alone. When it cannot be reached, csc compiles in its own process and the result is the same.
3. **Decide what to compile.** By default the run compiles the assemblies whose sources changed
   since the last Unity build, plus every assembly that references one of them. An assembly pulled
   in only through a reference is dropped again once everything it references has been compiled and
   every one of those reference assemblies (`.ref.dll`, which holds the public surface alone) shows
   a dependent the same API as the last Unity build's: a change that does not reach the public
   surface — a method body, say — cannot change what a dependent compiles to. A reference assembly
   that came out byte-identical counts as the same, as it does for Unity. One whose bytes differ is
   handed to the changed-files check worker, which writes both out as text and counts them the same
   only when the texts match. The text leaves out what no dependent can read: the attribute types
   the compiler embeds, the top-level types the compiler owns outright, whose names start with `<`,
   together with everything nested in them (such as `<PrivateImplementationDetails>`, which holds
   array-initializer data and string-switch hashing), private nested types nothing visible refers to
   (closures and async or iterator state machines) along with the attributes naming them, and where
   the compiler happens to put nullable annotations — each annotation is written as the value that
   applies to its member. So a private reference-type field in a nullable-enabled assembly, a
   private generic method with an `unmanaged` constraint, or a private member added before a lambda
   no longer compiles every dependent again, which Unity, going by bytes, does. This is safe because
   a dependent cannot name an embedded type, a top-level type whose name starts with `<` or anything
   nested in it, or an unreferenced private nested type, and reads every annotation by the value
   that applies to it. One difference remains: a dependent that already fails because it names a
   private nested type may report the error under another ID (CS0246, CS0426 or CS0117 in place of
   CS0122, or the other way round) until it compiles again; whether it compiles does not change.
   When the worker cannot be built or cannot answer, the dependents are compiled.
   When the API did change, the worker also says which names the change touches: the simple name of
   the type at each level of nesting (with and without an `Attribute` suffix), a namespace segment
   only when one side lacks that namespace, the member's name (a property or event rather than its
   accessor, the member alone for an explicit implementation), and `new(` for a constructor that a
   target-typed `new` can reach. A dependent that writes none of those names - in its own sources
   or in the generator output its last full compile saved, comments and strings included - is
   dropped the same way, and counts as keeping its surface for the assemblies below it. Whether
   a reference touches a dependent never decides which reference assembly the dependent reads when
   it compiles for another reason. A change a dependent can feel without writing any name is
   unnamed and compiles every dependent: an assembly-level row; for a type both sides have, its
   flags or attributes, base type, interfaces, type parameters, an instance field of a value type,
   a parameterless constructor or an indexer; an abstract member added; an operator; a
   compiler-generated member; a member C# binds by pattern (`GetEnumerator`, `GetAwaiter`, `Add`,
   `Deconstruct`, `Dispose`, `SetResult`, the LINQ query methods and the like); every member of an
   interpolated string handler or collection builder type; every member of a type in `System`,
   `Microsoft.CodeAnalysis` or a namespace under them, which the compiler looks up by name for
   `init`, `^1` and the like; members that only moved; and a change naming more than 2000 names. A dependent whose sources cannot be read compiles too.
   The saved generator output is read before any assembly compiles; the sources are read from the
   moment the run first compiles an assembly or has the worker check its changed files, or first
   needs the names for a verdict, whichever comes first; a run that only replays earlier results and
   has no verdict that needs names never reads them.
   Its answer is kept in `<Assembly>.surface.json`, keyed by the content of both reference
   assemblies and the worker build, so the runs until Unity builds again do not compare the same
   pair twice. The comparison is always against Unity's own artifact, never against the
   previous `compile-check` output, which would only say that nothing moved since that run and would
   hide an error a dependent has been carrying all along. An assembly whose compiler step failed in
   the last Unity build is selected whatever its timestamps say: Unity runs Bee with deferred dag
   verification, so a build can run the stale dag's compiler step, succeed, refresh the assembly's
   artifact, and only then rebuild the dag and fail on the sources that actually changed. The
   artifact is then newer than every source even though it was not produced from them. An assembly
   the last build left no artifact for at all is selected the same way, without looking at any
   timestamp: Unity 6's Bee deletes the assembly and its reference assembly when their compiler step
   fails, and an assembly an upstream failure kept from ever being compiled never had one. Bee
   writes the response file before the compiler runs, so what this check replays survives either
   case; only a dag holding no assembly at all stops the run. `--all`
   compiles every assembly in the build and skips nothing. Assemblies this step leaves out are
   counted in `SkippedAssemblies`; an assembly the run later stops short of because one it
   references has errors is named in `BlockedAssemblies` instead, and counted nowhere. An
   assembly's sources are re-globbed from its own directory and from every folder an `.asmref`
   attaches to it, wherever that folder sits — including inside a nested assembly's directory,
   which the glob of its own directory never reaches — each glob stopping at nested assembly
   boundaries, so a `.cs` file created in any of them since the last build is compiled. Assemblies
   under `Library/PackageCache` are the exception: the package manager restores them and nothing
   edits them in place, so their recorded sources are reused as-is and neither their directory nor
   their `.asmref` folders are re-globbed. A source
   the last build recorded for this assembly that no glob produces any more stops the run unless an
   `.asmref` still attaches its folder to this assembly: the walk up from the file to the first
   folder holding an `.asmref` or an `.asmdef` says which assembly owns it today. An `.asmref` whose
   reference resolves to no `.asmdef` in the project attaches nothing, so its folder is treated as an
   ordinary part of the assembly around it, which is what Unity compiles it as. A package shipping a
   sample for a render pipeline the project does not install is the ordinary case. An `.asmref`
   naming an `.asmdef` the build left out by platform or package settings still owns its folder:
   Unity attaches the folder to that assembly and then drops it, so the folder's sources go into no
   assembly. Scripts under `Assets` that no `.asmdef` or `.asmref` owns are walked once per run and
   given to the four predefined assemblies by Unity's own path rules: a folder named `Editor`
   anywhere sends a script to an editor assembly, and `Assets/Plugins`, `Assets/Standard Assets`,
   `Assets/Pro Standard Assets` and `Assets/iPhone Standard Assets` send it to a first-pass one, so
   a new script there is compiled with the assembly Unity would put it in. Scripts in
   `Assets/StreamingAssets`, `Assets/WebPlayerTemplates`, `Assets/WebGLTemplates` and
   `Assets/Plugins/Android` are left out, as Unity imports them without compiling them. Membership is compared without looking at any timestamp, because
   moving a file carries its modification time along, and either an `.asmdef` or an `.asmref` can
   hand a folder's sources to a different assembly without editing a single `.cs` file. The
   recorded sources keep the order of the response file, and the ones added since the last Unity
   build follow them in ordinal order: csc emits types in the order of its sources, so any other
   order gives an assembly nobody edited a reference assembly in other bytes than Unity's, and
   every assembly that references it compiles again.
4. **Check that the response files still describe the project.** An assembly definition that was
   added, removed, or that stopped building for the Editor, a reference or a precompiled
   reference that was added to one, and a project reference it lost (a reference in the response
   file that the `.asmdef` no longer declares and Unity does not add on its own: `UnityEngine.UI`,
   `UnityEditor.UI`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`), invalidate the recorded
   build. The run then stops with `COMPILE_CHECK_UNITY_BUILD_REQUIRED` rather than reporting
   diagnostics for a configuration the project no longer has.

   The build settings the response files cannot be read back into - scripting defines,
   `csc.rsp`, `defineConstraints` and `versionDefines`, and the package manifest and lock file -
   are recorded by the first run after a Unity build in
   `Library/uloop/compile-check/<dag>/build-settings.json`, keyed to the build-trace file Bee
   rewrites on every build. A later run against the same build refuses any of them that moved; a
   run after another Unity build replaces the record instead. A scripting define of the active
   build target group that the response files lack is refused whenever `ProjectSettings.asset` was
   saved after the last build, or there is no build-trace file.
5. **Compile in dependency order** and parse the compiler's diagnostics into JSON. An assembly
   starts once every assembly it references that this run also compiles has finished, so
   independent assemblies compile at the same time. An assembly selected for its own sources, or a
   dependent that is already known to need compiling, starts sooner: as soon as every assembly it
   references that was selected for its own sources has finished. The dependents it references
   that are still compiling are assumed to keep Unity's public surface, so it reads Unity's
   reference assembly for them. When one of them turns out to have changed its surface, the
   assembly is compiled again after all of them finished; when one of them fails, the assembly is
   blocked. An assembly is compiled at most twice per run. An assembly whose recorded result may
   still apply once those dependents finish waits for them instead, so a run over the same inputs
   still replays it. `assumed_kept` and `compiled_again_after` in the VibeLog's `units` say which
   happened. A unit whose record applied after the wait is counted under `reused` and does not
   appear in `units`; `waited_for_record` marks a unit that waited for a record that then did not
   apply. `--jobs <n>` caps how many run at once; the default is half the machine's CPUs and never
   less than one, because one `csc` process already keeps two to three cores busy on its own.
   `--jobs 1` compiles the assemblies one after another.
   Among the assemblies ready at the same time, one whose references have all finished starts
   before one started early, then the one with the most sources, and a tie goes to the one first in
   dependency order.
   The reported order does not depend on the job count: results are collected in dependency order
   whatever finishes first. An assembly that fails to compile stops the run at that point of the
   tree: it wrote no reference assembly, so everything below it has nothing to compile against and
   could only report the metadata it cannot find. Those assemblies are named in
   `BlockedAssemblies`, and Unity's own build stops in the same place. Fixing the errors and running
   again carries the check on past them.
6. **Reuse the previous run's answer where nothing moved.** Before starting the compiler for an
   assembly, the run digests the closure of inputs that decide what the compiler would say about it:
   the compiler and its host, the response files Bee wrote for it, its sources, its references, its
   analyzers, and the files those analyzers read. If that digest matches the one the previous
   `compile-check` recorded next to its outputs in `<Assembly>.result.json`, and the outputs of that
   compile are still on disk exactly as it wrote them, the run reports the recorded diagnostics and
   outcome without starting `csc` at all. Nothing is assumed to have succeeded: an assembly that
   reported errors reports them again until one of its inputs moves. Because the outputs stay in
   place, the assemblies below it resolve the same reference assembly and are reused in turn, which
   is what makes editing a single method body cost one compile rather than one per dependent.
   A reference to an assembly this run rebuilt is read from Unity's artifact when the rebuilt
   reference assembly shows dependents the same API (step 3), and from this check's own output
   otherwise. The
   first is digested by timestamp, like every reference the run did not rebuild, so rebuilding an
   assembly without moving its public surface leaves the digests of the assemblies that reference it
   where the runs that did not rebuild it left them. The second is digested by content, so a
   reference assembly rewritten with identical bytes still counts as unmoved. A response file naming
   a file the digest does not follow — `keyfile`, `resource`, `link`, `addmodule` — gets no digest,
   and such an assembly always compiles. `--all` looks nothing up, though it still records what it
   compiled, under the same rule: a reference whose rebuilt reference assembly keeps Unity's API stays
   on Unity's artifact, so an ordinary run after it reuses what it compiled. Only a reference whose
   rebuilt surface differs from Unity's resolves to this check's output under `--all`; an ordinary
   run that leaves that assembly out reads Unity's artifact instead, and may compile the assemblies
   that reference it once more before it starts reusing them. This is a separate mechanism from the dependent skip in step 3: that one compares this
   run's reference assembly against Unity's artifact to decide whether a *dependent* needs compiling
   at all, while this one compares an assembly's own inputs against what the previous `compile-check`
   read.

Files and directories Unity ignores — any name starting with a dot or ending with a tilde, which
covers the `._*` AppleDouble siblings macOS writes on non-native volumes — are skipped everywhere
the project tree is walked, so their binary content never reaches the index or the compiler.

Assembly definitions are indexed from `Assets`, `Packages` and `Library/PackageCache`, plus every
directory a `Packages/manifest.json` dependency points at with a `file:` path — a package a project
develops locally lives outside the project root and would otherwise look like one whose assembly
definitions were deleted.

## Checking only the changed files

When an assembly would otherwise be compiled in full, compile-check first tries to check only its
changed files against the last successful full compile of that assembly. The decision and the
guarantees are recorded in `docs/adr/0015-compile-check-checks-only-changed-files-after-a-full-compile.md`.

The check is used when all of these hold:

- The run is not `--all` or `--full`, the assembly has an input key and at least 300 sources, and
  it was picked because its own sources changed, not only because an assembly it references
  changed. Smaller assemblies are always compiled in full and are not listed as fallbacks.
- The last full compile of the assembly by compile-check succeeded and left a baseline, and its
  outputs, result, and saved generator output are still on disk as it wrote them.
- Every input other than the source contents is the same as at that compile: the compiler, the
  response files, the references, the analyzers and the files they read, the output names, and the
  list of source paths.
- 1 to 30 of its sources changed.
- Every changed file keeps its declarations: only method bodies, accessor bodies, expression
  bodies, non-constant initializers outside partial types, comments, or whitespace changed. Adding
  or removing a `using` directive counts as a declaration change, because it can change what every
  name in the file resolves to.

The changed files are checked by a worker built once per Editor toolchain with the Editor's own
dotnet and Roslyn. It reports their compiler and analyzer diagnostics, with `DiagnosticSuppressor`s
and the response file's options applied, and the diagnostics of the files that did not change are
carried over from the last full compile. The outputs, the result, and the baseline of that compile
are left as they were.

A second run over the same changed files - the same inputs, the same baseline, and the same worker
build - does not call the worker again: it reports what the previous check reported, and
`FastChecks[].Reused` says so.

Otherwise the assembly is compiled in full as before, and `FastPathFallbacks` says why:

- `no baseline: no full compile of this assembly is on record` (none ran yet, or an input changed
  while the last full compile ran, so it left no record; `FastPathBaselineNotes` of that run says
  which)
- `no baseline: the last full compile had errors; the first full compile without errors records one`
  (a full compile that fails drops the baseline an earlier one left, and the assembly is
  compiled in full until a compile succeeds)
- `no baseline: the last full compile did not record one` (`FastPathBaselineNotes` of that run
  says why, or the assembly was compiled only because a reference changed)
- `no baseline: it was recorded by another version of compile-check`
- `baseline compile is no longer on disk`
- `inputs other than source contents could not be read`
- `inputs other than source contents changed: <kinds>`, naming which of `compiler`,
  `response file`, `source list`, `references`, `analyzers`, `analyzer inputs`, and `output name`
  changed
- `generator output of the baseline is gone`
- `baseline record does not match its compile result`
- `too many changed files (<count>, limit 30)`
- `worker unavailable: ...` (the worker could not be built; a build that failed is not retried
  until the worker sources or the toolchain change)
- `baseline was fingerprinted by another worker build`
- `worker failed: ...`
- `DeclarationChanged: ...`
- `Unsupported: ...` (for example an analyzer that threw, or a response file option the worker does
  not support)

A full compile that succeeds records a new baseline, `--full` runs included, so the run after it can
use the check again. A source or another input edited while csc was compiling leaves no baseline
and no record of that compile to reuse, so the next run compiles the assembly again;
`FastPathBaselineNotes` says why a successful full compile left no baseline. An input edited while the
changed files were being checked leaves no record of that check, so the next run checks them again.

So the first body edit is compiled in full more often than it looks:

- Right after a Unity build, compile-check finds nothing to compile and records nothing. The first
  body edit after that is compiled in full and records the baseline; the edits after it are checked.
- A run with `--all` records baselines too. It rebuilds the assemblies the next ordinary run takes
  from Unity's build, but a reference whose rebuilt reference assembly keeps Unity's API is read
  from Unity's build in both runs. A reference whose rebuilt API differs is read from this check's
  output under `--all`; while the next ordinary run rebuilds it too, the reference reads the same
  and the first body edit is checked as usual. Only when that run leaves the reference out, so it
  reads Unity's build instead, does the first body edit of an assembly referencing it fall back
  with `inputs other than source contents changed: references`.
- An assembly a run leaves out because nothing it compiles from changed keeps its result and
  baseline, so its next body edit is checked against them. An assembly a run picks up only because
  a reference changed records no baseline when it is compiled, so its next body edit is compiled in
  full. When it is left out instead, because every such reference kept its public surface or
  changed only names the assembly never writes, it keeps its result, baseline and saved generator
  output, so its next body edit is checked against them as before. A run that blocks an assembly because one it references has errors keeps
  its result and baseline: the next run checks them against the inputs and output hashes they were
  recorded with before using them, so a reference that comes back the same leaves the assembly
  reused or checked on its changed files, and one that comes back changed compiles it.
- A full compile while the assembly still has errors, whether a `--full` run or a run that fell
  back to one, drops its baseline, so the run after the fix is compiled in full. Running `--full`
  once the errors are fixed keeps it.

What the check does not guarantee, so run `--full` before finishing:

- CS0067, CS0169, CS0414, and CS0649, which the compiler reports only after it has seen the whole
  assembly, and the rules of analyzers that use Compilation, CompilationEnd, SemanticModel, or
  SymbolStart actions. `FastChecks[].UncheckedCodes` lists the IDs it could not run, leaving out
  the ones the response file already turns off (with `-nowarn` or a ruleset) and analyzer rules
  that are disabled, so an assembly may list fewer than these four. In a project that treats
  warnings as errors (`-warnaserror`), these are errors that only `--full` reports.
- Errors the compiler reports only while emitting, such as CS8178 and CS0656.
- Source generators. The check reads the generator output saved by the last full compile and does
  not run them again; `FastChecks[].GeneratorsNotRerun` says so.

The check keeps its state next to the compile outputs: `<Assembly>.fastcheck.json` (the baseline),
`<Assembly>.generated/` (the saved generator output, whose files the baseline records by content
hash), `<Assembly>.fastresult.json` (the result of the last changed-files check, reported again
while nothing it read has changed), and the worker build in `Library/uloop/compile-check/fastcheck-worker/<key>/`. Deleting or
changing any of them only costs a full compile.

## One run per project at a time

Two runs against the same project would write the same output directory and the same compiler
outputs, and could each report what the other half-wrote. A run therefore holds an exclusive lock
on `Library/uloop/compile-check.lock` from the moment it has found the Bee build until it returns.
A second run on the same project waits for it, up to five minutes, and then stops with
`COMPILE_CHECK_PROJECT_BUSY`. Runs on different projects do not wait for each other. The lock is
the operating system's own (`flock` on macOS and Linux, `LockFileEx` on Windows), so a run that
crashes or is killed releases it with its process and leaves nothing stale behind. The lock file
sits outside `Library/uloop/compile-check/` so that deleting the disposable output directory never
removes a lock another run is holding.

## Output

The compiled DLLs land in `Library/uloop/compile-check/<dag>/` and are never handed to Unity. The
directory is disposable; deleting it only costs the next run some time.

`--jobs <n>` bounds how many assemblies compile at the same time; the default is half the machine's
CPUs and never less than one, and `--jobs 1` compiles them one after another.

The command prints a JSON payload with `Success`, `ErrorCount`, `WarningCount`, `Errors`,
`Warnings` (each diagnostic carrying `Message`, `Code`, `File`, `Line`, `Column`, `Assembly`),
`CompiledAssemblies`, `ReusedAssemblies`, `BlockedAssemblies`, `FastCheckedAssemblies`,
`FastChecks`, `FastPathFallbacks`, `FastPathBaselineNotes`, `SkippedAssemblies`,
`ResponseFileSet`, `ProjectRoot` and
a one-line `Message`. `CompiledAssemblies` names the assemblies this run handed to the compiler and
`ReusedAssemblies` the ones it reported on without compiling; the diagnostics of both are in
`Errors` and `Warnings` alike. `BlockedAssemblies` names the assemblies the run stopped short of
because one they reference has errors — they are not counted in `SkippedAssemblies`, which counts
the assemblies the check read and found nothing left to say about: the ones nothing changed for,
and the ones left out because every assembly they reference kept the same public surface.
`FastCheckedAssemblies` names the assemblies checked on their changed files only, which are in
neither `CompiledAssemblies` nor `ReusedAssemblies`; `FastChecks` gives each one's `ChangedFiles`,
`UncheckedCodes`, `GeneratorsNotRerun`, and `Reused` (set when the run reported the previous run's
check of the same changed files instead of checking them again), and `FastPathFallbacks` gives the `Assembly` and
`Reason` of each assembly the check was tried on and compiled in full. `FastPathBaselineNotes`
gives the `Assembly` and `Reason` of each successful full compile that left no baseline. The
process exits 1 when
`ErrorCount` is greater than zero. A failure of the command itself also exits 1 but writes nothing to
stdout: stderr gets `{"Success": false, "Error": {...}}`, whose `ErrorCode` names the failure.

With the `ULOOP_DEBUG` environment variable set, each run also writes two entries to the CLI
VibeLog of the project, `cli_compile_check_start` and `cli_compile_check_complete`, with which
assemblies it compiled, reused, checked on their changed files, or blocked, why each fallback
happened, and how long each assembly took. `docs/vibe-logs.md` lists their fields.

## Limitations

- **It replays the last Unity build, and only some staleness is detectable.** Five `.asmdef`
  settings are compared against the response file, because they reach `csc` and can be read without
  evaluating defines or package versions: whether the assembly still builds for the Editor, a
  project reference it gained, a project reference it lost (a reference in the response file that
  the `.asmdef` no longer declares and Unity does not add on its own: `UnityEngine.UI`,
  `UnityEditor.UI`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`), `allowUnsafeCode` turning on, and a precompiled reference it gained
  under `overrideReferences`. A new or deleted `.asmdef` is detected too, as is every way
  assembly membership moves without any `.cs` file changing: an `.asmdef` that no longer sits over
  any source its response file recorded (it was moved into another assembly's folder), an `.asmref`
  whose folder holds sources the assembly it names does not record (it was added or moved), and a
  recorded source no `.asmref` attaches to its assembly any more (one was removed or retargeted).
  So do the build settings described below. Those cases — and only those — stop the run with
  `COMPILE_CHECK_UNITY_BUILD_REQUIRED` and ask for `uloop compile`.
  Every other edit listed below is invisible to the check: the run proceeds and silently reuses
  what the last build recorded.
- **Build settings are compared against a record keyed to the last Unity build.** The scripting
  defines of the active build target group (from `ProjectSettings/ProjectSettings.asset`), every
  `csc.rsp` Unity may read (`Assets/csc.rsp` and the one beside each `.asmdef` outside the package
  cache), the `defineConstraints` and `versionDefines` of each such `.asmdef`,
  `Packages/packages-lock.json` and `Packages/manifest.json` are recorded by the first run after a
  Unity build, keyed to the build-trace file Bee rewrites on every build
  (`Library/Bee/tundra.log.json`, `TundraBuildState.state` or `fullprofile.json`, whichever is
  newest). A later run against the same build refuses any of them that moved. A scripting define
  that the response files lack is refused, record or not, whenever `ProjectSettings.asset` was
  saved after the last build or there is no build-trace file. What stays invisible:
  - on a Unity version whose `Library/Bee` has none of the build-trace files, only an added
    scripting define is seen - not a removed one, and not the other recorded settings;
  - a change made before the record exists (before the first run after that build), other than an
    added scripting define;
  - the scripting defines of a project whose active build target is Dedicated Server (the
    response files carry `UNITY_SERVER`), because which settings group Unity reads for it cannot be
    determined, and those of a `ProjectSettings.asset` saved in binary;
  - an analyzer config added since the last build that Unity passes with `/analyzerconfig:`; one a
    response file already names is read by content and invalidates a reused result;
  - a Unity 6 build profile that overrides Player Settings or carries its own scripting defines:
    only the global ProjectSettings defines are compared, and a global define the build lacks is
    refused only when `ProjectSettings.asset` was saved after the last build.
- **A new script outside every `.asmdef` is given to one of the four predefined assemblies by
  Unity's path rules.** When that assembly is one the last build did not produce - the first
  editor script of a project that had none - the run stops with `COMPILE_CHECK_UNITY_BUILD_REQUIRED`.
  Scripts in the folders Unity never compiles (`Assets/StreamingAssets`, `Assets/WebPlayerTemplates`,
  `Assets/WebGLTemplates`, `Assets/Plugins/Android`) are left out.
  A new script in a package folder that has no `.asmdef` is not seen, because where Unity puts
  such a script differs between Unity versions.
- **A dependent compile-check has never compiled is compiled, not narrowed by names, the first time
  a reference changes.** The names are read from the dependent's sources and from the generator
  output its last finished compile saved; a unit with analyzers and no saved output, whether it was
  picked up as a dependent or started on its own sources ahead of the reference, compiles on any
  named change, and the VibeLog reports it with `changed_by.reason` `no saved generator output` and
  counts it in `name_index.without_generated`. After that its saved generator output is read, and
  leaving it out keeps that output. What stays invisible is a generator that generates from a
  referenced assembly's contents rather than from the dependent's own code. Neither is a change to
  the type of an async method builder seen, which a dependent never names; a change to its members
  is unnamed and compiles every dependent.
- **Every change to a type in `System`, `Microsoft.CodeAnalysis` or a namespace under them compiles
  every dependent.** The compiler looks these types up by name, so only polyfills normally live
  there; a project that puts its own extension classes in `System.*` gets no narrowing for them.
- **Linux (x86_64) is supported.** The Editor is looked up under `~/Unity/Hub/Editor/<version>/Editor/Unity`.

## Troubleshooting

**`COMPILE_CHECK_UNITY_BUILD_REQUIRED`: no Bee build artifacts found**
The project has never been built by this Editor, or `Library` was deleted. Raised only when the dag
holds no assembly at all: a build that failed and took some assemblies down with it is replayed
rather than refused, and the assemblies it left no artifact for are the ones this run compiles.

compile-check cannot create the response files itself: which sources belong to which assembly, the
scripting defines, and the package references are all decided by the Editor, and part of that
decision lives in native code. So the first build has to be Unity's. It does not need a window: a
headless run imports the project, writes the response files and exits.

```bash
# macOS
"/Applications/Unity/Hub/Editor/<version>/Unity.app/Contents/MacOS/Unity" -batchmode -nographics -quit -projectPath "<PROJECT_ROOT>"
# Windows
"C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "<PROJECT_ROOT>"
```

`uloop launch` is the same first build with the Editor window open; use whichever fits. The run
takes as long as the project's initial import (about a minute for a 100-assembly project, longer
for a large one). Once `Library` exists, compile-check runs without the Editor; it refuses again only
when it detects an `.asmdef` change or a build setting that moved (see Limitations for what stays
unseen). The same applies on CI: restore `Library` from a cache or
run this headless build once per job before calling compile-check.

**`COMPILE_CHECK_UNITY_BUILD_REQUIRED`: an assembly definition no longer matches the last build**
Raised when an `.asmdef` was added, deleted or moved, when an `.asmref` was added, moved, removed
or retargeted so that a folder's sources now belong to a different assembly than the one the last
build compiled them into, or when one of the five compared settings changed (no longer builds for
the Editor, gained a project reference, lost a project reference, turned on `allowUnsafeCode`,
gained a precompiled reference under `overrideReferences`). A lost reference reads
`no longer references <assembly>`; a reference, by GUID or by name, that matches no `.asmdef` in the project
makes the check skip that `.asmdef` rather than guess. This is the intended refusal, not a bug. Run
`uloop compile` once so Unity rewrites the response files, then `compile-check` works again against
the new configuration.

An `.asmdef` edit that is not one of those does **not** raise this error — see Limitations for what
goes undetected. In that case the run succeeds against the previous configuration, so run
`uloop compile` yourself after such an edit rather than trusting a clean result.

**`COMPILE_CHECK_UNITY_BUILD_REQUIRED`: a build setting changed since the last Unity build**
The message reads `<setting> changed since the last Unity build` - for example
`scripting defines of Standalone (removed: FOO)`, `Assets/csc.rsp`,
`defineConstraints or versionDefines of Assets/X/X.asmdef` or `Packages/packages-lock.json` - or
`scripting define <NAME> is not in the last Unity build`. The response files were written with the
settings of the last Unity build, so compiling against them would check a configuration the project
no longer has. Run `uloop compile` once; the build moves the build-trace file forward, the next run
records the settings as they are now, and `compile-check` works again.

**`COMPILE_CHECK_UNITY_BUILD_REQUIRED`: a script belongs to an assembly the last build did not produce**
The message reads `<script> belongs to <assembly>, which the last Unity build did not produce`. The
script sits outside every `.asmdef`, and Unity's path rules give it to a predefined assembly - such
as `Assembly-CSharp-Editor` for the first script in an `Editor` folder - that has no response file
yet. Run `uloop compile` once so Unity creates that assembly.

**`COMPILE_CHECK_UNITY_BUILD_REQUIRED`: cannot tell which Bee build to check**
Both a debug and a release dag exist and neither `tundra.log.json` nor
`Library/EditorOnlyScriptingSettings.json` could be read. Building once from the Editor restores the
log.

**`COMPILE_CHECK_PROJECT_BUSY`**
Another `compile-check` on the same project held the lock for the whole wait. Let it finish, then
run `compile-check` once more; retrying in a loop only queues behind it again.

**The Editor install cannot be found**
The version in `ProjectVersion.txt` is not installed, or it lives outside the locations the Editor
discovery searches. Pass `--editor-version <version>` to select an installed one.

**Diagnostics look stale**
Only changed assemblies and their dependents are compiled by default. Use `--all` to compile
everything in the build.
