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
  without the Editor; it asks for another build when it detects an `.asmdef` change. A scripting define
  change it cannot detect, so run `uloop compile` yourself after one.

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

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `--all` | flag | Compile every assembly instead of only the changed ones and their dependents |
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
- `SkippedAssemblies`: how many assemblies were left out: nothing they compile from changed, or every assembly they reference kept the same public surface
- `ResponseFileSet`: the Bee build the run replayed
- `ProjectRoot`: resolved project root
- `Message`: one-line summary

The process exits 1 when `ErrorCount` is greater than zero.

## Limitations

- It replays the last Unity build's response files. A new or deleted `.asmdef`, a changed `.asmdef`
  reference, an `.asmdef` that stopped building for the Editor, or a newly required precompiled
  reference stops the run and asks for `uloop compile`.
- Changes to `defineConstraints` and `versionDefines` are **not** detected, because they depend on
  scripting defines and package versions that cannot be evaluated outside the Editor. Run
  `uloop compile` after editing them.
- A reference **removed** from an `.asmdef` is not detected either: Unity injects references of its
  own that no `.asmdef` declares, so only added references can be told apart from those. Diagnostics
  may therefore miss an error that the removal would cause. Run `uloop compile` after removing one.
- Changed scripting defines are not detected; the defines from the last build are reused.
- The predefined assemblies (`Assembly-CSharp` and friends) keep the source list of the last build,
  so a brand-new `.cs` file outside an `.asmdef` is not compiled until Unity imports it.
- The produced DLLs are never loaded by Unity, and the command never contacts a running Editor.
- Linux is not supported.
