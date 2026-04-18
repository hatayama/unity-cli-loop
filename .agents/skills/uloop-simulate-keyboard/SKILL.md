---
name: uloop-simulate-keyboard
description: "Simulate keyboard key input in PlayMode via Input System. Use when you need to: (1) Press game control keys like WASD, Space, or Shift during PlayMode, (2) Hold keys down for continuous movement or actions, (3) Combine multiple held keys for complex input like Shift+W for sprint."
context: fork
---

# Task

Simulate keyboard input on Unity PlayMode: $ARGUMENTS

## Workflow

1. Ensure Unity is in PlayMode (use `uloop control-play-mode --action Play` if not)
2. Execute the appropriate `uloop simulate-keyboard` command
3. Take a screenshot to verify the result: `uloop screenshot --capture-mode rendering`
4. Report what happened

## Tool Reference

```bash
uloop simulate-keyboard --action <action> --key <key> [options]
```

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--action` | enum | `Press` | `Press`, `KeyDown`, `KeyUp` |
| `--key` | string | (required) | Key name matching Input System Key enum (e.g. `W`, `Space`, `LeftShift`, `A`, `Enter`). Case-insensitive. |
| `--duration` | number | `0` | Hold duration in seconds for Press action (0 = one-shot tap). Ignored by KeyDown/KeyUp. |

### Actions

| Action | Behavior | Use Case |
|--------|----------|----------|
| `Press` | KeyDown → wait → KeyUp | One-shot tap (jump, use item) |
| `KeyDown` | KeyDown only (held until KeyUp) | Start continuous movement, hold sprint |
| `KeyUp` | KeyUp only (release held key) | Stop movement, release sprint |

### KeyDown/KeyUp Rules

- `KeyDown` fails if the key is already held
- `KeyUp` fails if the key is not currently held
- Multiple keys can be held simultaneously (e.g. W + LeftShift for sprint)
- All held keys are automatically released when PlayMode exits

### Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# One-shot key press (tap W once)
uloop simulate-keyboard --action Press --key W

# Jump (tap Space)
uloop simulate-keyboard --action Press --key Space

# Hold W for 2 seconds (move forward)
uloop simulate-keyboard --action Press --key W --duration 2.0

# Sprint forward (hold Shift + W, then release)
uloop simulate-keyboard --action KeyDown --key LeftShift
uloop simulate-keyboard --action KeyDown --key W
uloop screenshot --capture-mode rendering
uloop simulate-keyboard --action KeyUp --key W
uloop simulate-keyboard --action KeyUp --key LeftShift
```

## Prerequisites

- Unity must be in **PlayMode**
- **Input System package** (`com.unity.inputsystem`) must be installed
- Active Input Handling must be set to **Input System Package (New)** or **Both** in Player Settings
- Game code must read input via Input System API (e.g. `Keyboard.current[Key.W].isPressed`), not legacy `Input.GetKey()`
