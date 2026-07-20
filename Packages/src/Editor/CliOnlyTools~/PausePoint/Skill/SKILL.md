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

`--timeout-seconds` on enable starts the marker lifetime clock at enable time, not when you later run `await-pause-point`. Size it to cover only the setup steps you run between `enable-pause-point` and the `await-pause-point` call (for example seeding state with `execute-dynamic-code`): once `await-pause-point` starts waiting, it extends the marker's remaining lifetime to at least its own `--timeout-seconds`, so you no longer need to also budget for the wait itself. The extension cannot revive a marker that has already expired, so the enable timeout still has to outlive the setup steps themselves.

The response returns the derived marker `Id` (`Assets/Scripts/Enemy.cs:42`), the `ResolvedLine` that was actually patched, the `ResolvedMethod`, and `ResolvedLineText` — the actual source text at `ResolvedLine`. When the requested line has no executable statement, the pause point rounds forward to the next executable line — check `ResolvedLine`/`ResolvedLineText` when precision matters, and re-check them after every code edit: a rewritten file shifts line numbers, so do not assume a previously-derived line number still points at the same statement. Use the returned `Id` for every follow-up command.

2. Trigger the action with a `simulate-*` command.
3. Wait for the hit, even if the trigger command already returned `InterruptedByPausePoint=true`:

```bash
uloop await-pause-point --id "Assets/Scripts/Enemy.cs:42" --timeout-seconds 30
```

4. Read `CapturedVariables` in the hit response first: the locals, parameters, and `this` instance fields at the paused line are already there (see the next section). Adding a temporary `Debug.Log` just to see a local variable is no longer necessary. (the snapshot is pre-line: taken before the resolved line executes, like an IDE breakpoint)
5. While Unity is still paused, capture any additional evidence with `uloop execute-dynamic-code`, `uloop get-hierarchy`, `uloop find-game-objects`, and one screenshot.
6. Clear the marker with `uloop clear-pause-point --id "Assets/Scripts/Enemy.cs:42"` or stop PlayMode before moving on. Use `uloop clear-pause-point --all` to clear every active marker at once, for example when resetting between E2E scenarios. Clearing also removes the underlying code patch, so the method runs untouched afterwards.

## Capture Modes and History

Choose the capture mode when enabling a pause point:

- `single-shot` is the default. The first hit pauses Unity and disarms the marker.
- `continuous` pauses Unity on every hit and remains armed. `CapturedVariables` always holds the latest hit; `CapturedVariableHistory` holds only strictly older frames (the frame matching the latest hit is never repeated there), so with a single hit the history is empty by design.
- `trace` remains armed and records each hit without pausing Unity.

`--max-history` defaults to 20 and accepts values from 1 through 100. When the limit is exceeded, the oldest frames are dropped and `HistoryDroppedCount` reports how many were removed. `pause-point-status` returns the current `Mode`, `MaxHistory`, history frames, and dropped count.

To inspect value changes one Editor Step at a time, enable a `continuous` pause point on a line inside `Update` or `FixedUpdate`, trigger the first hit, then run:

```bash
uloop control-play-mode --action Step
uloop pause-point-status --id "Assets/Scripts/Enemy.cs:42"
```

Repeat the Step/status pair to inspect the history tail. A new frame is captured only when the patched line executes during that frame; event handlers such as `OnCollisionEnter` update only when the event occurs again. Use a longer `--timeout-seconds` for a Step session because the enable-time timeout does not extend after hits.

For multi-step verification, avoid repeating enable→await→clear cycles with the default single-shot mode: pass `--mode continuous` to `enable-pause-point` (the marker re-arms automatically after each hit and keeps history), or enable several file:line markers at once — markers are independent and can stay armed simultaneously.

## Reading CapturedVariables

Every hit response embeds `CapturedVariables`: the method's in-scope locals, its parameters, and the `this` instance fields, captured at the exact moment execution reached the patched line. Values are point-in-time strings, not live references, so they stay valid as evidence even after Unity resumes.

- The snapshot is taken **before** the resolved line executes, exactly like an IDE breakpoint on that line. To inspect a value after an assignment, place the pause point on the following line.
- `Scope` is `Local`, `Parameter`, `InstanceField`, or `This`. The synthetic `this` entry identifies which instance or GameObject was hit via `UnityObjectPath` and `UnityObjectInstanceId`; `UnityEngine.Object` values carry the same handle fields for follow-up digs with `get-hierarchy`, `find-game-objects`, or `execute-dynamic-code`.
- `--captured-variables names` on `await-pause-point`/`pause-point-status` drops every `Value` and keeps `Name`/`Scope`/`TypeName` — use it first on field-heavy classes, then fetch full values with a plain `pause-point-status` call.
- While Unity is still paused, `UloopPausePoint.TryGetCapturedValue("name")` (and `"this"`) returns live captured references for `execute-dynamic-code`; the holder clears on resume. (file:line marker hits only — id-only markers store no capture)

