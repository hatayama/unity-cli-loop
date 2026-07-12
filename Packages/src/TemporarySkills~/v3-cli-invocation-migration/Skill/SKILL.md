---
name: v3-cli-invocation-migration
description: Migrate only uloop V2 CLI option syntax in agent skills, Markdown, POSIX shell scripts, and PowerShell scripts to V3 syntax. Use when updating first-party or third-party uloop command examples that contain V2-style boolean arguments or renamed options.
---

# V3 CLI Invocation Migration

Agent-facing CLI migration candidates live in this skill. Installed-skill cleanup names stay in `SkillTargetInstaller` / `skills.go` `deprecatedSkillNames`.

Use this skill to update V2-era `uloop` CLI option syntax to V3
syntax in agent-facing docs and automation.

## Workflow

1. Read `references/first-party-v2-to-v3.md` before editing.
2. Search the repository for `uloop` invocations and the V2 names listed in the reference.
3. Inspect nearby context before every edit. Search hits are candidates, not proof.
4. Edit only files that clearly contain V2 `uloop` option syntax.
5. Repeat the searches after editing and report any remaining V2
   candidates with the reason each one was left unchanged.
6. After every Required Search Pass reports zero remaining V2 candidates,
   tell the user this temporary skill has finished its job and can be
   removed with `uloop skills uninstall-v3-migration --<target>` (for
   example `--claude` or `--codex`). Do not uninstall the skill yourself.

Prefer `rg` for searches when available. If `rg` is unavailable, use the best available project search tool. Candidate discovery should be repository search plus context inspection, not a generated candidate list.

## Validation Rules

- Verify this migration with targeted search passes and a focused diff review.
- Do not run Markdownlint as required validation for this migration.
- If Markdownlint runs anyway and fails, ignore that result for this task.
  Do not stop, revert, or withhold completion solely because Markdownlint
  reports existing or unrelated Markdown formatting violations.
- Do not fix Markdownlint findings as part of this migration unless the
  finding is directly caused by changing a `uloop` option token.

## Editing Rules

- Only change option tokens inside confirmed `uloop` command invocations.
- Convert V2 boolean arguments from `--flag true` or `--flag=false`
  syntax to V3 flag syntax.
- For third-party tools, check the tool's current schema or documentation
  before deciding whether `false` means removal, a `--no-*` flag, or a
  renamed option.
- For first-party tools, use the reference table. `compile`, `run-tests`,
  `get-hierarchy`, `record-input`, and `replay-input` have special renamed
  negative flags.
- A valid edit is limited to replacing, adding, or removing a `uloop` option
  token and the boolean value attached to that option in the same invocation.
- Preserve surrounding Markdown, shell, and PowerShell formatting. Do not
  reflow prose, wrap lines, reorder arguments, normalize unrelated whitespace,
  trim unrelated trailing spaces, or change indentation.
- If removing an option leaves extra spaces in that same `uloop` invocation,
  collapse only the spaces created by that removal.
- Do not rename commands, replace removed commands, or rewrite command
  structure unless the user explicitly asks for command migration.
- Do not change non-`uloop` command options, even when they have V2-looking
  boolean syntax.
- Do not edit generated installed skills under `.agents`, `.claude`,
  `.codex`, `.cursor`, `.gemini`, `.windsurf`, `.agent`, or similar target
  folders unless the user explicitly asks to migrate installed copies.
- Do not edit Markdown C# snippets, enum/member references, ordinary
  DTO/property access, regex match properties, or non-`uloop` JSON.
- Do not change protocol versions, release versions, package names, assembly
  names, or public extension identifiers as part of this migration.

## Required Search Passes

- `uloop` command lines and examples.
- Boolean-looking CLI options: `--* true`, `--*=true`, `--* false`, `--*=false`.
- First-party renamed options from the reference, including bare flags.
- Removed or renamed commands: `get-project-info`, `get-version`,
  `unity-search`, `execute-menu-item`, `get-menu-items`,
  `get-unity-search-providers`, `get-provider-details`, and `capture-window`
  (renamed to `screenshot`). Report these as out-of-scope command migration
  candidates unless the user explicitly asked to migrate removed commands.
