---
name: simulate-mouse-demo
description: "Run the SimulateMouse demo scenario on SimulateMouseDemoScene. Clicks buttons, long-presses, drags boxes, split-drags through waypoints, and operates the virtual pad. Use when the user asks to run the simulate-mouse demo, test mouse simulation, or exercise the demo scene."
context: fork
---

# Task

Run the SimulateMouse demo scenario: $ARGUMENTS

## What

Automate the SimulateMouse demo scene by clicking buttons, one-shot dragging boxes, split-dragging a box through multiple waypoints to the drop zone, and operating the virtual pad with DragStart/DragMove/DragEnd. Exercises Click, Drag, DragStart, DragMove, and DragEnd across all interactive elements.

## When

Use when you need to:
1. Run the simulate-mouse demo to verify click, drag, split-drag, and virtual pad functionality
2. Test mouse simulation on the demo scene after code changes
3. Exercise the demo scene end-to-end (buttons + one-shot drag + split drag + virtual pad)

## How

### Prerequisites

- Unity must be running with **SimulateMouseDemoScene** loaded
- **PlayMode** must be active

If PlayMode is not active, start it with `uloop control-play-mode --action Play` and wait a moment for the scene to initialize.

### Step 1: Discover UI element coordinates

Take an annotated screenshot to get exact coordinates for each interactive element:

```bash
uloop screenshot --capture-mode rendering --annotate-elements
```

From the `AnnotatedElements` array in the response, extract `SimX` and `SimY` for:
- **ClickButton1** — red button
- **ClickButton2** — blue button
- **DropZone** — the drag target area
- **RedBox** — red draggable box
- **GreenBox** — green draggable box
- **BlueBox** — blue draggable box
- **LongPressButton** — purple long-press button

**VirtualPadBackground** is also listed in the `AnnotatedElements` output (Type: "Draggable"). Extract its `SimX` and `SimY` directly — these are used as `<Pad.SimX>` and `<Pad.SimY>` in the command examples below.

### Step 2: Click buttons, one-shot drag, and split drag — chain all in one Bash call

**IMPORTANT**: Chain all commands with `&&` in a **single Bash tool call** to eliminate round-trip latency between operations. The simulate-mouse tool uses a single-pointer model, so commands must run sequentially (not in parallel), but chaining avoids AI round-trip delays.

**Phase 1 — Button clicks**: Alternate ClickButton1 and ClickButton2 four times.

**Phase 1.5 — LongPress**: LongPress on LongPressButton for 3 seconds. The button gradually changes color during the hold. On first activation it turns orange and shows "Activated!"; on second activation it toggles back to purple and shows "Hold 3s".

**Phase 2 — One-shot Drag**: Drag RedBox to DropZone top area (Y offset -80 from center) so it doesn't overlap subsequent drops.

**Phase 3 — Split drag with DragMove**: Use DragStart on GreenBox, then DragMove through 3 waypoints that zigzag across the screen, and finally DragEnd at the DropZone center. Use `--drag-speed 400` for DragMove/DragEnd so the movement is slow enough to observe the cursor overlay and trail line.

Waypoint design (relative to the midpoint between GreenBox and DropZone):
- Waypoint 1: far right, slightly above — tests horizontal sweep
- Waypoint 2: far left, below — tests direction reversal
- Waypoint 3: above DropZone — tests vertical approach
- DragEnd: at DropZone center (Y offset 0)

**Phase 4 — One-shot Drag**: Drag BlueBox to DropZone bottom area (Y offset +80 from center) so it doesn't overlap previous drops.

**Phase 5 — Virtual Pad**: Use DragStart on VirtualPadBackground center, then DragMove through 8 random directions within padRadius (80px from center) to exercise the joystick, and DragEnd back at center. Use `--drag-speed 300` for visible movement. The direction arrow should rotate to follow each DragMove direction.

