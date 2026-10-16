---
name: uloop-compile-check
description: "Compile C# with the Unity-bundled Roslyn compiler without launching or contacting the Unity Editor; reports errors and warnings only."
---

# uloop compile-check

Compile the project's C# offline and report the diagnostics, without starting Unity or sending
anything to a running Editor.

The command replays the response files Unity wrote during its last build, using the C# compiler
bundled with the Unity Editor install. Nothing it produces is loaded by Unity: the assemblies land
in `Library/uloop/compile-check/` and are never handed to the Editor.

## When to use

- The Editor is not running and you only want to know whether the code compiles.
- You want compiler errors quickly, without waiting for an import and a domain reload.
- You are iterating on code whose result does not need to be live in the Editor yet.

Unseen edits (see Limitations): a generator reading a referenced assembly, a new script in a package
folder without an `.asmdef`, build-profile defines, a setting changed before the first run after a
build. Run `uloop compile` after one.

## When not to use

- You want the change reflected in the Editor (play mode, tests, a tool call): run `uloop compile`.
- You just added or removed an `.asmdef`, added a reference to one, or changed scripting defines:
  run `uloop compile` once first. The response files describe the previous build only, so
  compile-check refuses the run with `COMPILE_CHECK_UNITY_BUILD_REQUIRED` instead of reporting
  diagnostics for a configuration the project no longer has.
- The project has no `Library` yet (a fresh clone, or an Editor that has never opened it): there is
  no build to replay, so the run is refused with the same code. Let Unity build once; a headless run
  is enough and needs no window:

  ```bash
  <Unity executable> -batchmode -nographics -quit -projectPath /path/to/project
  ```

  `uloop launch` does the same with the Editor window open. After that one build, compile-check works
  without the Editor; it asks for another build when it detects an `.asmdef` change or a build setting
  that moved; see the limitations for what stays unseen.

## Usage

```bash
uloop compile-check
uloop compile-check --all --project-path /path/to/project
```

By default it compiles the assemblies whose sources changed since the last Unity build, plus every
assembly that references one of them. `--all` compiles every assembly in the build. Assemblies that do not depend on each other compile at
the same time; `--jobs` caps how many run at once, and `--jobs 1` compiles them one after another.

An assembly whose inputs are all exactly as the previous `compile-check` read them is not compiled
again: the run reports the diagnostics recorded then, errors included. `--all` compiles
everything from scratch, which is the way out if a reused result ever looks wrong.

In a large assembly whose last compile-check compile succeeded, an edit that keeps every
declaration is checked on the changed files only, in a few seconds. Adding or removing a using
directive counts as a declaration change. Such a check skips a few assembly-wide rules, so run once
with `--full` before finishing. When the project treats warnings as errors, a rule listed in
`UncheckedCodes`, such as CS0414, can fail that `--full` run only. The first body edit after a Unity
build, after a full compile that had errors (a `--full` run or a run that fell back to one), after
a run that compiled the assembly only because an assembly it references changed, or after `--all`
in an assembly that references one whose rebuilt API differed from the last Unity build and that
the next run no longer rebuilds is compiled in full and records what the next check compares
against; `FastPathFallbacks` says why.

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `--all` | flag | Compile every assembly instead of only the changed ones and their dependents |
| `--full` | flag | Compile changed assemblies in full instead of checking only their changed files |
| `--editor-version <version>` | string | Use this Unity Editor version's compiler instead of ProjectVersion.txt |
| `--jobs <N>` | number | Compile up to N assemblies at once (default: half the CPUs, at least 1; `1` compiles sequentially) |
| `--max-depth <N>` | number | Search depth when locating the project (default: 3, -1 for unlimited) |
| `--project-path <path>` | string | Target another Unity project instead of the current directory |

## Output

A JSON payload:

- `Success`: whether the compile produced no errors
- `ErrorCount`, `WarningCount`: totals across every compiled and reused assembly
- `Errors`, `Warnings`: each with `Message`, `Code`, `File`, `Line`, `Column`, `Assembly`
- `CompiledAssemblies`: the assemblies this run compiled, in dependency order
- `ReusedAssemblies`: the assemblies reported from the previous run's recorded result, without compiling
- `BlockedAssemblies`: the assemblies not compiled because an assembly they reference has errors, as Unity's own build would stop there; fix those errors and run again
- `FastCheckedAssemblies`: the assemblies checked on their changed files only
- `FastChecks`: per fast-checked assembly, `ChangedFiles`, `UncheckedCodes` (rules only `--full` runs), `GeneratorsNotRerun`, and `Reused` (the previous run's check of the same edit, reported again)
- `FastPathFallbacks`: `Assembly` and `Reason` for each assembly that was compiled in full instead
- `FastPathBaselineNotes`: `Assembly` and `Reason` for each successful full compile that left no baseline for the next changed-files check
- `SkippedAssemblies`: how many assemblies were left out: nothing they compile from changed, or every assembly they reference kept the same public surface or changed only names they never write
- `ResponseFileSet`: the Bee build the run replayed
- `ProjectRoot`: resolved project root
- `Message`: one-line summary

The process exits 1 when `ErrorCount` is greater than zero. A failure of the command itself, such
as `COMPILE_CHECK_UNITY_BUILD_REQUIRED`, also exits 1 but prints no payload: the error, with its
`ErrorCode`, goes to stderr.

Only one compile-check runs per project at a time. A second one waits for the first, up to five
minutes, then fails with `COMPILE_CHECK_PROJECT_BUSY`: wait for the other run to finish and run
compile-check once more rather than retrying in a loop.

## Limitations

- It replays the last Unity build's response files. A new or deleted `.asmdef`, a changed `.asmdef`
  reference, an `.asmdef` that stopped building for the Editor, or a newly required precompiled
  reference stops the run and asks for `uloop compile`.
- Build settings are compared against a record the first run after a Unity build writes: a
  removed scripting define, a changed `csc.rsp`, `defineConstraints`, `versionDefines`,
  `packages-lock.json` or `manifest.json` stops the run, and a scripting define the last build lacks
  stops it when `ProjectSettings.asset` was saved after that build. A change made before that first
  run (other than an added define) is not seen, so run `uloop compile` after one. Under a Unity 6
  build profile that overrides Player Settings or carries its own defines, only the global
  ProjectSettings defines are compared.
- A reference removed from an `.asmdef` stops the run with `COMPILE_CHECK_UNITY_BUILD_REQUIRED`; the
  four references Unity adds on its own are allowed, and an `.asmdef` with a reference that matches
  no `.asmdef` in the project is not judged.
- A new `.cs` file outside every `.asmdef` goes to `Assembly-CSharp` or a sibling by Unity's path
  rules (`Editor` folders, `Assets/Plugins`); if the last build did not produce that assembly, the run
  stops and asks for `uloop compile`. A new script in a package folder without an `.asmdef` is not seen.
- The produced DLLs are never loaded by Unity, and the command never contacts a running Editor.
