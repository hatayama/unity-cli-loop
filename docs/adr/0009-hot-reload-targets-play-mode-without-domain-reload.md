# Hot Reload Targets Play Mode Entry Without Domain Reload

Date: 2026-09-26

## Decision

Hot reload is designed and verified for projects that enter Play Mode without a domain
reload: Enter Play Mode Settings set to **Reload Scene only** (the Unity 6.6 default for new
projects) or **Do not reload Domain or Scene**. In that configuration every active change
survives Play entry, and values wired into added fields are given back to the objects a scene
reload rebuilds.

With domain reload enabled on Play entry, hot reload keeps working but guarantees only this:

- Only the changes still active when Play Mode is entered are lost, because the reload
  unloads them. Hot reload applied after entering Play Mode is not affected.
- The loss is announced: `control-play-mode --action Play` warns before entering, and
  `hot-reload --status` reports `DroppedByPlayModeEntryCount` afterwards.
- Running `uloop hot-reload` again, or `uloop compile`, brings the changes back. Values wired
  into added fields are not brought back; the apply names the fields to wire again.

The code behind these guarantees stays and is frozen: the Play-entry drop ledgers,
`DroppedByPlayModeEntryCount`, re-selecting the owner files of discarded introduced types on a
run without `--files`, the rewire warning, and the Play-start warning. On this path only
defects that give a wrong result, or lose state without saying so, are fixed. Wording and
guidance improvements specific to it are not taken, and usability rounds do not run with
domain reload enabled.

Hot reload does not refuse to run, and does not ask the user to change Enter Play Mode
Settings, when domain reload is enabled.

## Context

Unity 6.6 changed the default Enter Play Mode Settings of new projects from Reload Domain and
Scene to Reload Scene only, and recommends keeping domain reload off to prepare for the
removal of Mono and of the domain reload mechanism in future versions
(<https://docs.unity.com/en-us/engine/6000.6/manual/whats-new/unity66>). Existing projects keep
the setting they have.

With domain reload enabled, each kind of change hot reload makes (patched methods, added
members, introduced types, added-field values) needs its own bookkeeping: recorded at Play
entry, recovered by a later apply, and counted in between. That bookkeeping is where the cases
multiply. After the usability rounds had converged on the primary configuration, the first
round run with domain reload enabled found that patches and added members inside a
re-introduced type stayed counted as dropped. Keeping this path at the same polish as the
primary one means running every round twice, for a configuration Unity is moving away from.

## Rejected Alternatives

- **Keep both configurations at the same level**: run a round with domain reload enabled
  before each merge to main and take its guidance findings. This doubles the rounds for the
  configuration Unity is retiring.
- **Refuse hot reload, or require changing the setting, when domain reload is enabled.**
  Projects created before Unity 6.6 keep domain reload enabled unless someone changes it, and
  hot reload applied after entering Play Mode is not affected by the reload at all. Refusing
  would block those projects for no gain.
- **Remove the domain-reload bookkeeping now.** It works and is covered by tests. Removing it
  would leave projects that still reload the domain without the count and without the
  re-selection of discarded files, and the removal is a change set with its own risk.

## Consequences

- Merging hot reload into main no longer waits for a round with domain reload enabled that
  adopts nothing.
- A finding from use with domain reload enabled is triaged by one question: does a result go
  wrong, or state get lost without notice? If so it is fixed; otherwise it is not taken.
- Usability rounds run their tester projects with Reload Scene only.
- When Unity removes domain reload from the Editor, the frozen code becomes unreachable and
  can be deleted in one change.

## Reversal condition

Reopen this decision if Unity returns to domain reload as the default for new projects, or if
reports from real use show that most hot-reload users keep domain reload enabled and the
guarantees above are not enough for them.
