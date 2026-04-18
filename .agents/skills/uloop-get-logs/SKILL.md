---
name: uloop-get-logs
description: "Retrieve logs from Unity Console with filtering and search. Use when you need to: (1) Check for errors or warnings after compilation or play mode, (2) Debug issues by searching log messages, (3) Investigate failures with stack traces. Supports filtering by log type, text search, and regex."
---

# uloop get-logs

Retrieve logs from Unity Console.

## Usage

```bash
uloop get-logs [options]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--log-type` | string | `All` | Log type filter: `Error`, `Warning`, `Log`, `All` |
| `--max-count` | integer | `100` | Maximum number of logs to retrieve |
| `--search-text` | string | - | Text to search within logs |
| `--include-stack-trace` | boolean | `false` | Include stack trace in output |
| `--use-regex` | boolean | `false` | Use regex for search |
| `--search-in-stack-trace` | boolean | `false` | Search within stack trace |

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# Get all logs
uloop get-logs

# Get only errors
uloop get-logs --log-type Error

# Search for specific text
uloop get-logs --search-text "NullReference"

# Regex search
uloop get-logs --search-text "Missing.*Component" --use-regex
```

## Output

Returns JSON array of log entries with message, type, and optional stack trace.
