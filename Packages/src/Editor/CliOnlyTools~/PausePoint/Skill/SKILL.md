---
name: uloop-pause-point
description: "Pauses Unity playback at any source file:line without editing code or recompiling, and returns a snapshot of the locals, parameters, and instance fields at that exact frame. Use for bug investigation, PlayMode/E2E verification, checking variable values at a specific frame, or confirming that a code path executed."
---

# uloop await-pause-point

## Quick Check Template

Use this small loop for one representative frame you care about. No source edit and no recompile: the pause point is patched into the already-compiled code and can be enabled mid-PlayMode.

1. Enter PlayMode, then enable a pause point on the line you want to freeze:

```bash
uloop enable-pause-point --file Assets/Scripts/Enemy.cs --line 42 --timeout-seconds 30
```

`--timeout-seconds` on enable starts the marker lifetime clock at enable time, not when you later run `await-pause-point`.

The response returns the derived marker `Id` (`Assets/Scripts/Enemy.cs:42`), the `ResolvedLine` that was actually patched, and the `ResolvedMethod`. When the requested line has no executable statement, the pause point rounds forward to the next executable line — check `ResolvedLine` when precision matters. Use the returned `Id` for every follow-up command.

2. Trigger the action with a `simulate-*` command.
3. Wait for the hit, even if the trigger command already returned `InterruptedByPausePoint=true`:

```bash
uloop await-pause-point --id "Assets/Scripts/Enemy.cs:42" --timeout-seconds 30
```

4. Read `CapturedVariables` in the hit response first: the locals, parameters, and `this` instance fields at the paused line are already there (see the next section). Adding a temporary `Debug.Log` just to see a local variable is no longer necessary.
5. While Unity is still paused, capture any additional evidence with `uloop execute-dynamic-code`, `uloop get-hierarchy`, `uloop find-game-objects`, and one screenshot.
6. Clear the marker with `uloop clear-pause-point --id "Assets/Scripts/Enemy.cs:42"` or stop PlayMode before moving on. Use `uloop clear-pause-point --all` to clear every active marker at once, for example when resetting between E2E scenarios. Clearing also removes the underlying code patch, so the method runs untouched afterwards.

## Capture Modes and History

Choose the capture mode when enabling a pause point:

- `single-shot` is the default. The first hit pauses Unity and disarms the marker.
- `continuous` pauses Unity on every hit and remains armed. Each hit adds a frame to `CapturedVariableHistory`, while `CapturedVariables` remains the latest-hit compatibility view.
- `trace` remains armed and records each hit without pausing Unity.

`--max-history` defaults to 20 and accepts values from 1 through 100. When the limit is exceeded, the oldest frames are dropped and `HistoryDroppedCount` reports how many were removed. `pause-point-status` returns the current `Mode`, `MaxHistory`, history frames, and dropped count.

To inspect value changes one Editor Step at a time, enable a `continuous` pause point on a line inside `Update` or `FixedUpdate`, trigger the first hit, then run:

```bash
uloop control-play-mode --action Step
uloop pause-point-status --id "Assets/Scripts/Enemy.cs:42"
```

Repeat the Step/status pair to inspect the history tail. A new frame is captured only when the patched line executes during that frame; event handlers such as `OnCollisionEnter` update only when the event occurs again. Use a longer `--timeout-seconds` for a Step session because the enable-time timeout does not extend after hits.

## Reading CapturedVariables

Every hit response embeds `CapturedVariables`: the method's in-scope locals, its parameters, and the `this` instance fields, captured at the exact moment execution reached the patched line. Values are point-in-time strings, not live references, so they stay valid as evidence even after Unity resumes.

