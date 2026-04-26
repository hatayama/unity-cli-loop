---
name: uloop-clear-console
description: "Clear all Unity Console log entries. Use when you need to: (1) Clear console before running tests or compilation, (2) Start a fresh debugging session, (3) Remove noisy logs to isolate specific output."
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
| `--add-confirmation-message` | boolean | `false` | Add confirmation message after clearing |

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
- `Success` (boolean): Whether the clear operation succeeded
- `ClearedLogCount` (number): Total number of log entries that were cleared
- `ClearedCounts` (object): Breakdown by log type
  - `ErrorCount` (number): Errors cleared
  - `WarningCount` (number): Warnings cleared
  - `LogCount` (number): Info logs cleared
- `Message` (string): Description of the result; carries the failure summary when the operation fails (e.g. `"Failed to clear console: ..."`)
- `ErrorMessage` (string): Currently always empty for this tool — read `Message` for failure details
