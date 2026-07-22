---
name: uloop-pause-point
description: "Pauses Unity playback at any source file:line without editing code or recompiling, and returns a snapshot of the locals, parameters, and instance fields at that exact frame. Use for bug investigation, PlayMode/E2E verification, checking variable values at a specific frame, or confirming that a code path executed."
---

# uloop await-pause-point

## Quick Check Template

Use this small loop for one representative frame you care about. No source edit and no recompile: the pause point is patched into the already-compiled code and can be enabled mid-PlayMode.

1. Enter PlayMode, then run one foreground command that arms the pause point, fires the input, and waits for the hit:

```bash
uloop enable-pause-point --file Assets/Scripts/Enemy.cs --line 42 --timeout-seconds 30 --await --trigger "simulate-keyboard --action Press --key Space"
```

`--trigger` runs a single uloop subcommand in-process only after the marker's arming is confirmed, so there is no arm-vs-input race and nothing needs to run in the background. The hit response additionally carries `TriggerResult` with the triggered command's own response (or, when the trigger was skipped, `Completed: false` and the reason in `Error`). The trigger string cannot name another pause-point wait (`await-pause-point`/`enable-pause-point`) and cannot pass `--project-path` — the enclosing command's project is used. `await-pause-point --id <id> --trigger ...` accepts the same flag for a marker enabled earlier.

When the game reaches the line on its own, omit `--trigger`. Fall back to split steps only when the triggering action is not a single uloop command (several inputs in sequence, an external event): run `enable-pause-point` without `--await` in the foreground (its response returning is the arm confirmation), then start `uloop await-pause-point --id <id>` in the background, then send the inputs. Do not approximate arm-waiting with a fixed sleep after a backgrounded enable.

`--timeout-seconds` on enable starts the marker lifetime clock at enable time and is also the deadline `--await` waits against, so size it to cover both the trigger and the wait.

The response returns the derived marker `Id` (`Assets/Scripts/Enemy.cs:42`), the `ResolvedLine` that was actually patched, the `ResolvedMethod`, and `ResolvedLineText` — the actual source text at `ResolvedLine`. When the requested line has no executable statement, the pause point rounds forward to the next executable line — check `ResolvedLine`/`ResolvedLineText` when precision matters, and re-check them after every code edit: a rewritten file shifts line numbers, so do not assume a previously-derived line number still points at the same statement. Use the returned `Id` for every follow-up command. On a hit, this same response already carries `CapturedVariables` and every other field `await-pause-point` would have returned — no separate `await-pause-point` call is needed.

2. Read `CapturedVariables` in the hit response first: the locals, parameters, and `this` instance fields at the paused line are already there (see the next section). Adding a temporary `Debug.Log` just to see a local variable is no longer necessary. (the snapshot is pre-line: taken before the resolved line executes, like an IDE breakpoint)
3. While Unity is still paused, capture any additional evidence with `uloop execute-dynamic-code`, `uloop get-hierarchy`, `uloop find-game-objects`, and one screenshot.
4. A `single-shot` marker (the default) disarms itself after the hit, so no clear call is required before moving on. Clearing is still what removes the underlying code patch (a disarmed marker leaves the patch installed), so for `continuous`/`trace` markers, or when the method must run fully untouched again, clear it with `uloop clear-pause-point --id "Assets/Scripts/Enemy.cs:42"` (or `--all` to clear every active marker at once, for example when resetting between E2E scenarios) or stop PlayMode. Clearing resumes Play Mode only when the current pause is owned by a pause-point hit — the clear response then carries a `Warning` saying it resumed Play Mode. A manual pause (`control-play-mode --action Pause` or the Editor pause button) is left untouched by clear.

A hit pauses Unity at the next frame boundary — the patched method and the rest of that frame still run to completion. Only `CapturedVariables` is evidence of the values at the patched line; state read after the pause (for example via `execute-dynamic-code`) may already have advanced past it. Treat every post-hit live read as a supplement for follow-up digging — the primary evidence for what a value was at the paused line is always `CapturedVariables`.

