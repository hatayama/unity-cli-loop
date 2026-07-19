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

The response returns the derived marker `Id` (`Assets/Scripts/Enemy.cs:42`), the `ResolvedLine` that was actually patched, the `ResolvedMethod`, and `ResolvedLineText` — the actual source text at `ResolvedLine`. When the requested line has no executable statement, the pause point rounds forward to the next executable line — check `ResolvedLine`/`ResolvedLineText` when precision matters, and re-check them after every code edit: a rewritten file shifts line numbers, so do not assume a previously-derived line number still points at the same statement. Use the returned `Id` for every follow-up command.

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
- `continuous` pauses Unity on every hit and remains armed. `CapturedVariables` always holds the latest hit; `CapturedVariableHistory` holds only strictly older frames (the frame matching the latest hit is never repeated there), so with a single hit the history is empty by design.
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
- The pause itself only takes effect at the next frame boundary: the frame that hit the pause point still runs to completion first, so any event that fires later in that same frame (a chained collision, a cascading destroy) has already happened by the time Unity actually stops. Trust `CapturedVariables` (the pre-line snapshot) as evidence for what was true up to the patched line; do not assume the paused state still matches it for events later in the same frame.
- `execute-dynamic-code` during the pause sees the interrupted method's **post-interrupt** state, not this pre-line snapshot. Use `CapturedVariables` for pre-line evidence; use the raw capture API below when you need live references while paused. If you suspect a captured value is stale or wrong, cross-check it against the live scene object with `execute-dynamic-code` (for example reading `transform.position` off the instance found via `UnityObjectPath`) rather than trusting either source alone. `execute-dynamic-code` responses also carry `EditorPaused` and `ActivePausePointId` — these fields appear only while the Editor is paused, so a call made while a pause point still has Unity paused is unambiguous instead of looking like a stale or buggy result.
- `Scope` is `Local`, `Parameter`, `InstanceField`, or `This`.
- The snapshot also includes a synthetic `this` entry (Scope `This`) for the paused instance itself, so you can tell which instance or GameObject was hit via its `UnityObjectPath` and `UnityObjectInstanceId`. For an async or coroutine method it resolves to the original outer instance, not the compiler-generated state machine, and static methods emit no `this` entry. While Unity is still paused, `UloopPausePoint.TryGetCapturedValue("this")` returns the live instance reference (for example so a watch expression can read `transform.position`).
- `UnityEngine.Object` values additionally carry `UnityObjectKind` (`SceneObject`, `PrefabAsset`, `Asset`, `RuntimeInstance`, or `Destroyed`), `UnityObjectPath`, and `UnityObjectInstanceId`. These three fields appear only for Unity object values; a non-Unity-object variable (an `int`, a `string`, a plain class) omits all three from the JSON entirely instead of sending them as empty/zero. Check whether `UnityObjectKind` is present to tell the two cases apart. Use the fields as handles for the next dig: a `SceneObject` path feeds `get-hierarchy`/`find-game-objects`, an asset path locates the asset, and the InstanceID works with `execute-dynamic-code`.
- `CapturedVariablesTruncated=true` means at least one value was clipped to the length cap or the variable-count cap stopped enumeration; clipped values are still present up to the cap.
- async and coroutine methods work: hoisted locals and the original `this` fields appear under their normal names.
- If the patched method ran off the main thread, values degrade to type names with a `(captured off main thread)` note; the hit itself is still recorded.

`await-pause-point`'s hit response also carries a top-level `Warning` (omitted when empty): it flags multiple hits, multiple matching logs, or truncated matching logs, so you can tell a single clean hit apart from evidence that needs closer inspection. `MatchingLogs` (log entries whose text contains the marker id) is still embedded, but source-derived ids rarely appear in log text, so treat `CapturedVariables` as the primary variable evidence.

Use `Generation`, `EnabledAtUtc`, and the hit sequence fields from the hit or status response to tell a fresh marker from stale evidence with the same id. `RemainingMilliseconds` and `Expired` are returned directly so you do not need to infer marker lifetime from elapsed time.

### Pulling More Than the Default Response Carries

The hit and status responses are push-first and kept lean by default: no field is ever a re-summary of another field, and a variable's `Value` is the only per-entry cost. For a class with dozens of `[SerializeField]` fields, a `continuous` marker's history still multiplies entry count by `MaxHistory` (default 20), which can be a lot of `Value` strings to carry around when you only need to know which names were captured.

Pull only what you need instead of paying for it all up front:

- `--captured-variables names` on `await-pause-point`/`pause-point-status` drops `Value` from every captured variable (including every history frame) and keeps `Name`/`Scope`/`TypeName`. Use it first on a field-heavy class, then fetch specific values afterward.
- `uloop pause-point-status --id <id>` returns the full response again, including every `Value`, whenever you need it — call it plain (no `--captured-variables`) for the complete picture after a lightweight `names` scan.

