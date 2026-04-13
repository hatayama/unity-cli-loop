---
name: uloop-focus-window
description: "Bring Unity Editor window to front via uloop CLI. Use when you need to: (1) Focus Unity Editor before capturing screenshots, (2) Ensure Unity window is visible for visual checks, (3) Bring Unity to foreground for user interaction."
---

# uloop focus-window

Bring Unity Editor window to front using OS-level commands.

## Usage

```bash
uloop focus-window
```

## Parameters

None.

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# Focus Unity Editor
uloop focus-window
```

## Output

Returns JSON confirming the window was focused.

## Notes

- **Works even when Unity is busy** (compiling, domain reload, etc.)
- Uses OS-level commands (osascript on macOS, PowerShell on Windows)
- Useful before `uloop capture-unity-window` to ensure the target window is visible
- Brings the main Unity Editor window to the foreground
