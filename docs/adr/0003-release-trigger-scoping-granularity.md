# Release Trigger Scoping Granularity

Date: 2026-07-28

## Decision

Release trigger rules resolve which components a shared input reaches at **package
granularity** and stop there. `sharedCommonPackageRoots` and
`dispatcherOnlyCommonPackageRoots` in
`cli/release-automation/internal/automation/release_trigger_guard.go` are whitelists whose
alignment with `go list -deps ./...` is enforced by
`TestReleaseTriggerGuardCommonPackageWhitelistsMatchGoDependencies`, which also fails when a
new single-component package appears without a matching rule. Within a package on the
shared list, every non-test Go change triggers both the dispatcher and the project runner,
regardless of which component can actually execute the changed code.

We do not add symbol-level reachability analysis to suppress the resulting no-op releases.

## Context

release-please attributes a commit to a component only when the commit touches that
component's package root (`Packages/src/`, `cli/dispatcher/`, `cli/project-runner/`).
Shared release inputs live outside those roots, so `scripts/stamp-release-inputs.sh` writes
`shared-inputs-stamp.json` into each affected root to make the commit visible to that
component's release. Operational rules: `docs/shared-release-inputs.md`.

This produces releases with no shipped change. `cli/common/errors/busy_editor_state.go` is
the worked example. Its helpers are unexported and reachable only through
`classifyRPCError`, so only a component that issues Unity RPCs can produce
`UNITY_SERVER_BUSY` — and the dispatcher has no `unityipc` references in non-test code at
all. Changing that file in #2036 still produced a dispatcher release whose only shipped
difference was the version stamp.

Two facts about the mechanism are worth recording, because both were guessed wrong when
this question was reopened in 2026-07-28 review:

- `sharedInputsHash` is a change marker, not a compared value. Nothing reads it across
  commits: the guard never reads it, and its only consumers are the script that writes it
  and that script's own self-test. Its scope can change freely without breaking anything.
- The gap is not a limit of static analysis. Coupling between components here is carried by
  types and symbols, so a call graph can resolve it: the repository already runs
  symbol-level reachability in CI via govulncheck, Go product code contains zero direct
  `reflect` usage, and the untyped `Details` map is write-only in Go (every access is an
  assignment, all within the project runner) and is consumed by agents reading the JSON
  rather than by another component.

So this decision rests on cost, not on feasibility.

## Rationale

### The two failure directions are not symmetric

Releasing a component that did not need it costs one version increment. Failing to release
one that did need it produces version skew whose cause — release-please attributing commits
by package root — is invisible from the symptom: both components build, both test suites
pass, and the missing fix simply never reaches users of one binary. An over-approximating
rule cannot produce that failure. Keeping that property is worth more than clean version
numbering.

### A whitelist is reviewable; a call graph is not

Today a reviewer reads a 14-line whitelist and knows which components a file triggers, and
the alignment test keeps that whitelist mechanically correct against `go list -deps` —
package-level scoping is cheap to keep sound because its ground truth is a one-line query.
Under call-graph scoping nobody can predict the trigger set without running the tool, the
answer depends on the whole import graph, and no equivalently simple ground truth exists to
enforce it against.

### The benefit is measured in version numbers

The gap only affects shared-list packages whose changed code one component cannot reach.
The whitelists already route single-component packages correctly — `cli/common/version/` is
dispatcher-only and is classified as such. What remains is occasional no-op releases. Their
cost today is a beta increment, a changelog entry naming a PR that changed nothing for that
binary, and one `cli-release` environment approval, since the publish workflows
(`dispatcher-publish.yml`, `native-cli-publish.yml`) each gate on manual approval.

## Alternatives

### Symbol-level reachability (call graph)

Feasible and rejected on cost. `golang.org/x/tools/go/callgraph` (RTA/CHA/VTA) from each
component's main package computes an overapproximation of the functions reachable per
component; mapping them to files would classify each changed file conservatively, since
spurious call edges can only add a trigger, never drop one. Platform-specific files would
require running the analysis per release platform rather than from a single entry point.
Rejected because it buys clean version numbering at the price of the two properties above,
and because any under-approximation lands on the dangerous side of the asymmetry.

Reversal condition: revisit if the per-release cost grows beyond today's single approval
click — for example per-release attestation, review, or announcement duties after leaving
beta — or if no-op releases become frequent enough that component changelogs stop being
usable as change records.

### Reproducible-build comparison

Sound and rejected on cost. Building each component at base and head with `-trimpath` and
comparing bytes answers "does the shipped artifact change" without inferring reachability,
and the linker's dead-code elimination is conservative by construction. Rejected because it
requires building the release platform matrix on every pull request, since platform-specific
files mean one platform is not representative.

Reversal condition: revisit if CI ends up building the full release matrix on every pull
request for other reasons, which would make the marginal cost of the comparison near zero.

## Addendum (2026-09-02): data inputs

The embedded tool catalog is classified by a field-level JSON diff that drops
`description` keys. This is not Go reachability analysis: the ground truth is
the one-line question "does anything other than a description differ", which
stays reviewable. The miss direction is stale out-of-project `--help` wording
until the next real CLI release; it cannot produce functional version skew.

Reversal condition: withdraw this exception if embedded descriptions start to
affect runtime behavior, or if the CLI stops reloading them from SKILL.md.
