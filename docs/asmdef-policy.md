# Asmdef Reference Policy

Assembly definitions (`.asmdef`) under `Packages/src` encode the package
architecture: Clean Architecture layers, a set of independent first-party tools,
and shared utilities between them. Unity already rejects reference cycles, and
the layer assemblies already fail to compile when a reference is missing, but
nothing stopped an *extra* reference from being added in a direction the
architecture forbids — for example one tool referencing another to borrow a
helper. `check-asmdef-policy` closes that gap.

## What it enforces

The checker reads every `.asmdef` under `Packages/src` (Unity-ignored `~`
folders excluded), resolves `GUID:` references through the sibling `.asmdef.meta`
files, drops references to assemblies outside the tree (Unity packages, the test
runner), classifies each assembly by name, and fails on any reference that the
table below does not allow.

### Categories

| Category | Assembly names |
|----------|----------------|
| Layer | `UnityCLILoop.ToolContracts`, `UnityCLILoop.Domain`, `UnityCLILoop.Application`, `UnityCLILoop.Infrastructure`, `UnityCLILoop.Presentation`, `UnityCLILoop.CompositionRoot.Editor` |
| InternalBridge | `Unity.InternalAPIEditorBridge.024` |
| Runtime | `*.Runtime` |
| ToolsUmbrella | `UnityCLILoop.FirstPartyTools.Editor` |
| ToolCommon | `UnityCLILoop.FirstPartyTools.Common.<Name>.Editor` |
| Tool | `UnityCLILoop.FirstPartyTools.<Tool>.Editor` (sub-assemblies such as `RunTests.TestFramework` belong to their parent tool) |

An assembly whose name matches none of these is an error, not a finding: extend
the naming convention (and this table) before adding it.

### Allowed references

| From | May reference | Rule id on violation |
|------|---------------|----------------------|
| ToolContracts | nothing inside the package | `layer-direction` |
| Domain | ToolContracts | `layer-direction` |
| Application | Domain, ToolContracts | `layer-direction` |
| Infrastructure | Application, Domain, ToolContracts, InternalBridge, Runtime, ToolCommon | `layer-direction` |
| Presentation | Application, Domain, ToolContracts | `layer-direction` |
| CompositionRoot | every layer, ToolsUmbrella, Runtime, InternalBridge | `layer-direction` |
| InternalBridge | nothing inside the package | `layer-direction` |
| Runtime | other Runtime assemblies | `runtime-isolation` |
| ToolsUmbrella | Tool, ToolCommon, ToolContracts, Domain | `umbrella-scope` |
| ToolCommon | ToolContracts, Domain, ToolCommon, Runtime, InternalBridge | `common-layering` |
| Tool | ToolContracts, Domain, Application, ToolCommon, Runtime, InternalBridge, its own parent tool | `tool-isolation` for another Tool, `layer-direction` otherwise |

The two rules that motivated the checker:

- **tool-isolation** — a tool must not reference another tool. Tools are meant
  to be independently removable; a tool-to-tool reference turns a helper into a
  hidden shared dependency.
- **common-layering** — `FirstPartyTools.Common.*` sits below the tools *and*
  below `Application`. A Common assembly that reaches into `Application` can no
  longer be reused by anything that must stay lightweight.

There is no cycle rule. Unity refuses to import an asmdef cycle, and the
`unity-compile-check-and-test-runner.yml` workflow already surfaces that.

## Allowlist

`tools/asmdef-policy-allowlist.json` lists the references that violate the
policy today and are tolerated until they are repaid:

```json
{
  "allowedReferences": [
    {
      "from": "UnityCLILoop.FirstPartyTools.Watch.Editor",
      "to": "UnityCLILoop.FirstPartyTools.PausePoint.Editor",
      "reason": "Why the reference exists today. Resolution: how it will be removed."
    }
  ]
}
```

Rules for the file:

- Every entry needs `from`, `to`, and a non-empty `reason`. The reason states
  *why* the reference exists and the *planned resolution*; "needed" is not a
  reason.
- An entry whose reference no longer exists is a **stale entry and fails the
  check**. Delete it in the same commit that removes the reference. The
  allowlist can only shrink by accident, never grow by accident.
- Adding an entry is a reviewable decision. Prefer removing the reference; add an
  entry only when the removal genuinely has to wait.

## Removing a tool-to-tool reference

Three remedies cover every case seen so far:

1. **Promote to Common.** When the borrowed code is a pure utility (a formatter,
   a constant set, a compiler wrapper), move it into a
   `FirstPartyTools.Common.<Name>.Editor` assembly and have both tools reference
   that.
2. **Invert the dependency.** When the borrowed code is a tool *behaviour*
   (stopping Play Mode, recording a session value), declare a port interface in
   `ToolContracts`, implement it in the owning tool, and wire the implementation
   in `CompositionRoot`. The consuming tool depends only on the port.
3. **Extract a foundation.** When several tools share a large capability
   (dynamic compilation), give it its own Common assembly and make the original
   tool depend on it too, so the tool becomes one consumer among others.

## Adding a new assembly

Name it so that it lands in exactly one category:

- A new tool: `UnityCLILoop.FirstPartyTools.<Tool>.Editor`, referenced from the
  umbrella `UnityCLILoop.FirstPartyTools.Editor`.
- A sub-assembly of a tool: `UnityCLILoop.FirstPartyTools.<Tool>.<Part>.Editor`.
- A shared utility: `UnityCLILoop.FirstPartyTools.Common.<Name>.Editor`.
- Runtime code: `<Anything>.Runtime`.

A name outside these shapes makes the check fail with
`matches no assembly category` until the convention is extended in
`cli/release-automation/internal/automation/asmdef_policy_rules.go`.

## Local usage

From `cli/release-automation`:

```sh
go run ./cmd/check-asmdef-policy --root "$(git rev-parse --show-toplevel)"
```

Exit code 0 prints `No asmdef reference violated the policy.`; exit code 1 lists
each violation as `From -> To: rule (path)` and each stale allowlist entry.
`--allowlist <path>` points at a different allowlist for experiments.

## Where it runs

1. **Pre-commit hook** (`.husky/pre-commit`) — when a staged change touches any
   `Packages/src/**/*.asmdef` or the allowlist. Catches the reference while the
   code that needs it is still small.
2. **Pull-request CI** — the `Check asmdef reference policy` step in
   `.github/workflows/build-and-test.yml`.
3. **Unit tests** — `asmdef_policy_test.go` covers every row of the permit table
   and one violation per rule, so a checker that accidentally allows everything
   fails its own tests.