- The snapshot is taken **before** the resolved line executes, exactly like an IDE breakpoint on that line. To inspect a value after an assignment, place the pause point on the following line.
- `execute-dynamic-code` during the pause sees the interrupted method's **post-interrupt** state, not this pre-line snapshot. Use `CapturedVariables` for pre-line evidence; use the raw capture API below when you need live references while paused.
- `Scope` is `Local`, `Parameter`, or `InstanceField`.
- `UnityEngine.Object` values additionally carry `UnityObjectKind` (`SceneObject`, `PrefabAsset`, `Asset`, `RuntimeInstance`, or `Destroyed`), `UnityObjectPath`, and `UnityObjectInstanceId`. Use these as handles for the next dig: a `SceneObject` path feeds `get-hierarchy`/`find-game-objects`, an asset path locates the asset, and the InstanceID works with `execute-dynamic-code`.
- `CapturedVariablesTruncated=true` means at least one value was clipped to the length cap or the variable-count cap stopped enumeration; clipped values are still present up to the cap.
- async and coroutine methods work: hoisted locals and the original `this` fields appear under their normal names.
- If the patched method ran off the main thread, values degrade to type names with a `(captured off main thread)` note; the hit itself is still recorded.

Read `EvidenceSummary` first when it is present. It groups `EditorState`, pause point hit metadata, matching-log counts, truncation status, and warnings so you can tell whether the evidence is a single clean hit or needs closer inspection. `MatchingLogs` (log entries whose text contains the marker id) is still embedded, but source-derived ids rarely appear in log text, so treat `CapturedVariables` as the primary variable evidence.

Use `Generation`, `EnabledAtUtc`, and the hit sequence fields from the hit or status response to tell a fresh marker from stale evidence with the same id. `RemainingMilliseconds` and `Expired` are returned directly so you do not need to infer marker lifetime from elapsed time.

## Raw Capture While Paused

While Unity is paused on a hit, `execute-dynamic-code` can read live captured references through `UloopPausePoint`:

- `TryGetCapturedValue(string name)` returns `(bool Found, object Value)` for the latest hit only. When multiple captured variables share the same name, the last one wins.
- `GetCapturedNames()` lists captured variable names from that snapshot.
- `GetCapturedPausePointId()` returns the pause-point id for the held snapshot.

The holder clears when Unity resumes (not when you `Step` while still paused), when the matching pause point is cleared, when a new hit replaces the snapshot, or when PlayMode exits. After resume, `TryGetCapturedValue` returns `Found=false`.

## Marker Types

- `uloop enable-pause-point --file --line` patches the already-compiled method at a source line. No code edit or recompile is required.
- `UloopPausePoint.Pause(id)` is a hand-written marker call for code paths that file:line patching cannot reach. Pair it with `uloop enable-pause-point --id <id>` (no `--file`/`--line`).
- For ordinary file:line debugging you do not need `UloopPausePoint.Pause` in source. Prefer CLI enable when the target line can be patched.

## Hit Preconditions

A pause point hits only when control flow reaches the patched line (or the `Pause(id)` call). `simulate-keyboard` returning `PressEdgeObserved=true` means the input edge was observed, not that your target game logic has reached the pause line yet.

## When To Use

- Use this as the standard frame proof for state-changing PlayMode/E2E simulated input, physics, or UI transitions.
- Consider a pause point during E2E passes when transition-frame evidence would add confidence, even if durable state, logs, or screenshots can later confirm the final result.
- Use this before reaching for `Time.timeScale`, sleeps, repeated polling, or after-the-fact `execute-dynamic-code`; those checks can supplement the paused-frame proof, but they are not substitutes.
- If the value you need is a method local, an intermediate calculation, or a branch reason that `execute-dynamic-code` cannot reach, put the pause point on that line: `CapturedVariables` records it without touching the source.
- Treat the pause like a lightweight breakpoint for one important transition: the captured snapshot plus paused-frame inspection confirm the variables and component state at that point.
- Do not treat `simulate-* Success=true`, generic action logs, sleeps/retries, testing-only counters, or `Time.timeScale` changes as paused-frame proof.
- Skip this only for ordinary persistent-state checks when you are not validating simulated input delivery, event ordering, or transition-frame fidelity.

## Timeout Checks

