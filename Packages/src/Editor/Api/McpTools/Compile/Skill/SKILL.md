---
name: uloop-compile
description: "Compile the Unity project and report errors/warnings. Use after C# edits or when a full Domain Reload compile is needed."
---

# uloop compile

Execute Unity project compilation.

## Usage

```bash
uloop compile [--force-recompile <true|false>] [--wait-for-domain-reload <true|false>]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--force-recompile` | boolean value | `false` | Force full recompilation (triggers Domain Reload). Rarely needed — see "When to use --force-recompile" below. Pass `true` or `false`; bare flags are not accepted. |
| `--wait-for-domain-reload` | boolean value | `false` | Wait until Domain Reload completes before returning. Pass `true` or `false`; bare flags are not accepted. |

## When to use --force-recompile

Almost never. Unity itself detects changed files — even when they were edited outside the
Editor, a plain `uloop compile` runs every recompilation the changes require. A forced full
recompile can freeze the Editor for a long time on large projects, and with
`--wait-for-domain-reload true` the response crosses a Domain Reload so `Success` comes back
as `null`, making it useless as a verification step. The only legitimate use: surfacing
warnings hidden by other asmdefs with a full build.

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# Check compilation
uloop compile

# Force full recompilation
uloop compile --force-recompile true

# Force recompilation and wait for Domain Reload completion
uloop compile --force-recompile true --wait-for-domain-reload true

# Wait for Domain Reload completion even without force recompilation
uloop compile --force-recompile false --wait-for-domain-reload true
```

## Output

Returns JSON:
- `Success`: boolean
- `ErrorCount`: number
- `WarningCount`: number

## Troubleshooting

Diagnose the failure mode before retrying.

**Stale lock files** (CLI hangs or shows "Unity is busy" while Unity Editor *is* running):

```bash
uloop fix
```

This removes any leftover lock files (`compiling.lock`, `domainreload.lock`, `serverstarting.lock`) from the Unity project's Temp directory. Then retry `uloop compile`.

**Unity Editor not running** (CLI returns a connection failure and no Unity process is alive):

```bash
uloop launch
```

`uloop launch` auto-detects the project at the current working directory and opens it in the matching Unity Editor version. After Unity finishes launching, retry `uloop compile`.
