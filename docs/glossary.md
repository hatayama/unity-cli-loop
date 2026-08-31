# Glossary

Ubiquitous language for Unity CLI Loop. Code identifiers, docs, commit messages, and reviews
should use these terms with the meanings defined here.

## Naming policy

Public package, assembly, and extension API identifiers are never renamed as part of
terminology cleanup (see the repository guidelines). When an internal identifier conflicts
with this glossary, prefer renaming the internal identifier; when a public identifier
conflicts, keep the identifier and document the mismatch here instead.

## Known Naming Debt (Public API, Kept by Policy)

The following public identifiers are kept by policy even though their names do not fully
match the current glossary:

- `UnityCliLoopConstants.COMMAND_NAME_AWAIT_PAUSE_POINT` and
  `UnityCliLoopConstants.COMMAND_NAME_PAUSE_POINT_STATUS` are used as tool catalog names,
  not internal bridge commands.
- The skill target types `SkillsTarget`, `SkillSetupTargetInfo`, and
  `ToolSkillSynchronizer.SkillTargetInfo` describe related concepts at different layers,
  but they remain public package identifiers.
- Several first-party tool UseCases implement `IUnityCliLoop*Service` interfaces. The names
  are preserved because renaming public identifiers is outside terminology cleanup; removing
  the pass-through interfaces would be a separate structural refactor.

## Terms

### Tool

A unit of Unity-side functionality exposed to the CLI, implemented as an
`IUnityCliLoopTool` and registered in the tool registry. Each tool maps to one CLI command
an agent can run (for example `compile`, `get-logs`, `run-tests`). Tool names use
`UnityCliLoopConstants.TOOL_NAME_*` constants.

### Internal bridge command

A CLI-only request handled inside the Unity package that must not appear in the
extension-facing tool registry (for example `get-version`, `get-compile-status`).
Routed by `InternalBridgeCommandRouter`; names use `UnityCliLoopConstants.COMMAND_NAME_*`
constants. Anything an end user or third-party extension should call is a tool, not an
internal bridge command.

### Server

The local IPC endpoint hosted inside the Unity Editor by the package
(`UnityCliLoopBridgeServer`) — a Unix domain socket on macOS/Linux, a named pipe on
Windows, never TCP (`BridgeTransportEndpoint`). It accepts short-lived sessions from the
CLI, dispatches JSON-RPC requests to tools and internal bridge commands, and reports
editor readiness.

### CLI

The `uloop` command-line interface as a whole — the user-facing entry point that talks to
the server over local IPC. Physically it consists of two Go binaries: the dispatcher and
the project runner.

### Dispatcher

The globally installed `uloop` binary. It resolves the target Unity project, ensures the
pinned project runner release is installed (downloading it when needed), keeps itself
up to date against the pin's `minimumDispatcherVersion`, and delegates command execution
to the project runner. Released via release-please from `cli/dispatcher/`.

### Project runner

The per-project CLI binary that implements the actual commands and speaks the IPC protocol
with the Unity package. Its release is selected by the pin's `projectRunnerVersion`, so a
project always runs the runner generation its package was built against. Released via
release-please from `cli/project-runner/`.

### Pin

`Packages/src/project-runner-pin.json` (mirrored byte-identically to
`.uloop/project-runner-pin.json`), the single source for cross-component version
requirements: `projectRunnerVersion` (stamped by release-please) and
`minimumDispatcherVersion` (the manually maintained dispatcher floor). The pin evolves
additively only — existing fields are never deleted or renamed.

### Protocol version

The integer IPC contract generation declared on both sides: `protocolVersion` in
`cli/common/clicontract/contract.json` and `CliConstants.REQUIRED_CLI_PROTOCOL_VERSION`
in the package. The runtime gate requires exact equality; both are bumped together only
when the wire format breaks compatibility.

### Skill

A generated instruction document that teaches an AI agent how to use a `uloop` command.
Skill sources live in the package; the copies under `.agents/` and `.claude/` are
generated and must not be edited directly.

A skill is also the single source of truth for the tool and parameter descriptions the CLI
prints. `--help` and `uloop list` read the parameter table out of the installed package's
skill at render time, and the embedded catalog (`cli/common/tools/default-tools.json`) is
generated from those same tables. Descriptions are therefore edited in the skill and nowhere
else.

