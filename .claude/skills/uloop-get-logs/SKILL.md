---
name: uloop-get-logs
description: "Retrieve current Unity Console entries via uloop CLI. Use when you need to: (1) inspect errors, warnings, or logs after compile, tests, PlayMode, or dynamic code execution, (2) search current Console messages or stack traces, (3) confirm whether a recent Unity operation emitted logs. Prefer this over reading Editor.log or Unity log files for normal Console contents; use log files only for startup, crash, freeze, or uloop connection failures."
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

Returns JSON with:
- `TotalCount` (number): Total logs available before max-count clipping
- `DisplayedCount` (number): Logs returned in this response (≤ `--max-count`)
- `LogType` (string): The `--log-type` filter that was applied
- `MaxCount` (number): The `--max-count` cap that was applied
- `SearchText` (string): The `--search-text` filter that was applied (empty when omitted)
- `IncludeStackTrace` (boolean): Whether stack traces are included in `Logs[]`
- `Logs` (array): Each entry has:
  - `Type` (string): `"Error"`, `"Warning"`, or `"Log"`
  - `Message` (string): Log message body
  - `StackTrace` (string): Stack trace text. Empty when `--include-stack-trace` is `false`.