If the game progresses on its own (timers, gravity, spawners), run `control-play-mode` `Pause` before setting up scenario state and resume with `control-play-mode --action Play` only after `enable-pause-point` succeeds — otherwise the scenario can be consumed before your input arrives. See Fast-Progressing Games below.

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
- Rigidbody values read inside a physics callback (`OnCollision*`/`OnTrigger*`) can be mid-solver intermediates — `velocity` may capture as `(0.0, 0.0)` at the callback even though the body visibly moves. `CapturedVariables` faithfully records that intermediate value; a later `execute-dynamic-code` read returning something different means the physics solver has since finished the step, not that the capture was wrong.
- `Scope` is `Local`, `Parameter`, `InstanceField`, or `This`. The synthetic `this` entry identifies which instance or GameObject was hit via `UnityObjectPath` and `UnityObjectInstanceId`; `UnityEngine.Object` values carry the same handle fields for follow-up digs with `get-hierarchy`, `find-game-objects`, or `execute-dynamic-code`.
- `--captured-variables names` on `await-pause-point`/`pause-point-status` drops every `Value` and keeps `Name`/`Scope`/`TypeName` — use it first on field-heavy classes, then fetch full values with a plain `pause-point-status` call.
- When the response would be dominated by variables you do not need, pass `--captured-variable-names velocity,this` (comma-separated, exact match on `Name`) to keep only those entries; it composes with `--captured-variables full|names`.
- Pass `--expect 'name=value'` (repeatable; on `await-pause-point` and `enable-pause-point --await`, not `pause-point-status`) to have the CLI compare captured variables against expected values; the response includes an `Expectations` array and `AllExpectationsPassed`, so you do not need to eyeball the JSON. Matching is string equality against the serialized value.
- Collection values (arrays, `List<T>`, dictionaries, plain objects) render as a JSON preview capped at 10 elements by default. When the elements you need sit past that cap (a 10x20 grid, a long list), re-enable with `--max-preview-elements <n>` (1–1000): it raises the element cap and scales the preview's character budget proportionally, so each element keeps the same ~100-character share it has at the default — plenty for numeric or boolean cells, but elements that are individually long can still be clipped by the scaled budget (`CapturedVariablesTruncated` tells you when that happened). The enable response echoes the effective `MaxPreviewElements`.
- While Unity is still paused, `UloopPausePoint.TryGetCapturedValue("name")` (and `"this"`) returns live captured references for `execute-dynamic-code`; the holder clears on resume. (file:line marker hits only — id-only markers store no capture) These are **live objects in their frame-completed state, not snapshots**: the hit's method ran to completion before the pause landed, so anything it changed — or destroyed — afterwards is already applied. A captured object that the method later passed to `Destroy()` reads as destroyed/null through this API even though `CapturedVariables` shows its pre-line field values intact. Never use live reads to reconstruct what a value was at the paused line; that is what the `CapturedVariables` snapshot is for. Use live reads only to dig further into objects that are still alive.

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
- Physical Unity message methods (`OnCollisionEnter2D`, `OnTriggerEnter2D`, and similar callbacks) can silently never hit even though the method body demonstrably runs: in real projects, a GameObject that already existed at enable time has been observed to keep calling the pre-patch code (the condition is environment-dependent and does not reproduce on demand). If `await-pause-point`/`pause-point-status` reports `HitCount=0` on a physical callback line, first confirm the body actually ran after arming (a counter or log emitted by fresh contact — a stale pre-arm value proves nothing), and check the response `Warning` for this note. The cheapest recovery to try first is re-arming: `clear-pause-point` the marker, then `enable-pause-point` it again and wait for the next fresh contact (one field-observed recovery so far, 2026-07-22 — environment-dependent, not guaranteed). If the re-armed marker still misses, fall back to recreating the GameObject after enabling, or embed `UloopPausePoint.Pause("<id>")` directly in the method body via an id-only marker instead of a file:line one.
- The same warning also covers one-hop indirect calls: a regular method that is *called from* a physics message method elsewhere in the same compiled assembly (for example a helper invoked inside `OnCollisionEnter2D`) can miss pre-existing GameObjects for the same reason, and enable warns about it too. A call chain deeper than one hop, or a caller in a different assembly, is not detected — when such a helper stays at `HitCount=0` without explanation, treat it as this same limitation.
- A method already bound into a delegate or event before `enable-pause-point` may not fire through that delegate: the pre-bound invocation path can bypass the patch. Workarounds: enable the pause point before the delegate is created, recreate the subscribing GameObject, or re-bind the delegate (e.g. via `execute-dynamic-code`) after enabling.

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

To locate where control flow stops before an unhit line, bisect with a second pause point on the method's entry (its first executable line). If the entry point hits while the target line stays at `HitCount=0`, an early return or a branch between the two lines is filtering execution — inspect the guard values in the entry hit's `CapturedVariables` instead of retrying the original line.

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

Pause and Step leave one residual race: input simulation needs an unpaused player, so the game runs freely between the final `--action Play` and your input landing. When a single command round-trip takes longer than the game's natural tick interval (for example a piece that auto-falls every 0.8 seconds), the tick fires before the input arrives no matter how the steps are ordered. Remove the race instead of trying to outrun it: temporarily overwrite the tick-interval field with `execute-dynamic-code` (for example set the fall interval to a very large value), run the verification, then restore the original value and confirm the restore with a re-read.

A pause point hit leaves Unity in this same paused state, so `Step` also works right after a hit: inspect the paused frame, then step forward to watch the following frames commit one at a time.

While the Editor is paused, injecting state by writing fields or transforms directly can silently fail to stick: `transform.position` and `Rigidbody2D.position` do not synchronize until the next simulation step, and any production `Update()` that recomputes the value will overwrite the injection on the next frame. Prefer arranging state through the game's own methods; after a direct write, advance one frame with `--action Step` and re-read the value to confirm it took effect.

## Line Placement

- Prefer natural runtime points after input has been consumed, such as after a command is accepted, a state value changes, an evaluation step resolves, or a dependent component is updated.
- For frame-specific bugs, target the suspicious state branch or the line right after the mutation you need to freeze (the snapshot is taken before the target line runs).
- A line that runs unconditionally every frame hits on the very next frame, before the input or event you actually wanted to observe arrives. If you need to catch a specific moment, choose a line that only executes conditionally (inside an `if` guarding the event you care about) so the pause point does not fire prematurely. The opposite applies to `continuous` mode paired with a watch expression: the watch only re-evaluates on a paused frame where the marker's line executes, so a conditional line that stops being reached leaves the watch value frozen (see Watch Expressions) — pick a line reached every frame when you need continuous per-Step updates.
- When every reachable line around the state change you want runs unconditionally every frame, with no existing `if` to hang the pause point on, move the moment you want to observe into a conditional block: `if (<event condition>) { <mutation>; Debug.Assert(<postcondition of the mutation>); }`, then target the pause point at the `Debug.Assert` line. The `if` creates a line that executes only when the event actually happens, so the pause point no longer fires on the very next frame; the `Debug.Assert` states the mutation's postcondition, so the line you pause on is meaningful production code ("this must hold here") rather than an arbitrary probe, and it can stay in the codebase after the investigation ends. Use `UnityEngine.Debug.Assert` for this, not `System.Diagnostics.Debug.Assert`: a failed System.Diagnostics assert never reaches the Unity Console, so `get-logs` cannot observe it.
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
