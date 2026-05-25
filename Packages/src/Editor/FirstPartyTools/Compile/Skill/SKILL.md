---
name: uloop-compile
toolName: compile
description: "Compile the Unity project and report errors/warnings. Use after C# edits."
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
| `--force-recompile` | flag | - | Use for broader validation, including warnings hidden by other asmdefs; much slower than normal compile |
| `--no-wait-for-domain-reload` | flag | - | Return before Domain Reload completion |

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
```

## Output

Returns JSON:
- `Success`: boolean
- `ErrorCount`: number
- `WarningCount`: number
