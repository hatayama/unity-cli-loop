# Offline compile check

`uloop compile-check` reports C# compile errors and warnings without launching the Unity Editor and
without contacting a running one. It is a dispatcher-owned command: it needs no IPC connection and
no project runner.

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
   since the last Unity build, plus every assembly that references one of them. `--all` compiles
   every assembly in the build. Assemblies left out are counted in `SkippedAssemblies`. An
   assembly's sources are re-globbed from its own directory, stopping at nested assembly
   boundaries; the files an `.asmref` attaches to it are taken from the last build's response file
   instead, wherever that folder sits — including inside a nested assembly's directory, which the
   glob never reaches. Such a file is kept only while an `.asmref` still attaches its folder to this
   assembly: the walk up from the file to the first folder holding an `.asmref` or an `.asmdef` says
   which assembly owns it today. An `.asmref` naming an assembly the build never produced — an
   unresolvable reference, or one excluded by platform or package settings — attaches nothing, so
   its folder is treated as an ordinary part of the assembly around it, which is what Unity compiles
   it as. A package shipping a sample for a render pipeline the project does not install is the
   ordinary case. Membership is compared without looking at any timestamp, because
   moving a file carries its modification time along, and either an `.asmdef` or an `.asmref` can
   hand a folder's sources to a different assembly without editing a single `.cs` file.
4. **Check that the response files still describe the project.** An assembly definition that was
   added, removed, or that stopped building for the Editor, and a reference or a precompiled
   reference that was added to one, invalidate the recorded build. The run then stops with
   `COMPILE_CHECK_UNITY_BUILD_REQUIRED` rather than reporting diagnostics for a configuration the
   project no longer has.
5. **Compile in dependency order** and parse the compiler's diagnostics into JSON. An assembly starts
   as soon as every assembly it references that this run also compiles has finished, so independent
   assemblies compile at the same time. `--jobs <n>` caps how many run at once; the default is half
   the machine's CPUs and never less than one, because one `csc` process already keeps two to three
   cores busy on its own. `--jobs 1` compiles the assemblies one after another.
   The reported order does not depend on the job count: results are collected in dependency order
   whatever finishes first.

Files and directories Unity ignores — any name starting with a dot or ending with a tilde, which
covers the `._*` AppleDouble siblings macOS writes on non-native volumes — are skipped everywhere
the project tree is walked, so their binary content never reaches the index or the compiler.

Assembly definitions are indexed from `Assets`, `Packages` and `Library/PackageCache`, plus every
directory a `Packages/manifest.json` dependency points at with a `file:` path — a package a project
develops locally lives outside the project root and would otherwise look like one whose assembly
definitions were deleted.

## Output

The compiled DLLs land in `Library/uloop/compile-check/<dag>/` and are never handed to Unity. The
directory is disposable; deleting it only costs the next run some time.

`--jobs <n>` bounds how many assemblies compile at the same time; the default is half the machine's
CPUs and never less than one, and `--jobs 1` compiles them one after another.

The command prints a JSON payload with `Success`, `ErrorCount`, `WarningCount`, `Errors`,
`Warnings` (each diagnostic carrying `Message`, `Code`, `File`, `Line`, `Column`, `Assembly`),
`CompiledAssemblies`, `SkippedAssemblies`, `ResponseFileSet`, `ProjectRoot` and a one-line
`Message`. The process exits 1 when `ErrorCount` is greater than zero.

## Limitations

- **It replays the last Unity build, and only some staleness is detectable.** Four `.asmdef`
  settings are compared against the response file, because they reach `csc` and can be read without
  evaluating defines or package versions: whether the assembly still builds for the Editor, a
  project reference it gained, `allowUnsafeCode` turning on, and a precompiled reference it gained
  under `overrideReferences`. A new or deleted `.asmdef` is detected too, as is every way
  assembly membership moves without any `.cs` file changing: an `.asmdef` that no longer sits over
  any source its response file recorded (it was moved into another assembly's folder), an `.asmref`
  whose folder holds sources the assembly it names does not record (it was added or moved), and a
  recorded source no `.asmref` attaches to its assembly any more (one was removed or retargeted).
  Those cases — and only those — stop the run with `COMPILE_CHECK_UNITY_BUILD_REQUIRED` and ask for
  `uloop compile`.
  Every other edit listed below is invisible to the check: the run proceeds and silently reuses
  what the last build recorded.
- **A removed `.asmdef` reference is not detected.** Unity injects references of its own that no
  `.asmdef` declares, so a reference present in the response file but absent from the `.asmdef`
  cannot be told apart from an injected one. Diagnostics may therefore miss an error that removing
  a reference would cause. Run `uloop compile` after removing one.
- **`defineConstraints` and `versionDefines` changes are not detected**, because they depend on
  scripting defines and package versions that can only be evaluated inside the Editor.
- **Changed scripting defines are not detected**; the defines recorded in the last build are reused.
- **The predefined assemblies** (`Assembly-CSharp` and friends) keep the source list of the last
  build, so a brand-new `.cs` file outside any `.asmdef` is not compiled until Unity imports it.
- **A new `.cs` file inside an `.asmref` folder is not compiled either.** The sources an `.asmref`
  attaches come only from the last build's response file, because the glob stops at the folder's
  assembly boundary, so a file added there waits for Unity to import it just as the predefined
  assemblies do.
- **Linux is not supported.** macOS and Windows only.

## Troubleshooting

**`COMPILE_CHECK_UNITY_BUILD_REQUIRED`: no Bee build artifacts found**
The project has never been built by this Editor, or `Library` was deleted. Open the project once
(`uloop launch`) and let it compile, then retry.

**`COMPILE_CHECK_UNITY_BUILD_REQUIRED`: an assembly definition no longer matches the last build**
Raised when an `.asmdef` was added, deleted or moved, when an `.asmref` was added, moved, removed
or retargeted so that a folder's sources now belong to a different assembly than the one the last
build compiled them into, or when one of the four compared settings changed (no longer builds for
the Editor, gained a project reference, turned on `allowUnsafeCode`, gained a precompiled reference
under `overrideReferences`). This is the intended refusal, not a bug. Run
`uloop compile` once so Unity rewrites the response files, then `compile-check` works again against
the new configuration.

An `.asmdef` edit that is not one of those does **not** raise this error — see Limitations for what
goes undetected. In that case the run succeeds against the previous configuration, so run
`uloop compile` yourself after such an edit rather than trusting a clean result.

**`COMPILE_CHECK_UNITY_BUILD_REQUIRED`: cannot tell which Bee build to check**
Both a debug and a release dag exist and neither `tundra.log.json` nor
`Library/EditorOnlyScriptingSettings.json` could be read. Building once from the Editor restores the
log.

**The Editor install cannot be found**
The version in `ProjectVersion.txt` is not installed, or it lives outside the locations the Editor
discovery searches. Pass `--editor-version <version>` to select an installed one.

**Diagnostics look stale**
Only changed assemblies and their dependents are compiled by default. Use `--all` to compile
everything in the build.
