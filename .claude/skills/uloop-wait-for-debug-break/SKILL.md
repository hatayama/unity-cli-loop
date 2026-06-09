---
name: uloop-wait-for-debug-break
description: "Use this as a quick breakpoint substitute for Unity PlayMode/E2E when uloop-simulated input changes runtime state. Add a marker, run wait-for-debug-break, and inspect paused variables/state; simulate-* success or InterruptedByDebugBreak alone is not proof."
---

## Quick Check Template

Use this small loop for one transition you care about:

1. Put a marker at the natural transition point:

```csharp
using io.github.hatayama.UnityCliLoop.Runtime;

UnityCliLoopDebug.Break("state-transition-applied");
```

2. Compile, enter PlayMode, then enable the marker:

```bash
uloop enable-debug-break --id state-transition-applied --timeout-seconds 30
```

3. Trigger the action with a `simulate-*` command.
4. Run `uloop wait-for-debug-break --id state-transition-applied --timeout-seconds 30`, even if the trigger command already returned `InterruptedByDebugBreak=true`.
5. While Unity is paused, capture focused evidence with `uloop execute-dynamic-code`, `uloop get-logs`, `uloop get-hierarchy`, `uloop find-game-objects`, and one screenshot.
6. Clear the marker with `uloop clear-debug-break --id state-transition-applied` or stop PlayMode before moving on.

## When To Use

- Use this when a state transition is transient, frame-specific, or difficult to prove after the fact.
- Use this during uloop interaction or endurance validation when simulated input drives a core runtime transition, even if durable state, logs, or screenshots can later confirm the final result.
- Good pause points include after input is consumed, a command is accepted, a state mutation is committed, an evaluation step resolves, a tracked value changes, a UI/domain state syncs, or a success/failure/end condition is entered.
- Treat the pause like a lightweight breakpoint for one important transition: combine nearby debug logs with paused-frame inspection to confirm the variables and component state at that point.
- Do not treat `simulate-* Success=true`, generic action logs, sleeps/retries, testing-only counters, or `Time.timeScale` changes as paused-frame proof.
- Skip this for ordinary persistent-state checks when you are not validating input delivery, event ordering, or transition-frame fidelity.

## Timeout Checks

If this command times out, the marker line was not reached while the command waited. Inspect `error.details.status`, `hitCount`, `isPlaying`, `isPaused`, `elapsedSinceEnabledMilliseconds`, and `remainingMilliseconds` to distinguish input not being consumed, runtime conditions not being met, an id mismatch, or Unity already being paused. `elapsedSinceEnabledMilliseconds` is measured from `enable-debug-break`, not from `wait-for-debug-break`.

Use `uloop debug-break-status --id state-transition-applied` only when you need to confirm the marker is armed or inspect the current hit state. Add focused debug logs before the marker when local variables must be captured.

## Marker Placement

- Prefer natural runtime points after input has been consumed, such as after a command is accepted, a state value changes, an evaluation step resolves, or a dependent component is updated.
- For frame-specific bugs, place the marker on the suspicious state branch or immediately after the state mutation you need to freeze.
- To avoid Domain Reload loss or tool Busy states, enable markers after Play Mode is running, and prefer checkpoints reached after the triggering input command can return.
- Avoid placing the marker immediately after issuing simulated input unless that exact input handling line is the state you need to inspect. Immediate markers can interrupt the input command before the resulting runtime state settles.
- Use separate ids for strict phases, for example `input-read`, `state-updated`, and `result-committed`, instead of reusing one broad marker.

## Safety

- Code in a custom asmdef must reference `UnityCLILoop.PausePoints.Runtime` to use `UnityCliLoopDebug.Break`.
- Do not pass side-effect expressions as the id argument. Use stable string ids.
- This feature does not collect logs or state snapshots. Use existing inspection commands after Unity pauses.
- If `enable-debug-break` warns about Domain Reload before PlayMode, the marker may be cleared when entering PlayMode. Domain Reload disabled is suitable for this workflow; otherwise enable it again after PlayMode starts.