### Choosing the Right Evidence Source

Three different sources answer three different questions about a captured variable; pick by what you actually need:

| Need | Source | Notes |
|---|---|---|
| A value type's value at capture time | `UloopPausePoint.TryGetCapturedValue("name")` | Faithful: value types are a boxed copy taken at capture time, so this never drifts. |
| A reference type's *live* current state | `UloopPausePoint.TryGetCapturedValue("name")` | The reference itself is live, so the object it points to may have changed since capture (or been destroyed/resumed away). Only available while Unity is still paused. |
| A reference type's state *as it was at capture time* | `uloop pause-point-status --id <id>` | The only faithful source for this: the response is a formatted string snapshot taken at capture time and stored in the registry, so it never drifts and stays retrievable after resume until the next clear or domain reload. |

Capturing a deep copy at hit time was deliberately not adopted: it would cost hot-path performance and risk getter side effects, so the formatted-string snapshot (`pause-point-status`) remains the only way to get capture-time-faithful evidence for reference types.

## Raw Capture While Paused

While Unity is paused on a hit, `execute-dynamic-code` can read live captured references through `UloopPausePoint`:

- `TryGetCapturedValue(string name)` returns `(bool Found, object Value)` for the latest hit only. When multiple captured variables share the same name, the last one wins.
- `GetCapturedNames()` lists captured variable names from that snapshot.
- `GetCapturedPausePointId()` returns the pause-point id for the held snapshot.

The holder clears when Unity resumes (not when you `Step` while still paused), when the matching pause point is cleared, when a new hit replaces the snapshot, or when PlayMode exits. After resume, `TryGetCapturedValue` returns `Found=false`. Re-enabling the same pause point while still paused (for example to refresh its timeout during a step session) keeps the held references, because a re-enable does not resume Unity.

## Watch Expressions

Use watch expressions when the value should be evaluated automatically after each paused Play Mode Step:

```bash
uloop enable-watch --id "speed" --expression "UloopPausePoint.TryGetCapturedValue(\"speed\").Value" --max-history 20
uloop get-watch-values --id "speed"
```

`enable-watch` compiles the C# expression once, evaluates it immediately for a baseline, and then evaluates it once per changed `Time.frameCount`, but only while Play Mode is running and the Editor is paused (each hit pause and each `Step`); nothing is recorded while the game runs unpaused. Multiple watches run in registration order. `enable-watch` rejects a duplicate id instead of overwriting; clear with `clear-watch --id <id>` before re-registering a changed expression. `clear-watch --id <id>` removes one watch; `clear-watch --all` removes all watches. `get-watch-values` without `--id` returns every registered watch.

Because a watch only re-evaluates on a changed, paused frame, a value that looks stuck across several reads usually means no new paused frame has occurred — most often the linked pause point has not been hit again (a marker on a conditional line freezes after its first hit; see Line Placement). `get-watch-values` surfaces this as a non-empty `ValueFrozenHint` on the entry once the last few evaluations came back identical; treat it as a prompt to re-trigger the code path, not as proof the value cannot legitimately stay the same.

The expression may use `UloopPausePoint.TryGetCapturedValue("name")` to inspect the latest raw pause-point capture while paused. Each history entry includes the frame and either a stringified value or an explicit error type and message. A throwing expression is recorded as an error and does not stop the Editor update loop. `--max-history` accepts 1 through 100 and drops the oldest entries after the limit.

Watch expressions are in-memory Editor state. A domain reload clears them, so re-register them after `uloop compile`, script recompilation, or an Editor restart. For reliable per-Step changes, keep the expression attached to a continuous pause point on an `Update` or `FixedUpdate` line and use `control-play-mode --action Step`.

## Marker Types

- `uloop enable-pause-point --file --line` patches the already-compiled method at a source line. No code edit or recompile is required.
- `UloopPausePoint.Pause(id)` is a hand-written marker call for code paths that file:line patching cannot reach. Pair it with `uloop enable-pause-point --id <id>` (no `--file`/`--line`). The call does not need to live in committed source — a dynamic-code watcher can fire it (see the next section).
- For ordinary file:line debugging you do not need `UloopPausePoint.Pause` in source. Prefer CLI enable when the target line can be patched.
- Physical Unity message methods (`OnCollisionEnter2D`, `OnTriggerEnter2D`, and similar callbacks) can silently never hit even though the method body demonstrably runs: Unity can resolve a GameObject's message dispatch before the file:line patch is installed, so a GameObject that already existed at enable time may keep calling the pre-patch code. If `await-pause-point`/`pause-point-status` reports `HitCount=0` on a physical callback line, check the response `Warning` for this note, then work around it by recreating the GameObject after enabling, or by embedding `UloopPausePoint.Pause("<id>")` directly in the method body via an id-only marker instead of a file:line one.

