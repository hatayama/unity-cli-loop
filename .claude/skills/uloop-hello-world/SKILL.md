---
name: uloop-hello-world
description: "Sample uloop hello-world tool. Use to verify custom tool wiring or inspect a minimal tool implementation example."
---

# uloop hello-world

Personalized hello world tool with multi-language support.

## Usage

```bash
uloop hello-world [options]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--name` | string | `World` | Name to greet |
| `--language` | string | `english` | Language for greeting: `english`, `japanese`, `spanish`, `french` |
| `--no-include-timestamp` | flag | - | Exclude timestamp from response |

## Examples

```bash
# Default greeting
uloop hello-world

# Greet with custom name
uloop hello-world --name "Alice"

# Japanese greeting
uloop hello-world --name "太郎" --language japanese

# Spanish greeting without timestamp
uloop hello-world --name "Carlos" --language spanish --no-include-timestamp
```

## Output

Returns JSON with:
- `message`: The greeting message
- `language`: Language used for greeting
- `timestamp`: Current timestamp (if enabled)

## Notes

This is a sample custom tool demonstrating:
- Type-safe parameter handling with Schema
- Enum parameters for language selection
- Value-less flag parameters
- Multi-language support
