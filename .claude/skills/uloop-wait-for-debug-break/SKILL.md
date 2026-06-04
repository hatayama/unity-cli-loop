---
name: uloop-wait-for-debug-break
description: "Pause Unity at a named UnityCliLoopDebug.Break marker when input or gameplay state is hard to verify."
---

# uloop wait-for-debug-break

Pause Unity when execution reaches a named marker in user code.

## When to use

- Use when logs or screenshots cannot prove that a gameplay path was reached.
- Use during development debugging, not only E2E tests.
- Use after simulated keyboard or mouse input when you need to inspect exact runtime state.

## Workflow

1. Add a marker at the state you want to inspect:

```csharp
using io.github.hatayama.UnityCliLoop.Runtime;

UnityCliLoopDebug.Break("player-jumped");
```

2. Compile the project.
3. Enable the marker before triggering the code path:

```bash
uloop enable-debug-break --id player-jumped --timeout-seconds 30
```

4. Check marker state if needed:

```bash
uloop debug-break-status --id player-jumped
```

5. Trigger the behavior, such as `simulate-keyboard`, `simulate-mouse-input`, UI interaction, or dynamic code.
6. Wait for the marker:

```bash
uloop wait-for-debug-break --id player-jumped --timeout-seconds 30
```

If this command times out, the marker line was not reached while the command waited. Inspect `error.details.status`, `hitCount`, `isPlaying`, `isPaused`, `elapsedSinceEnabledMilliseconds`, and `remainingMilliseconds` to distinguish input not being consumed, gameplay conditions not being met, an id mismatch, or Unity already being paused. `elapsedSinceEnabledMilliseconds` is measured from `enable-debug-break`, not from `wait-for-debug-break`.

7. While Unity is paused, inspect state with `uloop get-logs`, `uloop get-hierarchy`, `uloop find-game-objects`, screenshots, or `uloop execute-dynamic-code`.
8. Clear the marker if you stop waiting:

```bash
uloop clear-debug-break --id player-jumped
```

## Marker placement

- Prefer natural gameplay or state-transition locations after input has been consumed, such as after jump velocity/state changes, physics contact, damage application, or inventory mutation.
- Avoid placing the marker immediately after issuing simulated input unless that exact input handling line is the state you need to inspect; immediate markers can interrupt the input command before the resulting gameplay state settles.
- Use separate ids for strict phases, for example `jump-input-read`, `jump-velocity-applied`, and `jump-landed`, instead of reusing one broad marker.

## Safety

- `UnityCliLoopDebug.Break` uses Unity's conditional-call pattern and is compiled out of non-Editor call sites.
- Code in a custom asmdef must reference `UnityCLILoop.PausePoints.Runtime` to use `UnityCliLoopDebug.Break`.
- Do not pass side-effect expressions as the id argument. Use stable string ids.
- This does not collect logs or state snapshots. Use existing inspection commands after Unity pauses.
- If `enable-debug-break` warns about Domain Reload before PlayMode, the marker may be cleared when entering PlayMode. Domain Reload disabled is suitable for this workflow; otherwise enable again after PlayMode starts.