Before interpreting unexpected, missing, or truncated values, nested previews that render as type names, Unity-object `Value` strings, capture-time vs live evidence trade-offs, the hit response's `Warning`/`MatchingLogs` fields, marker freshness (`Generation`, `EnabledAtUtc`), or the raw capture API in detail, read [references/captured-variables.md](references/captured-variables.md).

## Watch Expressions

Use watch expressions when a value should be re-evaluated automatically after each paused Play Mode Step:

```bash
uloop enable-watch --id "speed" --expression "UloopPausePoint.TryGetCapturedValue(\"speed\").Value" --max-history 20
uloop get-watch-values --id "speed"
```

A watch evaluates only on a changed, paused frame, and a domain reload clears all watches. For the full evaluation rules (baseline, ordering, duplicate ids, `ValueFrozenHint`, error handling), read [references/watch-expressions.md](references/watch-expressions.md).

## Marker Types

- `uloop enable-pause-point --file --line` patches the already-compiled method at a source line. No code edit or recompile is required.
- `UloopPausePoint.Pause(id)` is a hand-written marker call for code paths that file:line patching cannot reach. Pair it with `uloop enable-pause-point --id <id>` (no `--file`/`--line`). The call does not need to live in committed source — a dynamic-code watcher can fire it (see the next section).
- The id-only marker records the hit itself and nothing more: `CapturedVariables` is always empty, and no raw capture is stored, so `TryGetCapturedValue`/`GetCapturedNames` return nothing for these hits. When you need variable values at an id-only marker, read the target objects directly with `execute-dynamic-code` while the Editor is paused, or use a file:line marker instead.
- For ordinary file:line debugging you do not need `UloopPausePoint.Pause` in source. Prefer CLI enable when the target line can be patched.
- Physical Unity message methods (`OnCollisionEnter2D`, `OnTriggerEnter2D`, and similar callbacks) can silently never hit even though the method body demonstrably runs: Unity can resolve a GameObject's message dispatch before the file:line patch is installed, so a GameObject that already existed at enable time may keep calling the pre-patch code. If `await-pause-point`/`pause-point-status` reports `HitCount=0` on a physical callback line, check the response `Warning` for this note, then work around it by recreating the GameObject after enabling, or by embedding `UloopPausePoint.Pause("<id>")` directly in the method body via an id-only marker instead of a file:line one.

## Catching a Runtime Condition with a Dynamic-Code Trigger

A file:line pause point freezes a specific source line. When the moment you need is defined by a runtime condition instead — an animation passing a normalized time, HP reaching zero, an enemy spawning — enable an id-only marker (`uloop enable-pause-point --id <id>`, no `--file`/`--line`), then use `execute-dynamic-code` to register an `EditorApplication.update` watcher that calls `UloopPausePoint.Pause("<id>")` on the first frame the condition holds, and wait with `uloop await-pause-point --id <id>` on the CLI side. This freezes the first frame where the condition holds, without writing any .cs file.

Before using this pattern, read [references/condition-triggered-pause.md](references/condition-triggered-pause.md) for the full workflow, a complete watcher example, and the safety rules (never sleep in the snippet, watcher self-unsubscription, deadline handling).

## Pausing Right After Simulated Input, Plus N Frames

To freeze the frame where a `simulate-mouse-ui` click or a `simulate-keyboard` key press lands, you do not need a watcher: enable a file:line pause point on the input-consuming line before sending the input. When the pause lands mid-command, the `simulate-*` command returns promptly with `InterruptedByPausePoint=true` (see Line Placement).

For "N frames after the input" (for example, three frames after a key press), advance from that hit with `control-play-mode --action Step` exactly N times — `Step` works right after a hit. Do not compute frame offsets in a dynamic-code watcher (recording `Time.frameCount` and pausing at `recorded + N`): frames keep advancing between CLI commands, so the recorded baseline is race-prone and the pause lands on an unpredictable frame. Reserve the watcher pattern for condition-defined moments; use hit-then-Step for frame-offset positioning.

## Hit Preconditions

A pause point hits only when control flow reaches the patched line (or the `Pause(id)` call). `simulate-keyboard` returning `PressEdgeObserved=true` means the input edge was observed, not that your target game logic has reached the pause line yet.

If a `simulate-*` command instead returns a failure whose message says PlayMode is paused, suspect a pause point hit rather than an unrelated failure: an active pause point can make PlayMode paused mid-simulation, and the `simulate-*` call surfaces that as a preflight failure. Check `uloop pause-point-status --id <id>` first to confirm the hit before treating it as a bug in the simulated action itself.

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

If none of the above explains `HitCount=0`, suspect JIT inlining: Mono can inline very small target methods into callers, and the pause point then never fires even though the line runs. Move the pause point into the calling method (see Requirements & Safety).

