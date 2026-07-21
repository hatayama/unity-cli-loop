---
name: uloop-simulate-mouse-ui
toolName: simulate-mouse-ui
description: "Simulate PlayMode EventSystem UI mouse actions using screen coordinates. Use for UI clicks, long-presses, or drags from annotated screenshots."
---

# Task

Simulate mouse interaction on Unity PlayMode UI.

## Workflow

1. Ensure Unity is in PlayMode (use `uloop control-play-mode --action Play` if not)
2. Get UI element info: `uloop screenshot --capture-mode rendering --annotate-elements --elements-only`
3. Use the `AnnotatedElements` array to find the target element by `Label`, `Name`, or `Path` (A=frontmost, B=next, ...). Use `Interaction` to distinguish click targets from drag/drop/text targets, then use `SimX`/`SimY` directly as `--x`/`--y` coordinates.
4. Execute the needed `uloop simulate-mouse-ui` commands
5. Inspect the result with the lightest useful evidence: runtime state, logs, or a screenshot
6. When this UI input verifies a state transition, use Pause Point inspection from the section below as the standard frame proof
7. Report what happened and which evidence was used

## Tool Reference

```bash
uloop simulate-mouse-ui --action <action> --x <x> --y <y> [options]
```

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--action` | enum | `Click` | `Click`, `Drag`, `DragStart`, `DragMove`, `DragEnd`, `LongPress` |
| `--x` | number | `0` | Target X position in screen pixels (origin: top-left). For Drag action, this is the destination. |
| `--y` | number | `0` | Target Y position in screen pixels (origin: top-left). For Drag action, this is the destination. |
| `--from-x` | number | `0` | Start X position for Drag action. Drag starts here and moves to x,y. |
| `--from-y` | number | `0` | Start Y position for Drag action. Drag starts here and moves to x,y. |
| `--drag-speed` | number | `2000` | Drag speed in pixels per second (0 for instant). 2000 is fast (default), 200 is slow enough to watch. Applies to Drag, DragMove, and DragEnd actions. |
| `--duration` | number | `0.5` | Hold duration in seconds for LongPress action. |
| `--button` | enum | `Left` | Mouse button. `Click` and `LongPress` support `Left`, `Right`, and `Middle`. Drag actions support `Left` only; other buttons return an error. |
| `--bypass-raycast` | flag | - | For `Click`, `LongPress`, `Drag`, and `DragStart`, bypass EventSystem raycast and dispatch pointer events directly to `--target-path`. Use when a raycast-blocking overlay visually covers the intended target. |
| `--target-path` | string | `""` | Hierarchy path of the target GameObject, for example `Canvas/Panel/Button`. Required when `--bypass-raycast` is used with `Click`, `LongPress`, `Drag`, or `DragStart`; prefer `AnnotatedElements[].Path` from screenshot JSON. |
| `--drop-target-path` | string | `""` | Optional hierarchy path of a drop target for `Drag` or `DragEnd`, for example `Canvas/DropZone`. Use this when the drop zone is also behind a raycast blocker. |

### Actions

| Action | Event Fired | Description |
|--------|-------------|-------------|
| `Click` | PointerDown → PointerUp → PointerClick | Click at (x, y) with the selected `--button` |
| `LongPress` | PointerDown → (hold) → PointerUp | Press and hold at (x, y) for `--duration` seconds, then release. No PointerClick is fired. |
| `Drag` | BeginDrag → Drag×N → EndDrag | One-shot drag from (fromX, fromY) to (x, y) at the specified speed |
| `DragStart` | BeginDrag | Begin drag at (x, y) and hold |
| `DragMove` | Drag×N | Animate from current position to (x, y) at the specified speed |
| `DragEnd` | Drag×N → EndDrag | Animate to (x, y) at the specified speed, then release drag |

### Split Drag Rules

- `DragStart` must be called before `DragMove` or `DragEnd`
- `DragEnd` must be called to release an active drag — failing to call it leaves drag state stuck
- Calling `DragMove` or `DragEnd` without an active drag returns an error

## Coordinate System

- Origin is **top-left** (0, 0)
- All positions are in **screen pixels**
- Get coordinates from `AnnotatedElements` JSON (`SimX`/`SimY`) — do NOT look up GameObject positions
- Clicking or long-pressing on empty space (no UI element) still succeeds with a message indicating no element was hit
- Dragging on empty space (no draggable UI element) returns `Success = false`
- `--bypass-raycast` still uses coordinates for pointer event positions, but chooses the clicked, long-pressed, or dragged GameObject by `--target-path`
- If `--target-path` or `--drop-target-path` matches multiple active GameObjects, the command fails instead of choosing an arbitrary duplicate
- Device Simulator play view is supported. Prefer `uloop screenshot --capture-mode rendering --annotate-elements` for coordinates; they use the simulated device resolution (`Handles.GetMainGameViewSize()` / `Screen`), not the Simulator chrome scale.

## Pause Point Inspection (Standard for E2E)

For standard frame proof when this UI input drives a state transition, follow the `uloop-pause-point` skill — it covers line placement and interruption semantics. Tool-specific note: if `InterruptedByPausePoint: true`, `Success: true` only means the command ended cleanly; read `Message` first — it states whether the pointer event was already dispatched before the pause (only the overlay animation was interrupted) or the pause landed first (no pointer event was fired). Clear inspection-only pause points (`uloop clear-pause-point --all`) before final validation.

## Examples

```bash
# Click a button at screen position
uloop simulate-mouse-ui --action Click --x 400 --y 300

