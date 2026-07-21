---
name: uloop-simulate-mouse-input
toolName: simulate-mouse-input
description: "Simulate Mouse.current input in PlayMode through Unity Input System. Use for gameplay mouse clicks, held button input, movement delta, or scroll. Use simulate-mouse-ui for UI."
---

# Task

Simulate mouse input via Input System in Unity PlayMode.

## Workflow

1. Ensure Unity is in PlayMode (use `uloop control-play-mode --action Play` if not)
2. For Click/LongPress: determine the target Game View input position from annotated `SimX`/`SimY`, raycast-grid `InputX`/`InputY`, or raw image pixels converted with `ScreenshotToInputFormula`
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
| `--x` | number | `0` | Target X position in Game View pixels (origin: top-left). Used by Click and LongPress. Use `AnnotatedElements[].SimX`, or raw image pixels converted with `ScreenshotToInputFormula`. |
| `--y` | number | `0` | Target Y position in Game View pixels (origin: top-left). Used by Click and LongPress. Use `AnnotatedElements[].SimY`, or raw image pixels converted with `ScreenshotToInputFormula`. |
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

For standard frame proof when this input drives a state transition, follow the `uloop-pause-point` skill — it covers line placement and interruption semantics. Tool-specific note: if `InterruptedByPausePoint: true`, Unity is paused and input bookkeeping was safely released. Clear inspection-only pause points (`uloop clear-pause-point --all`) before final validation.

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
# Left-click at a representative Game View point for runtime input
uloop simulate-mouse-input --action Click --x 400 --y 300

# Right-click at a representative Game View point
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

## Coordinate System

- `--x` / `--y` use **top-left Game View coordinates**.
- Raw image pixels from `uloop screenshot --capture-mode rendering` must be converted with `ScreenshotToInputFormula`.
- `AnnotatedElements[].SimX/SimY` can be passed directly to this tool.
- Do not flip Y in the caller. The tool converts internally for Unity Input System:

```text
unity_x = input_x
unity_y = gameViewHeight - input_y
```

- `Mouse.current.position` uses bottom-left Unity coordinates, so the value read inside Unity may show the converted Y.
- Device Simulator play view is supported. Prefer rendering-mode screenshots for coordinates; they match the simulated device resolution, not Simulator chrome scale.

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
- `PositionX` / `PositionY`: Target top-left Game View coordinates (nullable float; populated for `Click` / `LongPress`)
- `InputCoordinateSystem`: `"top-left-game-view"` for click/long-press coordinates
- `UnityCoordinateSystem`: `"bottom-left-game-view"` for the injected `Mouse.current.position`
- `GameViewWidth` / `GameViewHeight`: Game View size used for conversion
- `InputPositionX` / `InputPositionY`: Coordinates received from the caller
- `InjectedUnityPositionX` / `InjectedUnityPositionY`: Coordinates injected into `Mouse.current.position`
- `CoordinateConversionFormula`: Conversion formula used by the tool
- `InterruptedByPausePoint` / `PausePointId` / `PausePointHitCount` / `PausePointHits`: Pause-point interruption info (all nullable except the boolean). `PausePointHits` lists every marker hit during this input in hit order; `PausePointId` only names the latest one. See the Pause Point Inspection section above

Verify visual outcome with a follow-up screenshot.