The `enable-pause-point --timeout-seconds` countdown freezes while a hit holds the Editor paused: the elapsed pause duration is credited back onto the marker's expiry on resume, so inspecting a paused hit for as long as you need does not erode the remaining timeout budget. The freeze applies only to a pause caused by a pause-point hit; a manual pause without a hit does not stop the countdown.

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

While the Editor is paused, injecting state by writing fields or transforms directly can silently fail to stick: `transform.position` and `Rigidbody2D.position` do not synchronize until the next simulation step, and any production `Update()` that recomputes the value will overwrite the injection on the next frame. Prefer arranging state through the game's own methods; after a direct write, advance one frame with `--action Step` and re-read the value to confirm it took effect.

## Line Placement

- Prefer natural runtime points after input has been consumed, such as after a command is accepted, a state value changes, an evaluation step resolves, or a dependent component is updated.
- For frame-specific bugs, target the suspicious state branch or the line right after the mutation you need to freeze (the snapshot is taken before the target line runs).
- A line that runs unconditionally every frame hits on the very next frame, before the input or event you actually wanted to observe arrives. If you need to catch a specific moment, choose a line that only executes conditionally (inside an `if` guarding the event you care about) so the pause point does not fire prematurely. The opposite applies to `continuous` mode paired with a watch expression: the watch only re-evaluates on a paused frame where the marker's line executes, so a conditional line that stops being reached leaves the watch value frozen (see Watch Expressions) — pick a line reached every frame when you need continuous per-Step updates.
- When every reachable line around the state change you want runs unconditionally every frame, with no existing `if` to hang the pause point on, move the moment you want to observe into a conditional block: `if (<event condition>) { <mutation>; Debug.Assert(<postcondition of the mutation>); }`, then target the pause point at the `Debug.Assert` line. The `if` creates a line that executes only when the event actually happens, so the pause point no longer fires on the very next frame; the `Debug.Assert` states the mutation's postcondition, so the line you pause on is meaningful production code ("this must hold here") rather than an arbitrary probe, and it can stay in the codebase after the investigation ends.
- An empty-body loop such as `while (TryMove(0, 1)) { }` has no statement inside the braces, so a pause point on the line right after the loop hits at the loop's condition re-check, not once the loop has actually finished advancing. If you need the state after the loop completes, target a line that is guaranteed to run exactly once after the loop exits, not the loop line itself.
- Enable pause points after PlayMode is running: entering PlayMode with Domain Reload enabled reloads the domain and silently removes every source pause point (see Requirements & Safety).
- Targeting the line that directly handles simulated input is safe: when the pause lands mid-command, the `simulate-*` command returns promptly with `InterruptedByPausePoint=true` instead of running to completion, and `simulate-mouse-ui` additionally states in `Message` whether the pointer event was already dispatched before the pause. Prefer a line after the input is consumed when you want the settled result state rather than the input-handling moment.
- Use separate pause points on distinct lines for strict phases, for example input read, state updated, and result committed, instead of one broad pause point.

## Requirements & Safety

- **Debug code optimization is required.** When the Editor's Code Optimization mode is Release, enable is rejected with instructions; switch to Debug via the bug icon in the main toolbar, recompile, then retry.
- **Patches do not survive compiles or domain reloads.** Any script compile or domain reload removes every source pause point together with its marker, leaving the code exactly as compiled. Re-enable after the reload finishes. This is also why an interrupted CLI session never leaves stale patches behind.
- **`uloop compile` while PlayMode is running triggers this same domain reload.** It does not just drop the pause point marker — the running PlayMode session itself is reset by the reload, so the game state you had arranged (scene, spawned objects, progress) is gone too. After a mid-PlayMode compile, re-enable the pause point and re-enter PlayMode (arranging state again) rather than assuming the paused scenario is still intact.
- If `enable-pause-point` fails, read the failure `Message` and `RecommendedNextAction`: they name the exact next step, for example waiting for a reload to finish, re-resolving after a recompile, or what to do when the method cannot be patched.
- For scripts under `Packages/`, pass the package-id form of the path — `Packages/<package-id>/...`, exactly as the Unity Project window and console stack traces show it. The physical checkout path of an embedded package does not resolve.
- If enable fails with a "No sequence point found" error even for clearly executable lines, that script's assembly lacks debug sequence points and no line in the file can be patched. Move the pause point to a script in an assembly that carries them, such as a script under `Assets/`.
- Very small methods can be inlined by Mono's JIT into callers, in which case the pause point never hits even though the line executes. If a line demonstrably runs but the pause point stays unhit, move the pause point into the calling method.
- If `enable-pause-point` warns about Domain Reload before PlayMode, the pause point may be cleared when entering PlayMode. Domain Reload disabled is suitable for this workflow; otherwise enable it again after PlayMode starts.