# Force-click a button behind a raycast blocker by path
uloop simulate-mouse-ui --action Click --x 400 --y 300 --bypass-raycast --target-path "Canvas/Panel/Button"

# Force-long-press a button behind a raycast blocker by path
uloop simulate-mouse-ui --action LongPress --x 400 --y 300 --duration 3.0 --bypass-raycast --target-path "Canvas/Panel/Button"

# Force-drag an item behind a raycast blocker by path
uloop simulate-mouse-ui --action Drag --from-x 400 --from-y 300 --x 600 --y 300 --bypass-raycast --target-path "Canvas/Item"

# Force-drag and dispatch Drop to a blocked drop zone
uloop simulate-mouse-ui --action Drag --from-x 400 --from-y 300 --x 600 --y 300 --bypass-raycast --target-path "Canvas/Item" --drop-target-path "Canvas/DropZone"

# Long-press a button for 3 seconds
uloop simulate-mouse-ui --action LongPress --x 400 --y 300 --duration 3.0

# One-shot drag (start to end in one call)
uloop simulate-mouse-ui --action Drag --from-x 400 --from-y 300 --x 600 --y 300

# Slow drag for visual inspection
uloop simulate-mouse-ui --action Drag --from-x 400 --from-y 300 --x 600 --y 300 --drag-speed 200

# Split drag with hold (for inspection between steps)
uloop simulate-mouse-ui --action DragStart --x 400 --y 300
uloop screenshot --window-name Game
uloop simulate-mouse-ui --action DragMove --x 500 --y 300
uloop simulate-mouse-ui --action DragEnd --x 600 --y 300
```

## Prerequisites

- Unity must be in **PlayMode**
- Target scene must have an **EventSystem** GameObject
- UI elements must have a **GraphicRaycaster** on their Canvas
- If you need runtime mouse input rather than UI pointer events, `simulate-mouse-input` assumes the project uses the New Input System; otherwise prefer `execute-dynamic-code`

## Output

Returns JSON with:

- `Success`: Whether the operation succeeded
- `Message`: Status message (e.g. "Hit element: ButtonStart" or "No UI element under (x, y)")
- `Action`: Echoes which action was executed (`Click`, `Drag`, `DragStart`, `DragMove`, `DragEnd`, or `LongPress`)
- `HitGameObjectName`: Name of the topmost UI element under the pointer (nullable string; null if nothing was hit)
- `PositionX`: Target X coordinate that was used
- `PositionY`: Target Y coordinate that was used
- `EndPositionX`: Drag end X coordinate (nullable float; populated for drag actions only)
- `EndPositionY`: Drag end Y coordinate (nullable float; populated for drag actions only)
- `InterruptedByPausePoint` / `PausePointId` / `PausePointHitCount` / `PausePointHits`: Pause-point interruption info (all nullable except the boolean). `PausePointHits` lists every marker hit during this input in hit order; `PausePointId` only names the latest one. See the Pause Point Inspection section above

Verify the visual outcome with a follow-up `uloop screenshot --capture-mode rendering --annotate-elements`.

Note: Click and LongPress on empty space (no UI element) still return `Success = true` with `HitGameObjectName = null`. Drag actions on empty space return `Success = false`.
