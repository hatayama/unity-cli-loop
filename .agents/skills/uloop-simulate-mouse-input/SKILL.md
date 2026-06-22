---
name: uloop-simulate-mouse-input
toolName: simulate-mouse-input
description: "Simulate Mouse.current input in PlayMode through Unity Input System. Use for gameplay mouse clicks, held button input, movement delta, or scroll. Use simulate-mouse-ui for UI."
context: fork
---

# Task

Simulate mouse input via Input System in Unity PlayMode.

## Workflow

1. Ensure Unity is in PlayMode (use `uloop control-play-mode --action Play` if not)
2. For Click/LongPress: determine the target screen position (use `uloop screenshot` to find coordinates)
3. Execute the needed `uloop simulate-mouse-input` commands
4. Inspect the result with the lightest useful evidence: runtime state, logs, or a screenshot
5. When this input verifies a state transition, use Pause Point inspection from the section below as the standard frame proof
6. Report what happened and which evidence was used

## Tool Reference

```bash
uloop simulate-mouse-input --action <action> [options]
```

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--action` | enum | `Click` | `Click`, `LongPress`, `MoveDelta`, `SmoothDelta`, `Scroll` |
| `--x` | number | `0` | Target X position (origin: top-left). Used by Click and LongPress. |
| `--y` | number | `0` | Target Y position (origin: top-left). Used by Click and LongPress. |
| `--button` | enum | `Left` | Mouse button: `Left`, `Right`, `Middle`. Used by Click and LongPress. |
| `--duration` | number | `0` | Hold duration for LongPress, or interpolation duration for SmoothDelta (seconds). For Click, 0 = one-shot tap. |
| `--delta-x` | number | `0` | Delta X in pixels for MoveDelta/SmoothDelta. Positive = right. |
| `--delta-y` | number | `0` | Delta Y in pixels for MoveDelta/SmoothDelta. Positive = up. |
| `--scroll-x` | number | `0` | Horizontal scroll delta for Scroll action. |
| `--scroll-y` | number | `0` | Vertical scroll delta for Scroll action. Typically 120 per notch. |

### Actions

| Action | What it injects | Description |
|--------|----------------|-------------|
| `Click` | Mouse.current button press → release | Inject a button click so runtime logic detects `wasPressedThisFrame` |
| `LongPress` | Mouse.current button press → hold → release | Hold a button for `--duration` seconds |
| `MoveDelta` | Mouse.current.delta | Inject mouse movement delta one-shot |
| `SmoothDelta` | Mouse.current.delta (per-frame) | Inject mouse delta smoothly over `--duration` seconds (human-like camera pan) |
| `Scroll` | Mouse.current.scroll | Inject scroll wheel input |

### Pause Point Inspection (Standard for E2E)

For standard frame proof when this input drives a state transition, follow the `uloop-wait-for-pause-point` skill. Place markers after the app consumed the mouse input, not immediately after `simulate-mouse-input`.

- If `InterruptedByPausePoint: true`, Unity is paused and input bookkeeping was released. `PausePointId` and `PausePointHitCount` identify the marker.
- Remove temporary pause-point/log instrumentation before final validation when it was added only for inspection.

### Global Options (optional)

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## When to use this vs simulate-mouse-ui

All rows below assume the New Input System is installed.

| Scenario | Tool |
|----------|------|
| Click a Unity UI Button (IPointerClickHandler) | `simulate-mouse-ui` |
| Runtime logic reads `Mouse.current.leftButton` | `simulate-mouse-input` |
| Runtime logic reads right-click | `simulate-mouse-input --button Right` |
| Drag a UI slider | `simulate-mouse-ui --action Drag` |
| Runtime logic reads `Mouse.current.delta` | `simulate-mouse-input --action MoveDelta` |
| Runtime logic reads `Mouse.current.scroll` | `simulate-mouse-input --action Scroll` |

## Examples

```bash
# Left-click at screen center for runtime input
uloop simulate-mouse-input --action Click --x 400 --y 300

# Right-click at screen center
uloop simulate-mouse-input --action Click --x 400 --y 300 --button Right

# Hold left-click for 2 seconds
uloop simulate-mouse-input --action LongPress --x 400 --y 300 --duration 2.0

# Send a one-shot mouse delta
uloop simulate-mouse-input --action MoveDelta --delta-x 100 --delta-y 0

# Scroll up
uloop simulate-mouse-input --action Scroll --scroll-y 120

# Scroll down
uloop simulate-mouse-input --action Scroll --scroll-y -120

# Smooth camera pan right over 0.5 seconds
uloop simulate-mouse-input --action SmoothDelta --delta-x 300 --delta-y 0 --duration 0.5
```

## Prerequisites

- Unity must be in **PlayMode**
- **Input System package** (`com.unity.inputsystem`) must be installed; this tool only works with the New Input System.
- Game code must read input via Input System API (e.g. `Mouse.current.leftButton.wasPressedThisFrame`)

## Output

Returns JSON with:

- `Success`: Whether the operation succeeded
- `Message`: Status message
- `Action`: Echoes which action was executed (`Click`, `LongPress`, `MoveDelta`, `SmoothDelta`, or `Scroll`)
- `Button`: Which button was used (nullable string; populated for `Click` / `LongPress`, null otherwise)
- `PositionX`: Target X coordinate (nullable float; populated for `Click` / `LongPress`)
- `PositionY`: Target Y coordinate (nullable float; populated for `Click` / `LongPress`)
- `InterruptedByPausePoint`: True when Unity paused during Pause Point inspection and the input bookkeeping was safely released
- `PausePointId`: The id from `UloopPausePoint.Pause("<id>")` when it caused the interruption
- `PausePointHitCount`: The hit count for that `UloopPausePoint.Pause("<id>")`
- `PausePointHits` (array, nullable): Every marker hit during this input as `{Id, HitCount}` entries, in hit order. Read this when one input may trigger several markers; `PausePointId` only names the latest one

Verify visual outcome with a follow-up screenshot.
