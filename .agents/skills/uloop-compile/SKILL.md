---
name: uloop-compile
toolName: compile
description: "Compile the Unity project and report errors/warnings. Use after C# edits or when a full Domain Reload compile is needed."
---

# uloop compile

Execute Unity project compilation.

## Usage

```bash
uloop compile [--force-recompile] [--no-wait-for-domain-reload]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--force-recompile` | boolean | `false` | Force full recompilation (triggers Domain Reload) |
| `--no-wait-for-domain-reload` | boolean | `false` | Return before Domain Reload completion |

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# Check compilation
uloop compile

# Force full recompilation and wait for Domain Reload completion
uloop compile --force-recompile

# Start compilation without waiting for Domain Reload completion
uloop compile --no-wait-for-domain-reload
```

## Output

Returns JSON:
- `Success`: boolean
- `ErrorCount`: number
- `WarningCount`: number

## Troubleshooting

Diagnose the failure mode before retrying.

**Stale recovery state** (CLI hangs or shows recovery/startup state while Unity Editor *is* running):

```bash
uloop fix
```

This removes stale Unity CLI Loop readiness state files from the Unity project's Temp directory. Then retry `uloop compile`.

**Unity Editor not running** (CLI returns a connection failure and no Unity process is alive):

```bash
uloop launch
```

`uloop launch` auto-detects the project at the current working directory and opens it in the matching Unity Editor version. After Unity finishes launching, retry `uloop compile`.
