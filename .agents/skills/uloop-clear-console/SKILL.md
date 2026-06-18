---
name: uloop-clear-console
toolName: clear-console
description: "Clear Unity Console entries. Use before compile, tests, or debugging when stale logs would hide the current result."
---

# uloop clear-console

Clear Unity console logs.

## Usage

```bash
uloop clear-console [--add-confirmation-message]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--add-confirmation-message` | flag | - | Add confirmation message after clearing |

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# Clear console
uloop clear-console

# Clear with confirmation
uloop clear-console --add-confirmation-message
```

## Output

Returns JSON with:
- `success` (boolean): Whether the clear operation succeeded
- `clearedLogCount` (number): Total number of log entries that were cleared
- `clearedCounts` (object): Breakdown by log type
  - `errorCount` (number): Errors cleared
  - `warningCount` (number): Warnings cleared
  - `logCount` (number): Info logs cleared
- `message` (string): Description of the result. On failure, this field carries the error summary.
