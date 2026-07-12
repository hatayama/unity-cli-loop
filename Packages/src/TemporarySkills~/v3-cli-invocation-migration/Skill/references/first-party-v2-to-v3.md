# First-Party V2 to V3 CLI Migration

Agent-facing CLI migration candidates live here. Installed-skill cleanup names stay in `SkillTargetInstaller` / `skills.go` `deprecatedSkillNames`.

Use this reference as the canonical option migration map. Search results are
only candidates. Edit a match only after the surrounding context proves it is
a V2 `uloop` invocation.

This migration is option-only by default. Do not reformat surrounding text or
scripts, and do not change non-`uloop` commands. A valid edit only replaces,
adds, or removes a `uloop` option token and the boolean value attached to that
option in the same invocation.

Markdownlint is not required validation for this migration. If Markdownlint
runs and reports existing or unrelated formatting issues, ignore that result,
continue the option migration, and report the lint result as non-blocking. Do
not fix Markdownlint findings unless the migration itself created them.

## Search Checklist

Prefer `rg` when available, but any repository search tool is acceptable.

- Search `uloop` first and inspect command examples, shell scripts, PowerShell scripts, and agent skills.
- Search boolean-looking CLI syntax: `--` plus nearby `true` or `false`, including `--flag true`, `--flag=false`, and inline Markdown command examples.
- Search renamed first-party option names: `wait-for-domain-reload`, `reload-external-scene-changes`, `force-recompile`, `save-before-run`, `show-overlay`, `include-components`, `include-inactive`, and `compile-only`.
- Search removed or renamed first-party commands only to report them as
  out-of-scope command migration candidates: `get-project-info`, `get-version`,
  `unity-search`, `execute-menu-item`, `get-menu-items`,
  `get-unity-search-providers`, `get-provider-details`, and `capture-window`.
- Skip generated installed skill copies under `.agents`, `.claude`, `.codex`, `.cursor`, `.gemini`, `.windsurf`, `.agent`, or equivalent target folders unless the user explicitly asks to migrate installed copies.

## Boolean Argument Rules

| V2 form | V3 form |
| --- | --- |
| `--flag true` | `--flag` when the V3 option is a positive default-false boolean |
| `--flag=true` | `--flag` when the V3 option is a positive default-false boolean |
| `--flag=false` | remove the option when the V3 default is already false |
| `--flag false` | remove the option when the V3 default is already false |
| `--flag true` | remove the option when the V3 default is already true |
| `--flag=true` | remove the option when the V3 default is already true |
| `--flag false` | use the V3 negative option when the V3 default is true |
| `--flag=false` | use the V3 negative option when the V3 default is true |

For third-party tools, inspect the current tool schema or docs before choosing the replacement. Do not infer third-party negative flags from first-party conventions.

## Special First-Party Options

| V2 command | V2 option | V3 replacement |
| --- | --- | --- |
| `uloop compile` | `--force-recompile true` | `--force-recompile` |
| `uloop compile` | `--force-recompile false` | remove |
| `uloop compile` | `--wait-for-domain-reload true` or bare `--wait-for-domain-reload` | remove |
| `uloop compile` | `--wait-for-domain-reload false` | `--no-wait-for-domain-reload` |
| `uloop compile` | `--reload-external-scene-changes true` | remove |
| `uloop compile` | `--reload-external-scene-changes false` | `--stop-on-external-scene-changes` |
| `uloop run-tests` | `--save-before-run true` or bare `--save-before-run` | remove |
| `uloop run-tests` | `--save-before-run false` | `--fail-on-unsaved-changes` |
| `uloop record-input` | `--show-overlay true` | remove |
| `uloop record-input` | `--show-overlay false` | `--no-show-overlay` |
| `uloop replay-input` | `--show-overlay true` | remove |
| `uloop replay-input` | `--show-overlay false` | `--no-show-overlay` |
| `uloop get-hierarchy` | `--include-components true` | remove |
| `uloop get-hierarchy` | `--include-components false` | `--no-include-components` |
| `uloop get-hierarchy` | `--include-inactive true` | remove |
| `uloop get-hierarchy` | `--include-inactive false` | `--no-include-inactive` |
| `uloop execute-dynamic-code` | `--compile-only true` | `--compile-only` |
| `uloop execute-dynamic-code` | `--compile-only false` | remove |

## Removed First-Party Commands

| V2 command | V3 handling |
| --- | --- |
| `uloop get-project-info` | Report only unless the user explicitly asks for removed command migration. Do not guess from the command name alone. |
| `uloop get-version` | Report only unless the user explicitly asks for removed command migration. Do not guess from the command name alone. |
| `uloop unity-search` | Report only unless the user explicitly asks for removed command migration. Do not guess from the command name alone. |
| `uloop execute-menu-item` | Report only unless the user explicitly asks for removed command migration. Do not guess from the command name alone. |
| `uloop get-menu-items` | Report only unless the user explicitly asks for removed command migration. Do not guess from the command name alone. |
| `uloop get-unity-search-providers` | Report only unless the user explicitly asks for removed command migration. Do not guess from the command name alone. |
| `uloop get-provider-details` | Report only unless the user explicitly asks for removed command migration. Do not guess from the command name alone. |
| `uloop capture-window` | Renamed to `uloop screenshot`. Report only unless the user explicitly asks for removed command migration. Do not guess from the command name alone. |
