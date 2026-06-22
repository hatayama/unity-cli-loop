---
name: uloop-wait-for-pause-point
description: "Pauses Unity's playback and allows you to inspect specific frames. Use this for bug investigation or PlayMode/E2E testing. It's the most convenient yet rigorous method for verifying variable states at specific frames or confirming whether particular code has been executed."
---

# uloop wait-for-pause-point

## Quick Check Template

Use this small loop for one representative frame you care about:

Custom asmdef scripts must reference `UnityCLILoop.PausePoints.Runtime` before calling `UloopPausePoint.Pause`. `Assembly-CSharp` scripts can usually use it without a manual reference.

1. Put a focused log and marker at the natural transition point. Log only local/intermediate values that will be hard to inspect later:

```csharp
using UnityEngine;
using io.github.hatayama.UnityCliLoop.Runtime;

Debug.Log($"state-transition-applied localValue={localValue} reason={reason}");
UloopPausePoint.Pause("state-transition-applied");
```

1. Compile, enter PlayMode, then enable the marker:

```bash
uloop enable-pause-point --id state-transition-applied --timeout-seconds 30
```

1. Trigger the action with a `simulate-*` command.
2. Wait for the marker and read the focused log in one call, even if the trigger command already returned `InterruptedByPausePoint=true`:

```bash
uloop wait-for-pause-point --id state-transition-applied --timeout-seconds 30
```

The hit response always embeds the log entries matching the marker id as `MatchingLogs` (`--matching-logs-max-count` adjusts the limit, default 10), so a separate `get-logs` call while paused is unnecessary. Log embedding is always on; there is no opt-in flag, and a `--include-matching-logs` option no longer exists. An empty `MatchingLogs` array means the fetch succeeded and no matching log exists; if the field is absent, the log fetch itself failed, so fall back to `uloop get-logs --search-text state-transition-applied --max-count 20` while paused.

Read `EvidenceSummary` first when it is present. It groups `EditorState`, pause point hit metadata, matching-log counts, truncation status, and warnings so you can tell whether the evidence is a single clean hit or needs closer inspection. `EditorState.CapturedAt` names when the Unity Editor play/pause state was observed, such as `PausePointHit`, `Current`, or `ClearAll`.

Use `Generation`, `EnabledAtUtc`, and the hit sequence fields from the hit or status response to tell a fresh marker from stale evidence with the same id. `RemainingMilliseconds` and `Expired` are returned directly so you do not need to infer marker lifetime from elapsed time.

1. While Unity is still paused, capture any additional evidence with `uloop execute-dynamic-code`, `uloop get-hierarchy`, `uloop find-game-objects`, and one screenshot.
2. Clear the marker with `uloop clear-pause-point --id state-transition-applied` or stop PlayMode before moving on. Use `uloop clear-pause-point --all` to clear every active marker at once, for example when resetting between E2E scenarios. The clear response's `EditorState` describes Unity Editor play/pause state, not marker state.

## When To Use

- Use this as the standard frame proof for state-changing PlayMode/E2E simulated input, physics, or UI transitions.
- Consider a pause point during E2E passes when transition-frame evidence would add confidence, even if durable state, logs, or screenshots can later confirm the final result.
- Use this before reaching for `Time.timeScale`, sleeps, repeated polling, or after-the-fact `execute-dynamic-code`; those checks can supplement the paused-frame proof, but they are not substitutes.
- If the value you need is a method local, an intermediate calculation, or a branch reason that `execute-dynamic-code` cannot reach, add a focused `Debug.Log` immediately before the marker and read it with `get-logs` while paused. Do not count the breakpoint check complete until the matching log has been read.
- Treat the pause like a lightweight breakpoint for one important transition: combine nearby debug logs with paused-frame inspection to confirm the variables and component state at that point.
- Do not treat `simulate-* Success=true`, generic action logs, sleeps/retries, testing-only counters, or `Time.timeScale` changes as paused-frame proof.
- Skip this only for ordinary persistent-state checks when you are not validating simulated input delivery, event ordering, or transition-frame fidelity.

## Timeout Checks

If this command times out, the marker line was not reached while the command waited. Read `Error.Details.Hint` first: it names the most likely cause when PlayMode is not running, Unity is already paused, or the marker was enabled but never hit. A `PAUSE_POINT_EXPIRED` error carries the same hint and shell-neutral `Error.Details.RecommendedNextAction`: it means the marker's own `enable-pause-point --timeout-seconds` window (measured from enable, not from wait) ran out first, so clear and re-enable the marker using the returned `Id` and `TimeoutSeconds`. Then inspect `Error.Details.Status`, `HitCount`, `Generation`, `EnabledAtUtc`, `EditorState`, `ElapsedSinceEnabledMilliseconds`, and `RemainingMilliseconds` to distinguish input not being consumed, stale evidence from an older marker generation, runtime conditions not being met, an id mismatch, or Unity already being paused. On `PAUSE_POINT_WAIT_TIMEOUT`, `Error.Details.MatchingLogs` and `Error.Details.EvidenceSummary` show whether the marker's focused log ever appeared. `ElapsedSinceEnabledMilliseconds` is measured from `enable-pause-point`, not from `wait-for-pause-point`.

Use `uloop pause-point-status --id state-transition-applied` only when you need to confirm the marker is armed or inspect the current hit state. Add focused debug logs before the marker when local variables must be captured.

## Fast-Progressing Games

When the game advances on its own (a ball keeps bouncing, blocks keep falling), the state you are arranging can move past the marker before the input command and the wait are even issued. Pause the Editor and walk frames explicitly instead:

```bash
# Freeze the whole player loop while arranging the scenario
uloop control-play-mode --action Pause
# ... enable markers, inspect/arrange state with execute-dynamic-code, get-hierarchy, get-logs ...
# Advance exactly one frame and stay paused (the Editor's Next Frame button)
uloop control-play-mode --action Step
# Resume right before sending the input you are verifying (input simulation needs an unpaused player)
uloop control-play-mode --action Play
```

Do not use `Time.timeScale = 0` for this: projects that read unscaled time keep advancing regardless, and the value silently persists into the next PlayMode session. Editor pause and `Step` freeze the entire player loop independent of `Time.timeScale`.

A pause point hit leaves Unity in this same paused state, so `Step` also works right after a marker hits: inspect the paused frame, then step forward to watch the following frames commit one at a time.

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
