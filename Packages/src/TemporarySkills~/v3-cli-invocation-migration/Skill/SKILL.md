---
name: v3-cli-invocation-migration
description: Migrate uloop V2 CLI invocations in agent skills, Markdown, POSIX shell scripts, and PowerShell scripts to V3 syntax. Use when updating third-party or first-party uloop command examples, shell automation, SKILL.md files, or JSON output parsing from V2-style boolean arguments, renamed first-party options, or PascalCase response fields.
---

# V3 CLI Invocation Migration

Use this skill to update V2-era `uloop` CLI invocations to V3 syntax in agent-facing docs and automation.

## Workflow

1. Run the detector for the current repository:
   - POSIX shell: `sh scripts/detect-v3-cli-invocation-candidates.sh .`
   - PowerShell: `pwsh -File scripts/detect-v3-cli-invocation-candidates.ps1 -Root .`
2. Read `references/first-party-v2-to-v3.md` before editing first-party tool calls or JSON output parsing.
3. Inspect each reported line with nearby context. Treat detector output as candidates, not proof.
4. Edit only files that clearly contain V2 `uloop` command syntax or V2 output-field parsing.
5. Run the detector again and report remaining candidates with the reason each one was left unchanged.

## Editing Rules

- Convert V2 boolean arguments from `--flag true` or `--flag=false` syntax to V3 flag syntax.
- For third-party tools, check the tool's current schema or documentation before deciding whether `false` means removal, a `--no-*` flag, or a renamed option.
- For first-party tools, use the reference table. `compile` and `run-tests` have special renamed negative flags.
- Convert V2 PascalCase output field reads to V3 camelCase only when the surrounding command is a `uloop` first-party command.
- Do not rewrite generated installed skills under `.agents`, `.claude`, `.codex`, `.cursor`, `.gemini`, or similar generated target folders unless the user explicitly asks to migrate installed copies.
- Do not rely on `rg`, `jq`, Python, or Go being present. The bundled detectors are dependency-light and write no files.