### Skill target

A destination agent environment into which skills are installed (for example Claude Code,
Codex CLI). Each target defines where its skill copies live and which folder layout it uses.

### UseCase

An Application-layer orchestration class that coordinates one tool execution flow across
domain services and infrastructure ports. UseCases contain no UI and no direct Unity
editor state access; they are the only layer that sequences multi-step tool work.

### Pause point

A registry entry (`UloopPausePointRegistry`) that freezes PlayMode when a specific code path
is reached, then reports execution state through `await-pause-point`/`pause-point-status`.
A pause point is enabled either by a hand-written `UloopPausePoint.Pause(id)` marker call, or
as a source pause point resolved from a `--file`/`--line` location with no source edit.

### Source pause point

A pause point enabled by `enable-pause-point --file <path> --line <N>` instead of a marker
`Id`. `SourcePausePointResolver` maps the file and line to a patch location over the
method's portable PDB, and `SourcePausePointPatcher` injects the capture call at that
instruction via a Harmony transpiler — no source edit or recompile is required. Source
pause points are removed automatically on `clear-pause-point`/`ClearAll` and never survive a
script compile or domain reload.

### Captured variable

One local, parameter, or `this` instance field snapshotted at the moment execution reaches
a pause point, represented by `UloopCapturedVariable` internally and exposed as
`CapturedVariables` in tool and status responses. Its `Scope` is `Local`, `Parameter`, or
`InstanceField`; `UnityEngine.Object` values additionally carry `UnityObjectKind`,
`UnityObjectPath`, and `UnityObjectInstanceId`. The snapshot is taken before the resolved
line executes, and `CapturedVariablesTruncated` reports whether the length or count cap
clipped any evidence.

### Hot reload

The first-party tool `hot-reload` that replaces method bodies in a running Unity Editor
from edited project source files without a domain reload. It transforms each editable
method into a static shim, compiles that shim against publicized references, loads the
shim assembly, and transplants the shim IL into the original method via a Harmony
transpiler (`io.github.hatayama.uloop.hot-reload`). Patches are static Editor state and
disappear on domain reload by design; a later `uloop compile` converges to the same
source behavior.

### Source snapshot

A per-assembly byte-exact copy of project source files under
`Library/UloopHotReload/SourceSnapshot/<assemblyName>-<mvid>/`, captured after domain
reload. Hot reload adopts a snapshot as the edited-method baseline only when its bytes
match the corresponding portable-PDB document checksum for that source file; otherwise
the file falls back to patching every editable method.

### Shim

A generated static method that mirrors an edited user method body for hot reload. For an
instance method, the shim is a static method whose first parameter is the original
instance (`__uloopInstance`) so IL argument slots match the original method and the body can be
transplanted without rewriting call/load slots. Prefix wrappers are not generated.

### Shim registration

The per-file record hot reload publishes after a successful apply so pause points can
resolve against the patched code: the shim assembly and portable-PDB bytes plus, for
each patched method, its shim `MethodBase`, source line range, and whether the patch is
a delegation (`HotReloadShimRegistry`, exposed through
`HotReloadPausePointCoordination`). A file's registration is replaced by the next
hot-reload generation, and a method's entry is removed when its patch is reverted;
consumers treat a missing entry as "cannot re-target", never as an error.

### Re-target

Moving an armed source pause point's instrumentation to follow a hot-reload patch
transition without changing the marker's identity or registry state. On apply, the
marker's requested file and line are re-resolved against the new shim registration and
the capture call is re-injected into the patched body; on revert, the same request is
re-resolved against the compiled assembly. A marker that re-targets onto a patched body
reports `RetargetedToHotReloadPatch: true`; one that cannot be re-targeted is suppressed
(`SuppressedByHotReload: true`, with a reason) until a later transition or
`uloop compile` makes its line resolvable again.

### Publicized reference

A Cecil-rewritten copy of a project assembly under `Library/UloopHotReload/PublicizedRefs/fmt2/`
in which every type and member is public — except field-like event backing fields, which stay
non-public so shim compilation does not see both the event and its same-named backing field
(CS0229). Hot reload uses these copies only as compile-time
references for shim compilation so private/internal member access type-checks; they are
never loaded into the Editor domain as the runtime identity of the target types.