## Catching a Runtime Condition with a Dynamic-Code Trigger

A file:line pause point freezes a specific source line. When the moment you need is defined by a runtime condition instead — an animation passing a normalized time, HP reaching zero, an enemy spawning — combine an id-only marker with `execute-dynamic-code`. Timing-sensitive verification such as short motions or one-frame effects cannot be captured by sleeping and then taking a screenshot; this pattern freezes the first frame where the condition holds, without writing any .cs file.

1. Enable an id-only marker: `uloop enable-pause-point --id hit-peak --timeout-seconds 120` (single-shot by default).
2. Run `uloop execute-dynamic-code` to trigger the action and register a watcher on `EditorApplication.update`, then return immediately. The watcher evaluates the condition every frame; on the first frame it holds, it removes itself and calls `UloopPausePoint.Pause("hit-peak")`.
3. Wait on the CLI side: `uloop await-pause-point --id hit-peak --timeout-seconds 120`.
4. While Unity is paused, collect evidence: `uloop screenshot`, state reads with `execute-dynamic-code`, or `control-play-mode --action Step` frame stepping.
5. Resume with `uloop control-play-mode --action Play`.

Example watcher (freeze when the Hit animation passes 30% of the motion):

```csharp
using UnityEngine;
using UnityEditor;
using io.github.hatayama.UnityCliLoop.Runtime;
Animator animator = GameObject.Find("Zombie").GetComponent<Animator>();
// Match the marker's --timeout-seconds so an unmet condition cannot leak the delegate
double deadline = EditorApplication.timeSinceStartup + 120d;
EditorApplication.CallbackFunction watcher = null;
watcher = () =>
{
    if (EditorApplication.timeSinceStartup > deadline)
    {
        EditorApplication.update -= watcher;
        return;
    }
    AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
    if (!state.IsName("Hit") || state.normalizedTime < 0.3f) return;
    EditorApplication.update -= watcher;
    UloopPausePoint.Pause("hit-peak");
};
EditorApplication.update += watcher;
return "watcher registered";
```

Rules for this pattern:

- The dynamic-code body runs synchronously on the main thread. Never poll or sleep inside the snippet — frames stop advancing and the animation freezes with them. Register the watcher and return; the waiting belongs to `await-pause-point`.
- The watcher must unsubscribe itself from `EditorApplication.update` when it fires, and also on a deadline in case the condition never holds — a leaked delegate keeps running until the next domain reload. Match the deadline to the marker's `--timeout-seconds`.
- `UloopPausePoint.Pause(id)` is a public static Runtime API, and dynamic code compiles against the project's assemblies, so the watcher can call it exactly like game code. It fires only while the same id is enabled; otherwise it is a no-op, so a stray watcher cannot pause Unity unexpectedly.
- A single-shot marker disarms after the first hit. To catch repeated occurrences, enable with `--mode continuous` and run `await-pause-point` again after each resume.

## Pausing Right After Simulated Input, Plus N Frames

To freeze the frame where a `simulate-mouse-ui` click or a `simulate-keyboard` key press lands, you do not need a watcher: enable a file:line pause point on the input-consuming line before sending the input. When the pause lands mid-command, the `simulate-*` command returns promptly with `InterruptedByPausePoint=true` (see Line Placement).

For "N frames after the input" (for example, three frames after a key press), advance from that hit with `control-play-mode --action Step` exactly N times — `Step` works right after a hit. Do not compute frame offsets in a dynamic-code watcher (recording `Time.frameCount` and pausing at `recorded + N`): frames keep advancing between CLI commands, so the recorded baseline is race-prone and the pause lands on an unpredictable frame. Reserve the watcher pattern for condition-defined moments; use hit-then-Step for frame-offset positioning.

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

## Line Placement

- Prefer natural runtime points after input has been consumed, such as after a command is accepted, a state value changes, an evaluation step resolves, or a dependent component is updated.
- For frame-specific bugs, target the suspicious state branch or the line right after the mutation you need to freeze (the snapshot is taken before the target line runs).
- A line that runs unconditionally every frame hits on the very next frame, before the input or event you actually wanted to observe arrives. If you need to catch a specific moment, choose a line that only executes conditionally (inside an `if` guarding the event you care about) so the pause point does not fire prematurely. The opposite applies to `continuous` mode paired with a watch expression: the watch only re-evaluates on a paused frame where the marker's line executes, so a conditional line that stops being reached leaves the watch value frozen (see Watch Expressions) — pick a line reached every frame when you need continuous per-Step updates.
- An empty-body loop such as `while (TryMove(0, 1)) { }` has no statement inside the braces, so a pause point on the line right after the loop hits at the loop's condition re-check, not once the loop has actually finished advancing. If you need the state after the loop completes, target a line that is guaranteed to run exactly once after the loop exits, not the loop line itself.
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
