---
name: uloop-wait-for-debug-break
description: "Use this as a quick breakpoint substitute for Unity PlayMode/E2E when uloop-simulated input changes gameplay state. Add a marker, run wait-for-debug-break, and inspect paused variables/state; simulate-* success or InterruptedByDebugBreak alone is not proof."
---

## When To Use

- Use this when a state transition is transient, frame-specific, or hard to prove after the fact.
- Use this during uloop interaction or endurance validation when simulated input drives a core gameplay transition, even if durable state, logs, or screenshots can later confirm the final result.
- Good pause points include input consumed, jump velocity applied, hard drop locked, block placed, collision resolved, damage applied, lives decremented, or game over entered.
- Treat the pause like a lightweight breakpoint for one important transition: combine nearby debug logs with paused-frame inspection to confirm the variables and component state at that point.
- Do not treat `simulate-* Success=true`, generic action logs, sleeps/retries, testing-only counters, or `Time.timeScale` changes as paused-frame proof.
- Skip this for ordinary persistent-state checks when you are not validating input delivery, event ordering, or transition-frame fidelity.

## Quick Check Template

Use this small loop for one transition you care about:

1. Put `UnityCliLoopDebug.Break("player-jumped")` at the natural transition point.
2. Compile, enter PlayMode, then enable the marker with `uloop enable-debug-break --id player-jumped --timeout-seconds 30`.
3. Trigger the action with a `simulate-*` command.
4. Run `uloop wait-for-debug-break --id player-jumped --timeout-seconds 30`, even if the trigger command already returned `InterruptedByDebugBreak=true`.
5. While Unity is paused, capture focused evidence with `uloop execute-dynamic-code`, `uloop get-logs`, and one screenshot.
6. Clear the marker or stop PlayMode before moving on.

## Workflow

1. Add a marker at the state you want to inspect:

```csharp
using io.github.hatayama.UnityCliLoop.Runtime;

UnityCliLoopDebug.Break("player-jumped");
```

2. Compile the project.
3. Enable the marker before triggering the target code path:

```bash
uloop enable-debug-break --id player-jumped --timeout-seconds 30
```

4. Check marker state if needed:

```bash
uloop debug-break-status --id player-jumped
```

5. Trigger the behavior with `simulate-keyboard`, `simulate-mouse-input`, UI interaction, dynamic code, or similar commands.
6. Wait for the marker:

```bash
uloop wait-for-debug-break --id player-jumped --timeout-seconds 30
```

If this command times out, the marker line was not reached while the command waited. Inspect `error.details.status`, `hitCount`, `isPlaying`, `isPaused`, `elapsedSinceEnabledMilliseconds`, and `remainingMilliseconds` to distinguish input not being consumed, gameplay conditions not being met, an id mismatch, or Unity already being paused. `elapsedSinceEnabledMilliseconds` is measured from `enable-debug-break`, not from `wait-for-debug-break`.

7. While Unity is paused, inspect state with `uloop get-logs`, `uloop get-hierarchy`, `uloop find-game-objects`, screenshots, or `uloop execute-dynamic-code`. Add focused debug logs before the marker when local variables must be captured.
8. Clear the marker if you stop waiting:

```bash
uloop clear-debug-break --id player-jumped
```

## Marker Placement

- Prefer natural gameplay points or state-transition points after input has been consumed, such as after jump velocity or state changes, physics contact, or damage application.
- For frame-specific bugs, place the marker on the suspicious state branch or immediately after the state mutation you need to freeze.
- To avoid Domain Reload loss or tool Busy states, enable markers after Play Mode is running, and prefer checkpoints reached after the triggering input command can return.
- Avoid placing the marker immediately after issuing simulated input unless that exact input handling line is the state you need to inspect. Immediate markers can interrupt the input command before the resulting gameplay state settles.
- Use separate ids for strict phases, for example `jump-input-read`, `jump-velocity-applied`, and `jump-landed`, instead of reusing one broad marker.

## Safety

- Code in a custom asmdef must reference `UnityCLILoop.PausePoints.Runtime` to use `UnityCliLoopDebug.Break`.
- Do not pass side-effect expressions as the id argument. Use stable string ids.
- This feature does not collect logs or state snapshots. Use existing inspection commands after Unity pauses.
- If `enable-debug-break` warns about Domain Reload before PlayMode, the marker may be cleared when entering PlayMode. Domain Reload disabled is suitable for this workflow; otherwise enable it again after PlayMode starts.