If this command times out, the patched line was not reached while the command waited. Read `Error.Details.Hint` first: it names the most likely cause when PlayMode is not running, Unity is already paused, or the marker was enabled but never hit. A `PAUSE_POINT_EXPIRED` error carries the same hint and shell-neutral `Error.Details.RecommendedNextAction`: it means the marker's own `enable-pause-point --timeout-seconds` window (measured from enable, not from wait) ran out first, so clear and re-enable the pause point using the returned `Id` and `TimeoutSeconds`. Then inspect `Error.Details.Status`, `HitCount`, `Generation`, `EnabledAtUtc`, `EditorState`, `ElapsedSinceEnabledMilliseconds`, and `RemainingMilliseconds` to distinguish input not being consumed, stale evidence from an older marker generation, runtime conditions not being met, an id mismatch, or Unity already being paused. `ElapsedSinceEnabledMilliseconds` is measured from `enable-pause-point`, not from `await-pause-point`.

Use `uloop pause-point-status --id "Assets/Scripts/Enemy.cs:42"` only when you need to confirm the marker is armed or inspect the current hit state.

## Fast-Progressing Games

When the game advances on its own (a ball keeps bouncing, blocks keep falling), the state you are arranging can move past the target line before the input command and the wait are even issued. Pause the Editor and walk frames explicitly instead:

```bash
# Freeze the whole player loop while arranging the scenario
uloop control-play-mode --action Pause
# ... enable pause points, inspect/arrange state with execute-dynamic-code, get-hierarchy, get-logs ...
# Advance exactly one frame and stay paused (the Editor's Next Frame button)
uloop control-play-mode --action Step
# Resume right before sending the input you are verifying (input simulation needs an unpaused player)
uloop control-play-mode --action Play
```

Do not use `Time.timeScale = 0` for this: projects that read unscaled time keep advancing regardless, and the value silently persists into the next PlayMode session. Editor pause and `Step` freeze the entire player loop independent of `Time.timeScale`.

A pause point hit leaves Unity in this same paused state, so `Step` also works right after a hit: inspect the paused frame, then step forward to watch the following frames commit one at a time.

## Line Placement

- Prefer natural runtime points after input has been consumed, such as after a command is accepted, a state value changes, an evaluation step resolves, or a dependent component is updated.
- For frame-specific bugs, target the suspicious state branch or the line right after the mutation you need to freeze (the snapshot is taken before the target line runs).
- Enable pause points after PlayMode is running: entering PlayMode with Domain Reload enabled reloads the domain and silently removes every source pause point (see Requirements & Safety).
- Targeting the line that directly handles simulated input is safe: when the pause lands mid-command, the `simulate-*` command returns promptly with `InterruptedByPausePoint=true` instead of running to completion, and `simulate-mouse-ui` additionally states in `Message` whether the pointer event was already dispatched before the pause. Prefer a line after the input is consumed when you want the settled result state rather than the input-handling moment.
- Use separate pause points on distinct lines for strict phases, for example input read, state updated, and result committed, instead of one broad pause point.

## Requirements & Safety

- **Debug code optimization is required.** When the Editor's Code Optimization mode is Release, enable is rejected with instructions; switch to Debug via the bug icon in the main toolbar, recompile, then retry.
- **Patches do not survive compiles or domain reloads.** Any script compile or domain reload removes every source pause point together with its marker, leaving the code exactly as compiled. Re-enable after the reload finishes. This is also why an interrupted CLI session never leaves stale patches behind.
- If `enable-pause-point` fails, read the failure `Message` and `RecommendedNextAction`: they name the exact next step, for example waiting for a reload to finish, re-resolving after a recompile, or what to do when the method cannot be patched.
- For scripts under `Packages/`, pass the package-id form of the path — `Packages/<package-id>/...`, exactly as the Unity Project window and console stack traces show it. The physical checkout path of an embedded package does not resolve.
- If enable fails with a "No sequence point found" error even for clearly executable lines, that script's assembly lacks debug sequence points and no line in the file can be patched. Move the pause point to a script in an assembly that carries them, such as a script under `Assets/`.
- Very small methods can be inlined by Mono's JIT into callers, in which case the pause point never hits even though the line executes. If a line demonstrably runs but the pause point stays unhit, move the pause point into the calling method.
- If `enable-pause-point` warns about Domain Reload before PlayMode, the pause point may be cleared when entering PlayMode. Domain Reload disabled is suitable for this workflow; otherwise enable it again after PlayMode starts.
