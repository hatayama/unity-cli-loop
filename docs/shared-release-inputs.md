# Shared release inputs and triggers

The dispatcher is released through release-please like the project runner and
the Unity package: `cli/dispatcher/dispatchercontract/dispatcher-contract.json`
`dispatcherVersion` and `cli/dispatcher/CHANGELOG.md` are stamped by
release-please release PRs. Never bump `dispatcherVersion` by hand (the one
exception is a version series realignment; see
`docs/version-series-realignment.md`).

release-please attributes a commit to a component only when the commit touches
that package root (`Packages/src/`, `cli/dispatcher/`, `cli/project-runner/`).
Shared release inputs living outside those roots therefore need explicit
trigger updates in the same PR:

- Common module sources (non-test `cli/common/**/*.go`, `cli/common/go.mod`, `cli/common/go.sum`) must be
  accompanied by changes under both `cli/project-runner/` and `cli/dispatcher/`.
- Installer scripts (`scripts/install.sh`, `scripts/install.ps1`) must be accompanied by a
  change under `cli/dispatcher/`, because installers ship as dispatcher release assets.
- The embedded tool catalog (`cli/common/tools/default-tools.json`) is the one
  non-Go shared input: it is compiled into both binaries, so catalog changes —
  including regenerations driven by skill parameter-table edits — need the
  same accompanying trigger changes and stamp refresh.

Run `scripts/stamp-release-inputs.sh` to refresh
`cli/project-runner/shared-inputs-stamp.json` and
`cli/dispatcher/shared-inputs-stamp.json`, and commit the stamp updates with
the change. Pull request CI runs `check-release-triggers` (authoritative rules:
`releaseTriggerRules` in
`cli/release-automation/internal/automation/release_trigger_guard.go`) and
fails when shared release inputs changed without the matching triggers.

## Trigger scoping stops at package granularity

Trigger rules resolve which components a shared input reaches by package, not by
symbol: within a shared-list package, every non-test Go change triggers both
components regardless of which one can actually execute the changed code. A
change reachable from only one component therefore still releases both — that is
expected behaviour, not accidental churn. Before proposing finer scoping to
remove the resulting no-op releases, read
`docs/adr/0003-release-trigger-scoping-granularity.md`: the gap is deliberate,
and both finer alternatives are recorded there with their reversal conditions.
