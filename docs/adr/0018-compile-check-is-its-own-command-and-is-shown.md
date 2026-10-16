# Compile Check Is Its Own Command and Is Shown

Date: 2026-10-17

## Decision

`uloop compile-check` stays a command of its own rather than an option of `uloop compile`, and it
is listed in `--help`, `uloop list`, shell completion and the installed skills.

It is shown once the inputs that can change after the last Unity build and that it cannot detect
are only the ones under "What Stays Undetected" below. Every other input that moved since the last
Unity build either is compiled from the current files or makes the command refuse with
`COMPILE_CHECK_UNITY_BUILD_REQUIRED`, which a Unity build clears.

## Context

`uloop compile-check` replays the response files of the last Unity build through the
Editor-bundled Roslyn compiler. It was hidden because the inputs that could change after that
build without the replay seeing them - the limitations listed in `docs/compile-check.md` - were
too many to recommend it; each of them made it miss errors silently.

It was not made an option of `uloop compile`, for three reasons:

1. `uloop compile` produces the assemblies Unity loads and changes the Editor's state. The offline
   check touches no Editor and nothing it produces is ever loaded. If a flag decided whether the
   result reaches Unity, an agent would read a passing check as an applied change.
2. `uloop compile` is a Unity tool reached over IPC: it needs a running Editor and is rejected with
   BUSY while another command runs. The offline check runs without an Editor, takes its own
   project lock and fails with its own `COMPILE_CHECK_*` errors.
3. The options of `uloop compile` are defined by the Unity-side tool schema; the skill's parameter
   table has to match it, and the default tool catalog is generated from the table. The generator
   keeps a short allowlist of options the runner reads before Unity sees them
   (`run-tests --skip-compile`, `clear-pause-point --file` and `--line`), so a `--check` flag could
   be added the same way. Those options tune how the Unity tool runs; `--check` would make
   `compile` never reach Unity at all, so the flag would change what the command is rather than how
   it runs.

## What It Detects

The command refuses with `COMPILE_CHECK_UNITY_BUILD_REQUIRED` when:

- there are no Bee build outputs, or which build to replay cannot be decided;
- an assembly definition was added or removed;
- an assembly definition gained a reference, or lost one - except the four Unity adds without an
  `.asmdef` line: `UnityEngine.UI`, `UnityEditor.UI`, `UnityEngine.TestRunner`,
  `UnityEditor.TestRunner`;
- `allowUnsafeCode` was turned on, or a precompiled reference was added under
  `overrideReferences`;
- an assembly definition or assembly definition reference moved;
- a scripting define of the active build target group is missing from the last build;
- since the last Unity build, a scripting define, a `csc.rsp`, the `defineConstraints` or
  `versionDefines` of an assembly definition, `Packages/packages-lock.json` or
  `Packages/manifest.json` changed. This compares against a record tied to the files Bee rewrites
  on every build (`Library/Bee/tundra.log.json` and its neighbours);
- a new script outside every assembly definition belongs to a predefined assembly
  (`Assembly-CSharp` and its three siblings) that the last build did not produce.

## What It Compiles Rather Than Narrows

A dependent with no saved generator output - one the command has never compiled - is compiled when
a reference it uses changes its surface, instead of being left out by the names it writes. After
that compile its generator output is saved and read like its sources.

A dependent that writes a changed name only with `\u` escapes is compiled too: the names it writes
are read with the escapes decoded, where ADR 0017 left such a name unseen.

## What Stays Undetected

These are documented and not detected; after any of them, run `uloop compile`:

- (a) a source generator in a dependent that changes what it generates by reading a referenced
  assembly's contents rather than the dependent's own code;
- (b) a change to the type of an async method builder;
- (c) on a Unity version whose `Library/Bee` has none of the build-trace files, a scripting define
  removed since the last build and a changed `csc.rsp` and the other recorded settings (an added
  define is still seen);
- (d) a change made before the record exists - that is, before the first run after that build -
  other than an added scripting define: a removed define, or a changed `csc.rsp`,
  `defineConstraints`, `versionDefines`, `packages-lock.json` or `manifest.json`;
- (e) a new script in a package folder that has no assembly definition;
- (f) an analyzer config Unity passes with `/analyzerconfig:` that was added since the last build;
- (g) scripting defines are not compared in a project whose active build target is Dedicated
  Server (the response files carry `UNITY_SERVER`), because which settings group Unity reads for it
  cannot be determined;
- (h) a Unity 6 build profile that overrides Player Settings or carries its own scripting defines:
  only the global ProjectSettings defines are compared, and a global define the build lacks is
  refused only when `ProjectSettings.asset` was saved after the last build.

## Consequences

- Agents find the command through help, the command list and its skill, and can check C# edits
  without a running Editor.
- A refusal means "build once in Unity"; a refusal that a Unity build does not clear would make the
  command unusable, so every refusal above is cleared by the next `uloop compile`.
- The limitations above stay in `docs/compile-check.md` and the skill, so an agent knows when to
  fall back to `uloop compile`.

## Revisit When

- A tester round shows the check reporting an error `uloop compile` does not report, or a refusal
  that `uloop compile` does not clear. Then hide the command again until the cause is closed.
- Running the check offline before `uloop compile` - returning its errors without touching Unity -
  is decided in a separate ADR, once the detection above has gone through one tester round without
  a miss.
