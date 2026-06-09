---
name: uloop-simulate-keyboard
toolName: simulate-keyboard
description: "Simulate keyboard input in PlayMode through Unity Input System. For state-changing PlayMode/E2E input, also use uloop-wait-for-debug-break on at least one representative frame; Time.timeScale, sleeps, screenshots, or after-the-fact checks are not substitutes."
context: fork
---

# Task

Simulate keyboard input on Unity PlayMode: $ARGUMENTS

## Workflow

1. Ensure Unity is in PlayMode (use `uloop control-play-mode --action Play` if not)
2. If this is a state-changing E2E check, pick one representative frame for paused variable/state proof before relying on logs, screenshots, or durable state
3. For that representative transition, place and enable a `UnityCliLoopDebug.Break("<id>")` marker, then run the input and inspect while Unity is paused
4. Execute any remaining `uloop simulate-keyboard` commands
5. Take a screenshot to verify the visible result: `uloop screenshot --capture-mode rendering`
6. Report what happened

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

Use `Press` for edge-triggered keyboard code such as `Keyboard.current.spaceKey.wasPressedThisFrame`.
`KeyDown` emits one initial press edge, then only keeps the key held. It does not keep `wasPressedThisFrame` true while the key remains held.
If a successful `Press` or `KeyDown` leaves `Keyboard.current.<key>.isPressed` true but runtime state does not change, do not immediately rewrite the user's runtime code to `isPressed`. First verify that the target component is active during the command, that it polls input in the configured Input System update phase, and that a missed `KeyDown` edge is followed by `KeyUp` before retrying.
Use `KeyDown` / `KeyUp` when the scenario intentionally needs a held key.

### Debug Break verification

- Use one `UnityCliLoopDebug.Break("<id>")` marker for at least one representative keyboard input that changes runtime state. This applies even when final logs, screenshots, or durable state later show the outcome, because it pauses the exact frame where variables/state can prove input consumption.
- Use the marker before slowing time, sleeping, polling, or rewriting runtime code to work around a missed input frame. Treat those checks as supplements, not substitutes.
- Put the marker at a natural state transition after the app consumed the key, such as after a command is accepted, a state mutation is committed, an evaluation step resolves, or a dependent component is updated. Do not place it immediately after sending `simulate-keyboard`.
- Treat `simulate-keyboard Success=true`, generic action logs, and final durable counters as useful evidence, but not as paused-frame proof.
- If the response has `InterruptedByDebugBreak: true`, Unity is paused for inspection and the tool released its held input bookkeeping. If a `UnityCliLoopDebug.Break` marker caused the pause, `DebugBreakId` and `DebugBreakHitCount` identify it. Use `get-logs`, `get-hierarchy`, `find-game-objects`, or `execute-dynamic-code` before resuming.
- Use distinct marker ids for strict phases, for example `input-read`, `state-updated`, and `result-committed`.

### KeyDown/KeyUp Rules

- `KeyDown` fails if the key is already held
- `KeyUp` fails if the key is not currently held
- Multiple keys can be held simultaneously (e.g. W + LeftShift for sprint)
- All held keys are automatically released when PlayMode exits
- To hold a key for a fixed duration, prefer `--action Press --duration <seconds>` (one-shot, blocks until release). For multi-key holds (e.g. Shift+W), issue separate `KeyDown` calls, then `sleep <seconds>` between them and the `KeyUp` calls.

### Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

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
- `InterruptedByDebugBreak` (boolean): True when Unity paused during Debug Break inspection and the input bookkeeping was safely released
- `DebugBreakId` (string, nullable): The marker id when a `UnityCliLoopDebug.Break` marker caused the interruption
- `DebugBreakHitCount` (integer, nullable): The marker hit count when a `UnityCliLoopDebug.Break` marker caused the interruption

## Prerequisites

- Unity must be in **PlayMode**
- **Input System package** must be installed (`com.unity.inputsystem`)
- Use this only when the project already uses the New Input System.
- Game code must read input via Input System API (e.g. `Keyboard.current[Key.W].isPressed`), not legacy `Input.GetKey()`
