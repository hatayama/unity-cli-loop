---
name: uloop-simulate-mouse-input
description: "Simulate mouse input in PlayMode for gameplay code that reads Unity Input System Mouse.current. Use when you need to: (1) Click or right-click in games that read Mouse.current button state, (2) Inject mouse delta for FPS camera control, (3) Inject scroll wheel for hotbar switching or zoom. Requires PlayMode and the New Input System; for EventSystem UI elements, use simulate-mouse-ui instead."
context: fork
---

# Task

Simulate mouse input via Input System in Unity PlayMode: $ARGUMENTS

## Workflow

1. Ensure Unity is in PlayMode (use `uloop control-play-mode --action Play` if not)
2. For Click/LongPress: determine the target screen position (use `uloop screenshot` to find coordinates)
3. Execute the appropriate `uloop simulate-mouse-input` command
4. Take a screenshot to verify the result: `uloop screenshot --capture-mode rendering`
5. Report what happened

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
| `Click` | Mouse.current button press → release | Inject a button click so game logic detects `wasPressedThisFrame` |
| `LongPress` | Mouse.current button press → hold → release | Hold a button for `--duration` seconds |
| `MoveDelta` | Mouse.current.delta | Inject mouse movement delta one-shot (e.g. for FPS camera look) |
| `SmoothDelta` | Mouse.current.delta (per-frame) | Inject mouse delta smoothly over `--duration` seconds (human-like camera pan) |
| `Scroll` | Mouse.current.scroll | Inject scroll wheel input (e.g. for hotbar or zoom) |

### Global Options (optional)

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |


## When to use this vs simulate-mouse-ui

| Scenario | Tool |
|----------|------|
| Click a Unity UI Button (IPointerClickHandler) | `simulate-mouse-ui` |
| Destroy a block in Minecraft (reads `Mouse.current.leftButton`) | `simulate-mouse-input` when the project uses the New Input System; otherwise prefer `execute-dynamic-code` for a project-specific workaround |
| Place a block with right-click | `simulate-mouse-input --button Right` when the project uses the New Input System |
| Drag a UI slider | `simulate-mouse-ui --action Drag` |
| Look around with mouse (FPS camera) | `simulate-mouse-input --action MoveDelta` when the project uses the New Input System |
| Scroll hotbar slots | `simulate-mouse-input --action Scroll` when the project uses the New Input System |

## Examples

```bash
# Left-click at screen center (for game logic)
uloop simulate-mouse-input --action Click --x 400 --y 300

# Right-click at screen center (e.g. place block)
uloop simulate-mouse-input --action Click --x 400 --y 300 --button Right

# Hold left-click for 2 seconds (e.g. mine block)
uloop simulate-mouse-input --action LongPress --x 400 --y 300 --duration 2.0

# Look right (FPS camera)
uloop simulate-mouse-input --action MoveDelta --delta-x 100 --delta-y 0

# Scroll up (e.g. previous hotbar slot)
uloop simulate-mouse-input --action Scroll --scroll-y 120

# Scroll down (e.g. next hotbar slot)
uloop simulate-mouse-input --action Scroll --scroll-y -120

# Smooth camera pan right over 0.5 seconds
uloop simulate-mouse-input --action SmoothDelta --delta-x 300 --delta-y 0 --duration 0.5
```

## Prerequisites

- Unity must be in **PlayMode**
- **Input System package** must be installed (`com.unity.inputsystem`)
- Game code must read input via Input System API (e.g. `Mouse.current.leftButton.wasPressedThisFrame`)
- If the target project cannot use the New Input System, prefer `execute-dynamic-code` for a project-specific workaround instead of changing project settings just to use this tool

## Output

Returns JSON with:
- `Success`: Whether the operation succeeded
- `Message`: Status message
- `Action`: Echoes which action was executed (`Click`, `LongPress`, `MoveDelta`, `SmoothDelta`, or `Scroll`)
- `Button`: Which button was used (nullable string; populated for `Click` / `LongPress`, null otherwise)
- `PositionX`: Target X coordinate (nullable float; populated for `Click` / `LongPress`)
- `PositionY`: Target Y coordinate (nullable float; populated for `Click` / `LongPress`)

These are the only six fields. There is no `DeltaX`, `DeltaY`, `ScrollX`, `ScrollY`, `Duration`, or hit-element field in the response — only the issued action, button, and target position are echoed back. Verify visual outcome with a follow-up screenshot.
