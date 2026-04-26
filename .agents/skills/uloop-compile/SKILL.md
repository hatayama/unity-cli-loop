---
name: uloop-compile
description: "Compile Unity project and report errors/warnings. Use when you need to: (1) Verify code compiles after C# file edits, (2) Check for compile errors before testing, (3) Force full recompilation with Domain Reload. Returns error and warning counts."
---

# uloop compile

Execute Unity project compilation.

## Usage

```bash
uloop compile [--force-recompile] [--wait-for-domain-reload]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--force-recompile` | boolean | `false` | Force full recompilation (triggers Domain Reload) |
| `--wait-for-domain-reload` | boolean | `false` | Wait until Domain Reload completes before returning |

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# Check compilation
uloop compile

# Force full recompilation
uloop compile --force-recompile

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
