---
name: uloop-wait-for-pause-point
description: "Standard paused-frame proof for Unity PlayMode/E2E gameplay verification. Whenever you verify behavior driven by simulate-* input, physics, or UI events, pause at least one representative state transition with a pause point and inspect the frozen frame like an IDE breakpoint. simulate-* Success=true, action logs, screenshots, sleeps/retries, and final durable state supplement but do not replace this paused-frame proof."
---

## Quick Check Template

Use this small loop for one representative frame you care about:

1. Put a focused log and marker at the natural transition point. Log only local/intermediate values that will be hard to inspect later:

```csharp
using UnityEngine;
using io.github.hatayama.UnityCliLoop.Runtime;

Debug.Log($"state-transition-applied localValue={localValue} reason={reason}");
UloopPausePoint.Pause("state-transition-applied");
```

2. Compile, enter PlayMode, then enable the marker:

```bash
uloop enable-pause-point --id state-transition-applied --timeout-seconds 30
```

3. Trigger the action with a `simulate-*` command.
4. Run `uloop wait-for-pause-point --id state-transition-applied --timeout-seconds 30`, even if the trigger command already returned `InterruptedByPausePoint=true`.
5. Before resuming, read the focused log for the same marker id:

```bash
uloop get-logs --search-text state-transition-applied --max-count 20
```

6. While Unity is still paused, capture any additional evidence with `uloop execute-dynamic-code`, `uloop get-hierarchy`, `uloop find-game-objects`, and one screenshot.
7. Clear the marker with `uloop clear-pause-point --id state-transition-applied` or stop PlayMode before moving on.

## When To Use

- Use this as the standard frame proof for state-changing PlayMode/E2E simulated input, physics, or UI transitions.
- Pause at least one representative transition per E2E pass, even if durable state, logs, or screenshots can later confirm the final result.
- Use this before reaching for `Time.timeScale`, sleeps, repeated polling, or after-the-fact `execute-dynamic-code`; those checks can supplement the paused-frame proof, but they are not substitutes.
- If the value you need is a method local, an intermediate calculation, or a branch reason that `execute-dynamic-code` cannot reach, add a focused `Debug.Log` immediately before the marker and read it with `get-logs` while paused. Do not count the breakpoint check complete until the matching log has been read.
- Good pause points include after input is consumed, a command is accepted, a state mutation is committed, an evaluation step resolves, a tracked value changes, a UI/domain state syncs, or a success/failure/end condition is entered.
- Treat the pause like a lightweight breakpoint for one important transition: combine nearby debug logs with paused-frame inspection to confirm the variables and component state at that point.
- Do not treat `simulate-* Success=true`, generic action logs, sleeps/retries, testing-only counters, or `Time.timeScale` changes as paused-frame proof.
- Skip this only for ordinary persistent-state checks when you are not validating simulated input delivery, event ordering, or transition-frame fidelity.

## Timeout Checks

If this command times out, the marker line was not reached while the command waited. Inspect `error.details.status`, `hitCount`, `isPlaying`, `isPaused`, `elapsedSinceEnabledMilliseconds`, and `remainingMilliseconds` to distinguish input not being consumed, runtime conditions not being met, an id mismatch, or Unity already being paused. `elapsedSinceEnabledMilliseconds` is measured from `enable-pause-point`, not from `wait-for-pause-point`.

Use `uloop pause-point-status --id state-transition-applied` only when you need to confirm the marker is armed or inspect the current hit state. Add focused debug logs before the marker when local variables must be captured.

## Marker Placement

- Prefer natural runtime points after input has been consumed, such as after a command is accepted, a state value changes, an evaluation step resolves, or a dependent component is updated.
- For frame-specific bugs, place the marker on the suspicious state branch or immediately after the state mutation you need to freeze.
- To avoid Domain Reload loss or tool Busy states, enable markers after Play Mode is running, and prefer checkpoints reached after the triggering input command can return.
- Avoid placing the marker immediately after issuing simulated input unless that exact input handling line is the state you need to inspect. Immediate markers can interrupt the input command before the resulting runtime state settles.
- Use separate ids for strict phases, for example `input-read`, `state-updated`, and `result-committed`, instead of reusing one broad marker.

## Safety

- Code in a custom asmdef must reference `UnityCLILoop.PausePoints.Runtime` to use `UloopPausePoint.Pause`.
- Do not pass side-effect expressions as the id argument. Use stable string ids.
- This feature does not collect logs or state snapshots. Use existing inspection commands after Unity pauses.
- If `enable-pause-point` warns about Domain Reload before PlayMode, the marker may be cleared when entering PlayMode. Domain Reload disabled is suitable for this workflow; otherwise enable it again after PlayMode starts.
