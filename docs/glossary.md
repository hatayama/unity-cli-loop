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

- `UnityCliLoopConstants.COMMAND_NAME_WAIT_FOR_PAUSE_POINT` and
  `UnityCliLoopConstants.COMMAND_NAME_PAUSE_POINT_STATUS` are used as tool catalog names,
  not internal bridge commands.
- The skill target types `SkillsTarget`, `SkillSetupTargetInfo`,
  `ToolSkillSynchronizer.SkillTargetDefinition`, and `ToolSkillSynchronizer.SkillTargetInfo`
  describe related concepts at different layers, but they remain public package identifiers.
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

The TCP IPC endpoint hosted inside the Unity Editor by the package
(`UnityCliLoopBridgeServer`). It accepts short-lived sessions from the CLI, dispatches
JSON-RPC requests to tools and internal bridge commands, and reports editor readiness.

### CLI

The `uloop` command-line interface as a whole — the user-facing entry point that talks to
the server over TCP. Physically it consists of two Go binaries: the dispatcher and the
project runner.

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

### Skill target

A destination agent environment into which skills are installed (for example Claude Code,
Cursor). Each target defines where its skill copies live and which folder layout it uses.

### UseCase

An Application-layer orchestration class that coordinates one tool execution flow across
domain services and infrastructure ports. UseCases contain no UI and no direct Unity
editor state access; they are the only layer that sequences multi-step tool work.
