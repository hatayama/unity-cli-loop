---
name: uloop-simulate-keyboard
toolName: simulate-keyboard
description: "Simulate keyboard input in PlayMode through Unity Input System. Use for key presses, holds (via Press --duration or KeyDown/KeyUp), releases, and game controls such as WASD or Space."
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
uloop simulate-keyboard --action <Press|KeyDown|KeyUp> --key <key> [options]
uloop simulate-keyboard --action ReleaseAll
```

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--action` | enum | `Press` | `Press`, `KeyDown`, `KeyUp`, `ReleaseAll` |
| `--key` | string | (required except `ReleaseAll`) | Key name matching Input System Key enum (e.g. `W`, `Space`, `LeftShift`, `A`, `Enter`). Case-insensitive. Digit keys use `Digit0`-`Digit9` or `Numpad0`-`Numpad9`, not bare `0`-`9`. Not used by `ReleaseAll`. |
| `--duration` | number | `0` | Hold duration in seconds for Press action (0 = one-shot tap). Ignored by KeyDown/KeyUp/ReleaseAll. |

### Actions

| Action | Behavior | Use Case |
|--------|----------|----------|
| `Press` | KeyDown → wait → KeyUp | One-shot tap (jump, use item) |
| `KeyDown` | KeyDown only (held until KeyUp) | Start continuous movement, hold sprint |
| `KeyUp` | KeyUp only (release held key) | Stop movement, release sprint |
| `ReleaseAll` | Force-releases every tracked and device-pressed key (bookkeeping and Input System device state) | Recover a clean keyboard state after a pause-point interruption |

There is no separate hold action: to hold a key, use `Press --duration <seconds>` (fixed-time hold) or `KeyDown` followed later by `KeyUp` (open-ended hold).

Use `Press` for edge-triggered keyboard code such as `Keyboard.current.spaceKey.wasPressedThisFrame`.
`KeyDown` emits one initial press edge, then only keeps the key held. It does not keep `wasPressedThisFrame` true while the key remains held.
If a successful `Press` or `KeyDown` leaves `Keyboard.current.<key>.isPressed` true but runtime state does not change, do not immediately rewrite the user's runtime code to `isPressed`. First verify that the target component is active during the command, that it polls input in the configured Input System update phase, and that a missed `KeyDown` edge is followed by `KeyUp` before retrying.

`ReleaseAll` is a recovery action, not part of normal gameplay simulation: after a pause-point interruption, bookkeeping and the Input System device can disagree, or a stale press latch can keep `isPressed` true after resume. `ReleaseAll` forces both back to a clean slate; it works while Unity is still paused and does not clear pause-point captures. For ordinary releases during gameplay simulation, keep using `KeyUp`.

### Pause Point Inspection (Standard for E2E)

For standard frame proof when this input drives a state transition, follow the `uloop-pause-point` skill — it covers line placement and interruption semantics. Tool-specific note: if `InterruptedByPausePoint: true`, Unity is paused and input bookkeeping was safely released; `PressEdgeObserved` is still reported on pause-point interruptions. Interruption detection covers the whole press lifetime: a pause landing while `Press` is holding the key (during the duration wait) also returns promptly with `InterruptedByPausePoint: true`, and the pause takes precedence even when the requested duration had already elapsed — treat such a response as the pause reporting in, not as a delivery failure. Clear inspection-only pause points (`uloop clear-pause-point --all`) before final validation. If a later key action still reports inconsistent state after an interruption, recover with `--action ReleaseAll` instead of retrying `KeyUp`.

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

# Recover a clean keyboard state (works while paused; e.g. after a pause-point interruption)
uloop simulate-keyboard --action ReleaseAll
```

## Output

Returns JSON with:

- `Success` (boolean): Whether the action succeeded (e.g. `KeyDown` on a not-yet-held key, `KeyUp` on a currently-held key, or `Press` round-trip)
- `Message` (string): Description of what happened or why it failed
- `Action` (string): The `--action` value that was applied (`Press`, `KeyDown`, `KeyUp`, or `ReleaseAll`)
- `KeyName` (string, nullable): The key that was acted on; may be `null` when the action could not resolve a key
- `ReleasedKeys` (string list, nullable): Set only for `ReleaseAll`; the key names that were force-released (empty when nothing was held)
- `InterruptedByPausePoint` / `PausePointId` / `PausePointHitCount` / `PausePointHits`: Pause-point interruption info (all nullable except the boolean). `PausePointHits` lists every marker hit during this input in hit order; `PausePointId` only names the latest one. See the Pause Point Inspection section above
- `PressEdgeObserved` (boolean, nullable): For `Press` and `KeyDown`, whether the press edge (`wasPressedThisFrame`) was visible inside a gameplay input update. `false` means the CLI succeeded but gameplay polling most likely missed the edge — verify with a focused log instead of trusting `Success` alone. `null` only for `KeyUp` and timed-out responses; pause-point interruptions still report the observed value. When a single-shot pause point is armed, do not blindly retry on `false`: the input may still have registered late, so check `pause-point-status` for a hit first — a blind retry can consume a re-enabled marker or double-fire the scenario
- `PressEdgeConsumedByUpdateType` / `PressEdgeAnyDynamicUpdateObserved` / `PressEdgeKeyAlreadyPressedBeforeQueue` (nullable): Diagnostics populated only when `PressEdgeObserved` is `false` (all `null` when the edge was observed). `Message` carries the same diagnosis as text. Read them before retrying:

| Diagnostic | Meaning | Next action |
|---|---|---|
| `PressEdgeKeyAlreadyPressedBeforeQueue=true` | The key was already held; no press transition could occur | Release with `KeyUp`, then press again |
| `PressEdgeConsumedByUpdateType` names a non-`Dynamic` update type (e.g. `Editor`) | An editor-side update consumed the edge before gameplay polling saw it | Retry: rerun `Press` directly; for `KeyDown` the key is now held, so `KeyUp` first (a held key rejects a second `KeyDown`) |
| `PressEdgeAnyDynamicUpdateObserved=false` | No gameplay input update ran during the press window | Check that PlayMode is running and unpaused; do not retry blindly |

## Prerequisites

- Unity must be in **PlayMode**. `Press`/`KeyDown`/`KeyUp` additionally require PlayMode to be unpaused; `ReleaseAll` is allowed while paused.
- **Input System package** (`com.unity.inputsystem`) must be installed; this tool only works with the New Input System.
- Game code must read input via Input System API (e.g. `Keyboard.current[Key.W].isPressed`), not legacy `Input.GetKey()`
