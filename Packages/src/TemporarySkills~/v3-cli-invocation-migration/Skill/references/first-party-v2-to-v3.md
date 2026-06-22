# First-Party V2 to V3 CLI Migration

Use this reference as the canonical migration map. Search results are only candidates. Edit a match only after the surrounding context proves it is a V2 `uloop` invocation.

## Search Checklist

Prefer `rg` when available, but any repository search tool is acceptable.

- Search `uloop` first and inspect command examples, shell scripts, PowerShell scripts, and agent skills.
- Search boolean-looking CLI syntax: `--` plus nearby `true` or `false`, including `--flag true`, `--flag=false`, and inline Markdown command examples.
- Search renamed first-party option names: `wait-for-domain-reload`, `reload-external-scene-changes`, `force-recompile`, `save-before-run`, `show-overlay`, `include-components`, `include-inactive`, and `compile-only`.
- Search removed first-party commands: `get-project-info` and `get-version`.
- Skip generated installed skill copies under `.agents`, `.claude`, `.codex`, `.cursor`, `.gemini`, `.windsurf`, `.agent`, or equivalent target folders unless the user explicitly asks to migrate installed copies.

## Boolean Argument Rules

| V2 form | V3 form |
| --- | --- |
| `--flag true` | `--flag` when the V3 option is a positive default-false boolean |
| `--flag=false` | remove the option when the V3 default is already false |
| `--flag true` | remove the option when the V3 default is already true |
| `--flag false` | use the V3 negative option when the V3 default is true |

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
| `uloop get-project-info` | Replace manually based on caller intent. Do not guess from the command name alone. |
| `uloop get-version` | Replace manually based on caller intent. Do not guess from the command name alone. |
