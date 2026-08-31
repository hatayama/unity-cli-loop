# Dir-Mode Evidence-Gated Ownership

Date: 2026-08-24

## Decision

In dir mode (`uloop skills install|uninstall|list --output-dir <path>`), uloop replaces or
deletes content inside `<path>/<skill-name>/` only when it holds **install evidence** that
uloop wrote that content. A name match alone never authorizes destruction.

Evidence is one of:

- an exact-name regular `SKILL.md` inside the skill directory (the sync writes it last, so
  its presence marks a completed install), or
- a source-owned entry whose content fully equals the current source (the orphan a partial
  removal leaves behind; this keeps orphan repair working). A match where both sides are
  empty — an empty file, or a directory with no comparable files — is vacuous and grants no
  evidence.

Without evidence, the skill is a per-skill **conflict**, not a fatal error: install reports
the reason in a `SKILL_STORE_CONFLICT` envelope, counts the skill under `Blocked:`, keeps
syncing the remaining skills, and exits 1; list prints `! <name> (conflict)` with the
reason and completes; uninstall preserves the content and reports the skill as not found.

Every lookup — the skill directory itself included — compares exact on-disk names from
`ReadDir`, never path probes, so case-insensitive filesystems (APFS, NTFS) cannot widen a
"name match" into claiming a user's `Uloop-Sample/`, `skill.md`, or `References/`.

Two deliberate carve-outs from the evidence rule:

- Names ending in `.uloop-tmp-<digits>` / `.uloop-backup-<digits>` are uloop's reserved
  artifact namespace (only `os.CreateTemp`/`os.MkdirTemp` mint them) and are cleaned even
  without evidence — except when the name exactly matches a current source-owned entry,
  which is live managed content. Only regular files ever qualify as ignorable debris.
- Target mode (`.claude/`, `.agents/`, ...) keeps plain name-based ownership over
  `uloop-*` directories. Those directories are uloop's designated output location and the
  `uloop-` prefix is a namespace claim; user edits inside a `uloop-*` skill directory are
  explicitly out of scope (accepted 2026-08-24).

## Context

Dir mode serves an external skill store: a directory shared by hand-authored skills, other
tools, and potentially several Unity projects' uloop installs. Skill names are not confined
to the first-party `uloop-*` set — custom-command skills carry user-chosen names — so no
prefix convention reserves `<path>/<skill-name>/` for uloop the way it does in target mode.

The initial dir-mode implementation ported target mode's premise ("name match = uloop's")
into that shared store. Review round 7 (2026-08) built the branch binary and reproduced
real data loss in the mode's advertised scenario — an external store with foreign files —
tracing six blockers to that single premise, plus case-insensitive path probes claiming
case-variant user entries. The ownership model was rewritten in place to the evidence rule
above; rounds 9–13 then converged (1–4 findings per round, final round zero adopted),
each fix landing as a few lines plus a regression test.

The deciding argument over "a same-named user skill is the user's own fault": for user
responsibility to exist, the user must learn about the collision before the damage.
Name-based deletion discovers the collision only after `os.RemoveAll` succeeded with exit
code 0 — silently, for a person who may not be the one who created the colliding tool.
The conflict report is the mechanism that hands the decision to the user. For a publicly
distributed CLI, one "uloop deleted my skill directory" issue costs more than the
protection does.

Related settled points, so they are not relitigated finding by finding:

- **No manifest in the store.** Ownership derives from current skill sources only; uloop
  writes no metadata into the destination. Consequence (accepted): an entry a future skill
  version removes or renames is left behind rather than cleaned, and evidence gating cannot
  protect a user file that sits exactly inside the reserved artifact namespace.
- **Installed-side read failures are states, not errors.** An unreadable store-side
  `SKILL.md` is a conflict; an unreadable owned entry is `outdated` (self-repair on next
  sync). Source-side read failures propagate as real errors (fail fast).
- **No disabled-tool filtering or deprecated-skill cleanup in dir mode.** The store may
  serve multiple projects; one project's tool settings must not hide or delete skills from
  it. A disabled tool is still refused at invocation time by the Unity side.
- **No cross-process locking.** Concurrent uloop runs against one store are unsynchronized
  everywhere in the CLI; dir mode adds nothing.

## Alternatives rejected

- **Name-based ownership with a documented contract** ("directories named after skills
  belong to uloop"): removes `skills_dir_status.go` (~280 SLOC) and ~20 tests, but makes
  silent, irreversible deletion of user content a specified behavior. Rejected for the
  responsibility argument above.
- **A manifest file in the store**: would make ownership exact, but contradicts the
  no-uloop-metadata-in-store trade-off and still breaks for stores edited by other tools.
- **Evidence-gating the artifact namespace too**: cleanup would merely be delayed until the
  first successful install creates evidence, so the same file is deleted one command later;
  gating adds inconsistency without restoring the guarantee.

## Reversal condition

Reopen this decision only if dir mode gains a store-level metadata channel (for example a
store that is itself uloop-managed end to end), which would make exact ownership tracking
possible without polluting shared stores — or if the evidence rules demonstrably block a
mainstream workflow that conflict reporting cannot resolve manually. Rewriting to
name-based ownership because the evidence code looks large is not a reversal condition;
the status logic is contained in `skills_dir_status.go` with one entry point
(`getDirSkillState`), and the regression tests in `skills_dir_test.go` encode each rule
above.
