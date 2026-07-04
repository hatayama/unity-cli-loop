---
name: uloop-hello-world
description: "Sample hello world tool via uloop CLI. Use when you need to test the MCP tool system or see an example of custom tool implementation."
---

# npx --yes uloop-cli@2.2.0 hello-world

Personalized hello world tool with multi-language support.

## Usage

```bash
npx --yes uloop-cli@2.2.0 hello-world [options]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--name` | string | `World` | Name to greet |
| `--language` | string | `english` | Language for greeting: `english`, `japanese`, `spanish`, `french` |
| `--include-timestamp` | boolean | `true` | Whether to include timestamp in response |

## Examples

```bash
# Default greeting
npx --yes uloop-cli@2.2.0 hello-world

# Greet with custom name
npx --yes uloop-cli@2.2.0 hello-world --name "Alice"

# Japanese greeting
npx --yes uloop-cli@2.2.0 hello-world --name "太郎" --language japanese

# Spanish greeting without timestamp
npx --yes uloop-cli@2.2.0 hello-world --name "Carlos" --language spanish --include-timestamp false
```

## Output

Returns JSON with:
- `Message`: The greeting message
- `Language`: Language used for greeting
- `Timestamp`: Current timestamp (if enabled)

## Notes

This is a sample custom tool demonstrating:
- Type-safe parameter handling with Schema
- Enum parameters for language selection
- Boolean flag parameters
- Multi-language support
