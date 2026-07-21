---
name: uloop-simulate-keyboard
toolName: simulate-keyboard
description: "Simulate keyboard input in PlayMode through Unity Input System. Use for key presses, holds, releases, and game controls such as WASD or Space."
---

# Task

Simulate keyboard input on Unity PlayMode.

## Workflow

1. Ensure Unity is in PlayMode (use `uloop control-play-mode --action Play` if not)
2. Execute the needed `uloop simulate-keyboard` commands
3. Inspect the result with the lightest useful evidence: runtime state, logs, or a screenshot
4. If exact-frame proof would reduce uncertainty, treat Pause Point inspection as an optional follow-up using the section below
5. Report what happened and which evidence was used

## Tool Reference

```bash
uloop simulate-keyboard --action <action> --key <key> [options]
```

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--action` | enum | `Press` | `Press`, `KeyDown`, `KeyUp` |
| `--key` | string | (required) | Key name matching Input System Key enum (e.g. `W`, `Space`, `LeftShift`, `A`, `Enter`). Case-insensitive. Digit keys use `Digit0`-`Digit9` or `Numpad0`-`Numpad9`, not bare `0`-`9`. |
| `--duration` | number | `0` | Hold duration in seconds for Press action (0 = one-shot tap). Ignored by KeyDown/KeyUp. |

### Actions

| Action | Behavior | Use Case |
|--------|----------|----------|
| `Press` | KeyDown → wait → KeyUp | One-shot tap (jump, use item) |
| `KeyDown` | KeyDown only (held until KeyUp) | Start continuous movement, hold sprint |
| `KeyUp` | KeyUp only (release held key) | Stop movement, release sprint |

There is no separate hold action: to hold a key, use `Press --duration <seconds>` (fixed-time hold) or `KeyDown` followed later by `KeyUp` (open-ended hold).

Use `Press` for edge-triggered keyboard code such as `Keyboard.current.spaceKey.wasPressedThisFrame`.
`KeyDown` emits one initial press edge, then only keeps the key held. It does not keep `wasPressedThisFrame` true while the key remains held.
If a successful `Press` or `KeyDown` leaves `Keyboard.current.<key>.isPressed` true but runtime state does not change, do not immediately rewrite the user's runtime code to `isPressed`. First verify that the target component is active during the command, that it polls input in the configured Input System update phase, and that a missed `KeyDown` edge is followed by `KeyUp` before retrying.

### Pause Point Inspection (Standard for E2E)

For standard frame proof when this input drives a state transition, follow the `uloop-pause-point` skill — it covers line placement and interruption semantics. Tool-specific note: if `InterruptedByPausePoint: true`, Unity is paused and input bookkeeping was safely released; `PressEdgeObserved` is still reported on pause-point interruptions. Interruption detection covers the whole press lifetime: a pause landing while `Press` is holding the key (during the duration wait) also returns promptly with `InterruptedByPausePoint: true`, and the pause takes precedence even when the requested duration had already elapsed — treat such a response as the pause reporting in, not as a delivery failure. Clear inspection-only pause points (`uloop clear-pause-point --all`) before final validation.

### KeyDown/KeyUp Rules

- `KeyDown` fails if the key is already held
- `KeyUp` fails if the key is not currently held
- Multiple keys can be held simultaneously (e.g. W + LeftShift for sprint)
- All held keys are automatically released when PlayMode exits
- To hold a key for a fixed duration, prefer `--action Press --duration <seconds>` (one-shot, blocks until release). For multi-key holds (e.g. Shift+W), issue separate `KeyDown` calls, then `sleep <seconds>` between them and the `KeyUp` calls.

## Examples

```bash
# One-shot key press
uloop simulate-keyboard --action Press --key W

# One-shot action key
uloop simulate-keyboard --action Press --key Space

# Hold a key for 2 seconds
uloop simulate-keyboard --action Press --key W --duration 2.0

# Hold two keys, then release them
uloop simulate-keyboard --action KeyDown --key LeftShift
uloop simulate-keyboard --action KeyDown --key W
uloop screenshot --capture-mode rendering
uloop simulate-keyboard --action KeyUp --key W
uloop simulate-keyboard --action KeyUp --key LeftShift
```

## Output

Returns JSON with:

- `Success` (boolean): Whether the action succeeded (e.g. `KeyDown` on a not-yet-held key, `KeyUp` on a currently-held key, or `Press` round-trip)
- `Message` (string): Description of what happened or why it failed
- `Action` (string): The `--action` value that was applied (`Press`, `KeyDown`, or `KeyUp`)
- `KeyName` (string, nullable): The key that was acted on; may be `null` when the action could not resolve a key
- `InterruptedByPausePoint` / `PausePointId` / `PausePointHitCount` / `PausePointHits`: Pause-point interruption info (all nullable except the boolean). `PausePointHits` lists every marker hit during this input in hit order; `PausePointId` only names the latest one. See the Pause Point Inspection section above
- `PressEdgeObserved` (boolean, nullable): For `Press` and `KeyDown`, whether the press edge (`wasPressedThisFrame`) was actually visible inside a gameplay input update. `false` means the CLI succeeded but gameplay polling most likely missed the edge (e.g. the press was consumed by an editor-only input update) — retry the input or verify with a focused log instead of trusting `Success` alone. `null` only for `KeyUp` and for timed-out responses; pause-point interruptions still report the observed value. When a single-shot pause point is armed, do not blindly retry on `PressEdgeObserved=false` — the input may still have registered late. Check `pause-point-status` for a hit first, so a retry does not consume a re-enabled marker or double-fire the scenario
- `PressEdgeConsumedByUpdateType` / `PressEdgeAnyDynamicUpdateObserved` / `PressEdgeKeyAlreadyPressedBeforeQueue` (nullable): Diagnostics populated only when `PressEdgeObserved` is `false` (all `null` when the edge was observed). Read them before retrying: `PressEdgeKeyAlreadyPressedBeforeQueue=true` means the key was already held so no press transition could occur — release it with `KeyUp` before pressing again; `PressEdgeConsumedByUpdateType` naming a non-`Dynamic` update type (for example `Editor`) means an editor-side update consumed the edge before gameplay polling could see it; `PressEdgeAnyDynamicUpdateObserved=false` means no gameplay input update ran at all during the press window, so check that PlayMode is running and unpaused rather than retrying blindly. `Message` carries the same diagnosis as text

## Prerequisites

- Unity must be in **PlayMode**
- **Input System package** (`com.unity.inputsystem`) must be installed; this tool only works with the New Input System.
- Game code must read input via Input System API (e.g. `Keyboard.current[Key.W].isPressed`), not legacy `Input.GetKey()`
