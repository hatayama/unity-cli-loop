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

## Output

Returns JSON:

- `Success`: boolean or null
- `ErrorCount`: number or null
- `WarningCount`: number or null
- `Message`: string