Waypoint design (offsets from VirtualPadBackground center):
- Move 1: upper-right (+60, -60 in sim coords)
- Move 2: lower-left (-70, +50)
- Move 3: straight up (0, -75)
- Move 4: straight right (+80, 0)
- Move 5: lower-right (+50, +60)
- Move 6: straight left (-80, 0)
- Move 7: upper-left (-55, -65)
- Move 8: straight down (0, +75)
- DragEnd: back to center (0, 0)

```bash
uloop simulate-mouse-ui --action Click --x <ClickButton1.SimX> --y <ClickButton1.SimY> && sleep 0.3 && \
uloop simulate-mouse-ui --action Click --x <ClickButton2.SimX> --y <ClickButton2.SimY> && sleep 0.3 && \
uloop simulate-mouse-ui --action Click --x <ClickButton1.SimX> --y <ClickButton1.SimY> && sleep 0.3 && \
uloop simulate-mouse-ui --action Click --x <ClickButton2.SimX> --y <ClickButton2.SimY> && sleep 0.3 && \
uloop simulate-mouse-ui --action LongPress --x <LongPressButton.SimX> --y <LongPressButton.SimY> --duration 3.0 && sleep 0.3 && \
uloop simulate-mouse-ui --action Drag --from-x <RedBox.SimX> --from-y <RedBox.SimY> --x <RedBox.SimX> --y <DropZone.SimY - 80> --drag-speed 700 && sleep 0.3 && \
uloop simulate-mouse-ui --action DragStart --x <GreenBox.SimX> --y <GreenBox.SimY> && sleep 0.3 && \
uloop simulate-mouse-ui --action DragMove --x <DropZone.SimX + 150> --y <GreenBox.SimY - 50> --drag-speed 400 && sleep 0.3 && \
uloop simulate-mouse-ui --action DragMove --x <DropZone.SimX - 150> --y <DropZone.SimY + 50> --drag-speed 400 && sleep 0.3 && \
uloop simulate-mouse-ui --action DragMove --x <DropZone.SimX> --y <DropZone.SimY - 80> --drag-speed 400 && sleep 0.3 && \
uloop simulate-mouse-ui --action DragEnd --x <DropZone.SimX> --y <DropZone.SimY> --drag-speed 400 && sleep 0.3 && \
uloop simulate-mouse-ui --action Drag --from-x <BlueBox.SimX> --from-y <BlueBox.SimY> --x <BlueBox.SimX> --y <DropZone.SimY + 80> --drag-speed 700 && sleep 0.3 && \
uloop simulate-mouse-ui --action DragStart --x <Pad.SimX> --y <Pad.SimY> && sleep 0.3 && \
uloop simulate-mouse-ui --action DragMove --x <Pad.SimX + 60> --y <Pad.SimY - 60> --drag-speed 300 && sleep 0.4 && \
uloop simulate-mouse-ui --action DragMove --x <Pad.SimX - 70> --y <Pad.SimY + 50> --drag-speed 300 && sleep 0.4 && \
uloop simulate-mouse-ui --action DragMove --x <Pad.SimX> --y <Pad.SimY - 75> --drag-speed 300 && sleep 0.4 && \
uloop simulate-mouse-ui --action DragMove --x <Pad.SimX + 80> --y <Pad.SimY> --drag-speed 300 && sleep 0.4 && \
uloop simulate-mouse-ui --action DragMove --x <Pad.SimX + 50> --y <Pad.SimY + 60> --drag-speed 300 && sleep 0.4 && \
uloop simulate-mouse-ui --action DragMove --x <Pad.SimX - 80> --y <Pad.SimY> --drag-speed 300 && sleep 0.4 && \
uloop simulate-mouse-ui --action DragMove --x <Pad.SimX - 55> --y <Pad.SimY - 65> --drag-speed 300 && sleep 0.4 && \
uloop simulate-mouse-ui --action DragMove --x <Pad.SimX> --y <Pad.SimY + 75> --drag-speed 300 && sleep 0.4 && \
uloop simulate-mouse-ui --action DragEnd --x <Pad.SimX> --y <Pad.SimY> --drag-speed 300
```

### Step 3: Verify results

Take a final screenshot to confirm all boxes reached the drop zone:

```bash
uloop screenshot --capture-mode rendering
```
