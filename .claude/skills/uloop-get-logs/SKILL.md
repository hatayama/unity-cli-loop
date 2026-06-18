---
name: uloop-get-logs
toolName: get-logs
description: "Read current Unity Console entries from a running Editor. Use during bug investigation after compile, tests, PlayMode, dynamic code, or immediately after `uloop-wait-for-pause-point`."
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
| `--include-stack-trace` | flag | - | Include stack trace in output |
| `--use-regex` | flag | - | Use regex for search |
| `--search-in-stack-trace` | flag | - | Search within stack trace |

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
- `totalCount` (number): Total logs available before max-count clipping
- `displayedCount` (number): Logs returned in this response (≤ `--max-count`)
- Input filters (`logType`, `maxCount`, `searchText`, `includeStackTrace`) are echoed back in the response
- `logs` (array): Each entry has:
  - `type` (string): `"Error"`, `"Warning"`, or `"Log"`
  - `message` (string): Log message body
  - `stackTrace` (string): Stack trace text. Empty when `--include-stack-trace` is `false`.
