---
name: v3-cli-invocation-migration
description: Migrate uloop V2 CLI invocations in agent skills, Markdown, POSIX shell scripts, and PowerShell scripts to V3 syntax. Use when updating third-party or first-party uloop command examples, shell automation, or SKILL.md files that use V2-style boolean arguments, renamed first-party options, or removed commands.
---

# V3 CLI Invocation Migration

Use this skill to update V2-era `uloop` CLI invocations to V3 syntax in agent-facing docs and automation.

## Workflow

1. Read `references/first-party-v2-to-v3.md` before editing.
2. Search the repository for `uloop` invocations and the V2 names listed in the reference.
3. Inspect nearby context before every edit. Search hits are candidates, not proof.
4. Edit only files that clearly contain V2 `uloop` command syntax.
5. Repeat the searches after editing and report any remaining V2 candidates with the reason each one was left unchanged.

Prefer `rg` for searches when available. If `rg` is unavailable, use the best available project search tool. Candidate discovery should be repository search plus context inspection, not a generated candidate list.

## Editing Rules

- Convert V2 boolean arguments from `--flag true` or `--flag=false` syntax to V3 flag syntax.
- For third-party tools, check the tool's current schema or documentation before deciding whether `false` means removal, a `--no-*` flag, or a renamed option.
- For first-party tools, use the reference table. `compile` and `run-tests` have special renamed negative flags.
- Do not edit generated installed skills under `.agents`, `.claude`, `.codex`, `.cursor`, `.gemini`, `.windsurf`, `.agent`, or similar target folders unless the user explicitly asks to migrate installed copies.
- Do not edit Markdown C# snippets, enum/member references, ordinary DTO/property access, regex match properties, or non-`uloop` JSON.
- Do not change protocol versions, release versions, package names, assembly names, or public extension identifiers as part of this migration.
- Keep edits local to the command or parser being migrated.

## Required Search Passes

- `uloop` command lines and examples.
- Boolean-looking CLI options: `--* true`, `--*=true`, `--* false`, `--*=false`.
- First-party renamed options from the reference, including bare flags.
- Removed commands: `get-project-info` and `get-version`.
