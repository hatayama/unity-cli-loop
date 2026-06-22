---
name: uloop-compile
toolName: compile
description: "Compile the Unity project and report errors/warnings. Use after C# edits."
---

# uloop compile

Execute Unity project compilation.

## Usage

```bash
uloop compile [--force-recompile] [--no-wait-for-domain-reload] [--stop-on-external-scene-changes]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--force-recompile` | flag | - | Use for broader validation, including warnings hidden by other asmdefs; much slower than normal compile |
| `--no-wait-for-domain-reload` | flag | - | Return before Domain Reload completion |
| `--stop-on-external-scene-changes` | flag | - | Stop before compilation if open Scene files changed externally instead of auto-reloading them |

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# Check compilation
uloop compile

# Start compilation without waiting for Domain Reload completion
uloop compile --no-wait-for-domain-reload

# Stop instead of auto-reloading externally changed open Scene files
uloop compile --stop-on-external-scene-changes
```

## Output

Returns JSON:

- `Success`: boolean or null
- `ErrorCount`: number or null
- `WarningCount`: number or null
- `Message`: string
