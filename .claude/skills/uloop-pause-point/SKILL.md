---
name: uloop-pause-point
description: "Pauses Unity playback at any source file:line without editing code or recompiling, and returns a snapshot of the locals, parameters, and instance fields at that exact frame. Use for bug investigation, PlayMode/E2E verification, checking variable values at a specific frame, or confirming that a code path executed."
---

# uloop await-pause-point

## Quick Check Template

Use this small loop for one representative frame you care about. No source edit and no recompile: the pause point is patched into the already-compiled code and can be enabled mid-PlayMode.

1. Enter PlayMode, then decide before anything else: does the game progress on its own (timers, gravity, spawners)? If yes, pause it right away with `control-play-mode --action Pause`, arrange any scenario state while paused, and add `--resume-play` to the command in step 2 — see Fast-Progressing Games below.
2. Run one foreground command that arms the pause point, fires the input, and waits for the hit:

```bash
uloop enable-pause-point --file Assets/Scripts/Enemy.cs --line 42 --timeout-seconds 60 --await --trigger "simulate-keyboard --action Press --key Space"
```

Digit keys are `Digit0`-`Digit9` or `Numpad0`-`Numpad9` — bare `0`-`9` is rejected.

Before writing a `--trigger` command that differs from the example, load the skill of the tool you are about to trigger. `--trigger` runs a single uloop subcommand in-process only after the marker's arming is confirmed, so the input cannot land before arming and nothing needs to run in the background. `execute-dynamic-code` is one such command: `--trigger "execute-dynamic-code --code-file <path>"`. A short snippet can go inline because quotes keep whitespace as one argument (`--trigger "execute-dynamic-code --code 'return 1;'"`); the tokenizer does not handle escaped or nested quotes, so snippets that contain quotes or are otherwise complex should use `--code-file` — inline `--code` still works for simple snippets. One race does remain: the marker itself can hit before the trigger executes (for example on a line that runs every frame). Commands that cannot run while PlayMode is paused are then rejected — a hit response carries `TriggerFailed: true` at the top level and a `Warning` explaining that no input reached the game (the triggered command's own response stays in `TriggerResult`), so do not treat such a hit as input-driven; a timeout or expired wait that observed the same rejection also carries `Error.Details.TriggerFailed: true` and a Hint pointing at `Details.TriggerResult`. That safety valve does not apply to `execute-dynamic-code`, which still runs while paused: a pre-hit trigger of it will execute. If the trigger command itself is rejected before it runs — its argument parsing fails (`INVALID_ARGUMENT`) or the command name is unknown (`UNKNOWN_COMMAND`) — the wait is abandoned immediately with a `PAUSE_POINT_TRIGGER_FAILED` error instead of waiting out `--timeout-seconds`: the marker stays armed, a PlayMode resumed by `--resume-play` is paused again, and `Error.NextActions` carries the recovery commands — fix the trigger value and re-run the same command. The hit response additionally carries `TriggerResult` with the triggered command's own response (or `Completed: false` — with the reason in `Error` when the trigger was skipped, or with an `Explanation` when the wait settled before the trigger reported its result). The trigger string cannot name another pause-point wait (`await-pause-point`/`enable-pause-point`) and cannot pass `--project-path` — the enclosing command's project is used. `await-pause-point --id <id> --trigger ...` accepts the same flag for a marker enabled earlier. Both commands also accept `--resume-play` — see Fast-Progressing Games.

When the game reaches the line on its own, omit `--trigger`. Fall back to split steps only when the triggering action is not a single uloop command (several inputs in sequence, an external event). `execute-dynamic-code` — whether `--code-file` or inline `--code` — is one command; do not split the wait just to run it. When you do need split steps: run `enable-pause-point` without `--await` in the foreground (its response returning is the arm confirmation), then start `uloop await-pause-point --id <id>` in the background, then send the inputs. Do not approximate arm-waiting with a fixed sleep after a backgrounded enable.

`--timeout-seconds` on enable starts the marker lifetime clock at enable time and is also the deadline `--await` waits against, so size it to cover both the trigger and the wait. Give any agent-shell timeout or yield window more room than `--timeout-seconds`, never the same value: the response prints only when the wait ends, so a wrapper that cuts off at the same boundary reports empty output even though the command completed and printed just past the cutoff. When that happens, do not re-run the enable blindly — read the outcome with `uloop pause-point-status --id <id>`; an expired marker's record stays readable there. Some agent shells cap a foreground call's output window (often around 30 seconds) regardless of any timeout you configure and report the call as completed with empty output; when the wait must exceed that cap, use the split steps above (enable without --await, then await in the background) instead of one long foreground wait.

The response returns the derived marker `Id` (`Assets/Scripts/Enemy.cs:42`), the `ResolvedLine` that was actually patched, the `ResolvedMethod`, and `ResolvedLineText` — the actual source text at `ResolvedLine`. When a statement spans multiple physical lines, `ResolvedLineText` is that whole statement normalized onto one line. When the requested line has no executable statement, the pause point rounds forward to the next executable line — check `ResolvedLine`/`ResolvedLineText` when precision matters, and re-check them after every code edit — a rewritten file shifts line numbers. Use the returned `Id` for every follow-up command. On a hit, this same response already carries `CapturedVariables` and every other field `await-pause-point` would have returned — no separate `await-pause-point` call is needed. `EditorState` on a hit response is a snapshot from the moment of the hit. After `--resume-play`, a successful await response can still show `EditorState.IsPaused: true` from that hit — it is not the Editor's current pause flag. Read the live state with `control-play-mode --action Status`.

3. Read `CapturedVariables` in the hit response first: the locals, parameters, and `this` instance fields at the paused line are already there (see Reading CapturedVariables).
4. While Unity is still paused, capture any additional evidence with `uloop execute-dynamic-code`, `uloop get-hierarchy`, `uloop find-game-objects`, and one screenshot.
5. A `single-shot` marker (the default) disarms itself after the hit, so no clear call is required before moving on. Clearing is still what removes the underlying code patch (a disarmed marker leaves the patch installed), so for `continuous`/`trace` markers, or when the method must run fully untouched again, clear it with `uloop clear-pause-point --id "Assets/Scripts/Enemy.cs:42"` (or `--all` to clear every non-cleared pause point marker (armed, auto-disarmed, or expired) at once) or stop PlayMode. Clearing resumes Play Mode only when the cleared marker (or `--all`) owns the current pause-point hit — the clear response then carries a `Warning` saying it resumed Play Mode. Clearing a different marker leaves that pause in place. A manual pause (`control-play-mode --action Pause` or the Editor pause button) is left untouched by clear.

A hit pauses Unity at the next frame boundary — the patched method and the rest of that frame still run to completion. Only `CapturedVariables` is evidence of the values at the patched line; state read after the pause (for example via `execute-dynamic-code`) may already have advanced past it.

## Parameters

One skill covers several commands, so each command's schema parameters have their own table below.
CLI-only flags (`--await`, `--trigger`, `--resume-play`, `--expect`, `--captured-variables`,
`--captured-variable-names`, `--matching-logs-max-count`) are described in the sections above; only
parameters Unity itself accepts appear here.

### enable-pause-point

Enable a pause point so Unity pauses when that code path is reached, either by a named UloopPausePoint.Pause marker (Id) or by resolving a source file and line (File+Line)

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--id` | string | - | Named pause point id passed to UloopPausePoint.Pause. Mutually exclusive with File/Line |
| `--file` | string | - | Project-relative source file path to patch a pause point into. Requires Line; mutually exclusive with Id |
| `--line` | integer | - | 1-based source line to resolve within File. Requires File; mutually exclusive with Id |
| `--timeout-seconds` | integer | `30` | Seconds before the enable request expires and stops pausing late hits |
| `--mode` | enum | `single-shot` | Capture mode: single-shot pauses once, continuous pauses on every hit, trace records hits without pausing |
| `--max-history` | integer | `20` | Maximum number of captured hit frames to retain (1-100) |
| `--max-preview-elements` | integer | `10` | Maximum number of elements to include in a captured collection's preview (1-1000). The value set at enable time also caps the previews in every later pause-point-status response for that marker; status has no flag to change it. |
| `--max-caller-frames` | integer | `2` | Maximum number of caller stack frames to record on each hit (0-8). 0 disables capture (`CallerFrames` stays an empty array). The value set at enable time also caps every later pause-point-status response for that marker; status has no flag to change it. |
| `--method` | string | - | Optional method simple name or `Type.Method`. When set, `--line` resolves only inside matching methods |

### clear-pause-point

Clear one or all named UloopPausePoint.Pause markers. The response field `ClearedCount` is the number of markers this call transitioned to Cleared: 0 or 1 for `--id`, and the number transitioned for `--all`. Auto-disarmed and expired markers still count as 1; the record stays readable via `pause-point-status` (`StatusBeforeClear` keeps the prior state).

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--id` | string | - | Named pause point id to clear |
| `--all` | flag | - | Clear every non-cleared pause point marker (armed, auto-disarmed, or expired) |

### enable-watch

Register a C# expression to evaluate on each paused Play Mode step

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--id` | string | - | Unique watch expression identifier |
| `--expression` | string | - | C# expression returning an object; UloopPausePoint.TryGetCapturedValue can read the latest raw capture |
| `--max-history` | integer | `20` | Maximum number of watch evaluations to retain (1-100) |

### get-watch-values

Show registered watch expression values and bounded evaluation history

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--id` | string | - | Optional watch expression identifier; omit to return all watches |

### clear-watch

Clear one or all registered C# watch expressions

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--id` | string | - | Watch expression identifier to clear |
| `--all` | flag | - | Clear every registered watch expression |

## Capture Modes and History

Choose the capture mode when enabling a pause point:

- `single-shot` is the default. The first hit pauses Unity and disarms the marker.
- `continuous` pauses Unity on every hit and remains armed.
- `trace` remains armed and records each hit without pausing Unity.
- In every mode, `CapturedVariables` holds the latest hit and `CapturedVariableHistory` holds only strictly older frames, so with a single hit the history is empty (for `single-shot` it always is). When the latest-hit frame is excluded, `CapturedVariableHistoryNote` explains that the latest hit's variables are in `CapturedVariables`.
- Prefer tracing a line that executes conditionally: a line that runs every frame fills the capped history within a fraction of a second and drops everything recorded before it.
- On every Hit, the response carries a StatusNote. In trace mode it says Play Mode was not paused (the marker fired while the game kept running). In single-shot and continuous it says Unity pauses at the next frame boundary, so live reads after the hit reflect post-frame state; use CapturedVariables for at-line values.
- An Expired response carries a RecommendedNextAction: re-enable the pause point with a longer --timeout-seconds (default 30) and trigger the code path again; clearing the expired marker first is not required.
- Expired responses include `MethodEntryCount`: `0` means the armed method was never invoked; a positive value means the method ran but never reached the armed line (branch not taken). For `async` and iterator methods the count is state-machine `MoveNext` entries, so each `await` resumption increments it.
- For an already-hit `continuous` or `trace` marker, `await-pause-point` waits for a **new** hit after the wait starts (`LastHitSequence` advancing). It does not return the stale hit that is already present. Read the current hit with `pause-point-status` instead. If await times out while waiting for that new hit, the error stays `PAUSE_POINT_WAIT_TIMEOUT` and `Details.Hint` tells you to pass `--resume-play` (or resume Play Mode) so another hit can occur. A freshly enabled marker (including `enable-pause-point --await`) has no prior hit, so the first hit satisfies the wait as before.

`--max-history` defaults to 20 and accepts values from 1 through 100. When the limit is exceeded, the oldest frames are dropped and `HistoryDroppedCount` reports how many were removed. `pause-point-status` returns the current `Mode`, `MaxHistory`, history frames, and dropped count.

Re-enabling the same `file:line` replaces the marker instead of updating it: the
marker starts a new generation and the previous `CapturedVariableHistory` is
discarded, so a re-enable never carries frames over. Read the history you still
need with `pause-point-status` before re-enabling (for example before raising
`--max-preview-elements` or changing the mode).

To inspect value changes one Editor Step at a time, pair a `continuous` marker with a `control-play-mode --action Step` + `pause-point-status` loop — see [references/captured-variables.md](references/captured-variables.md) for the loop and its caveats.

For multi-step verification, avoid repeating enable→await→clear cycles with the default single-shot mode: pass `--mode continuous` to `enable-pause-point`, or enable several file:line markers at once — markers are independent and can stay armed simultaneously.

## Reading CapturedVariables

Every hit response embeds `CapturedVariables`: the method's in-scope locals, its parameters, and the `this` instance fields, captured at the exact moment execution reached the patched line. Values are point-in-time strings, not live references, so they stay valid as evidence even after Unity resumes.

- The snapshot is taken **before** the resolved line executes, exactly like an IDE breakpoint on that line. To inspect a value after an assignment, place the pause point on the following line.
- `Scope` is `Local`, `Parameter`, `InstanceField`, or `This`. The synthetic `this` entry identifies which instance or GameObject was hit via `UnityObjectPath` and `UnityObjectInstanceId`; `UnityEngine.Object` values carry the same handle fields for follow-up digs with `get-hierarchy`, `find-game-objects`, or `execute-dynamic-code`.
- `--captured-variables` defaults to `full`, which keeps each captured entry's `Value` (capture caps still apply — watch `CapturedVariablesTruncated`). When that dump is noisy, trim it with `--captured-variable-names` or `--captured-variables names`, both detailed in the bullets below.
- `--captured-variables names` on `await-pause-point`/`pause-point-status` drops every `Value` and keeps `Name`/`Scope`/`TypeName` — use it first on field-heavy classes, then fetch full values with a plain `pause-point-status` call.
- When the response would be dominated by variables you do not need, pass `--captured-variable-names velocity,this` (comma-separated, exact match on `Name`) to keep only those entries; it composes with `--captured-variables full|names`. `CapturedVariablesTruncated` reports truncation at Unity-side capture time and is independent of this name filter — it can stay `true` even when every listed variable is complete, if a truncated variable was excluded by the filter. In that case the CLI sets `CapturedVariablesTruncatedNote`. Requested names that matched nothing are listed in `CapturedVariableNamesNotFound`, so a partial match is visible without comparing the response against the request by hand.
- Pass `--expect 'name=value'` (repeatable; on `await-pause-point`, `enable-pause-point --await`, and `pause-point-status`) to have the CLI compare captured variables against expected values; the response includes an `Expectations` array and `AllExpectationsPassed`, so you do not need to eyeball the JSON. Matching is string equality against the serialized value. On `pause-point-status` a marker that has not been hit yet reports each expectation as not found, and the verdict never changes the exit code — a polling loop reads `AllExpectationsPassed`, not the exit status.
  Serialized `value` forms that match in practice (string equality against `CapturedVariables[].Value`):
  - bool: `True` / `False` (C# form, capital first letter)
  - float: `7` when the value is exactly an integer (not `7.0`)
  - Vector2/3 and custom structs via `ToString()`: `(2.31, 6.61)` (one space after each comma)
  - enum: `Grass` (member name only)
  - `List<int>` / arrays: `[19]`, `[0,1,2,3]` (numeric elements unquoted)
  - `List<Vector2Int>` and other element-`ToString()` collections: `["(9, 3)","(9, 2)"]` (elements quoted)
  When unsure, hit once and copy the `Value` string from `CapturedVariables` into `--expect` verbatim.
- Collection values (arrays, `List<T>`, dictionaries, plain objects) render as a JSON preview capped at 10 elements by default. When the elements you need sit past that cap (a 10x20 grid, a long list), re-enable with `--max-preview-elements <n>` (1–1000). The value set at enable time also caps the previews in every later `pause-point-status` response for that marker — status has no flag to change it.
- While Unity is still paused, `UloopPausePoint.TryGetCapturedValue("name")` (and `"this"`) returns live captured references for `execute-dynamic-code`; the return is a `(bool Found, object Value)` tuple, and the holder clears on resume. (file:line marker hits only — id-only markers store no capture) These are **live objects in their frame-completed state, not snapshots** — use them only to dig further into objects that are still alive, never to reconstruct what a value was at the paused line.

`CallerFrames`: caller stack frames showing how execution reached the marker, nearest caller first, capped by `--max-caller-frames` (default 2, range 0–8; 0 records none and leaves an empty array) — top-level for the latest hit in `pause-point-status` / `await-pause-point` responses, and on every `CapturedVariableHistory` frame in all hit-carrying responses (`enable-pause-point` / `clear-pause-point` payloads have no top-level capture, so their frames appear in the history only). Always present (empty array when no managed callers were captured — for example when the marker's method is called directly by the engine, or when `--max-caller-frames 0`). Each frame has `Method`; `File` (project-relative, forward slashes) and `Line` are omitted when debug symbols are unavailable. When they are omitted, `Note` distinguishes a hot-reload **or pause-point instrumentation** dynamic method, a frame with no debug symbols, and a source path outside the Unity project. Frame-selection rules: [references/captured-variables.md](references/captured-variables.md).

For snapshot timing, preview/truncation caps, Unity-object `Value` semantics, capture-time vs live evidence, `Warning`/`MatchingLogs`, marker freshness, caller frames, and the raw capture API, read [references/captured-variables.md](references/captured-variables.md).

## Watch Expressions

Use watch expressions when a value should be re-evaluated automatically after each paused Play Mode Step:

```bash
uloop enable-watch --id "speed" --expression "UloopPausePoint.TryGetCapturedValue(\"speed\").Value" --max-history 20
uloop get-watch-values --id "speed"
```

A watch evaluates only on a changed, paused frame, and a domain reload clears all watches. For the full evaluation rules (baseline, ordering, duplicate ids, `ValueFrozenHint`, error handling), read [references/watch-expressions.md](references/watch-expressions.md).

## Marker Types

- `uloop enable-pause-point --file --line` patches the already-compiled method at a source line.
- `UloopPausePoint.Pause(id)` is a hand-written marker call for code paths that file:line patching cannot reach. Pair it with `uloop enable-pause-point --id <id>` (no `--file`/`--line`). The call does not need to live in committed source — a dynamic-code watcher can fire it (see the next section).
- The id-only marker records the hit itself and nothing more: `CapturedVariables` is always empty, and no raw capture is stored, so `TryGetCapturedValue`/`GetCapturedNames` return nothing for these hits. When you need variable values at an id-only marker, read the target objects directly with `execute-dynamic-code` while the Editor is paused, or use a file:line marker instead.
- For ordinary file:line debugging you do not need `UloopPausePoint.Pause` in source. Prefer CLI enable when the target line can be patched.
- Physics message methods (`OnCollisionEnter2D`, `OnTriggerEnter2D`, and similar callbacks), helpers they call, and methods already bound into delegates or events can miss hits on GameObjects that existed before enable — `enable-pause-point` warns where it can detect this. Confirmation steps and recovery order: [references/troubleshooting.md](references/troubleshooting.md). A one-way cross-check: hot-reload a temporary log line into the method (`uloop hot-reload`) and re-trigger — the log appearing proves the body ran even though the marker missed; the log staying absent proves nothing, because the same cached dispatch can bypass the hot-reload patch too.

## Catching a Runtime Condition with a Dynamic-Code Trigger

When the moment you need is defined by a runtime condition instead of a source line — HP reaching zero, an animation passing a threshold, an enemy spawning — enable an id-only marker, register an `EditorApplication.update` watcher via `execute-dynamic-code` that calls `UloopPausePoint.Pause("<id>")` on the first frame the condition holds, and wait with `uloop await-pause-point --id <id>`. No .cs file is written.

Before using this pattern, read [references/condition-triggered-pause.md](references/condition-triggered-pause.md) for the full workflow, a complete watcher example, and the safety rules (never sleep in the snippet, watcher self-unsubscription, deadline handling).

## Pausing Right After Simulated Input, Plus N Frames

To freeze the frame where a `simulate-mouse-ui` click or a `simulate-keyboard` key press lands, you do not need a watcher: enable a file:line pause point on the input-consuming line before sending the input. When the pause lands mid-command, the `simulate-*` command returns promptly with `InterruptedByPausePoint=true` (see Line Placement).

For "N frames after the input" (for example, three frames after a key press), advance from that hit with `control-play-mode --action Step` exactly N times — `Step` works right after a hit. Do not compute frame offsets in a dynamic-code watcher: frames keep advancing between CLI commands, so a recorded `Time.frameCount` baseline is race-prone. Reserve the watcher pattern for condition-defined moments; use hit-then-Step for frame-offset positioning.

## Hit Preconditions

A pause point hits only when control flow reaches the patched line (or the `Pause(id)` call). `simulate-keyboard` returning `PressEdgeObserved=true` means the input edge was observed, not that your target game logic has reached the pause line yet.

If a `simulate-*` command instead returns a failure whose message says PlayMode is paused, suspect a pause point hit rather than an unrelated failure: an active pause point can make PlayMode paused mid-simulation, and the `simulate-*` call surfaces that as a preflight failure. The failure response names the responsible marker in `RejectedByActivePausePointId`. Check `uloop pause-point-status --id <id>` first to confirm the hit before treating it as a bug in the simulated action itself.

## When To Use

- Use a pause point as the standard frame proof whenever state-changing simulated input, physics, or a UI transition is being verified — including E2E passes where transition-frame evidence adds confidence even when durable state, logs, or screenshots could confirm the final result.
- Use it before reaching for `Time.timeScale`, sleeps, repeated polling, or after-the-fact `execute-dynamic-code`; those can supplement the paused-frame proof but are not substitutes. `simulate-* Success=true`, generic action logs, testing-only counters, and `Time.timeScale` changes are not paused-frame proof either.
- If the value you need is a method local, an intermediate calculation, or a branch reason that `execute-dynamic-code` cannot reach, put the pause point on that line: `CapturedVariables` records it without touching the source.
- Skip this only for ordinary persistent-state checks that do not validate simulated input delivery, event ordering, or transition-frame fidelity.

## Timeout Checks

If this command times out, the patched line was not reached while the command waited. Read `Error.Details.Hint` first: it names the most likely cause when PlayMode is not running, Unity is already paused, or the marker was enabled but never hit. A wait timeout that is not waiting for a new hit on a continuous/trace marker auto-clears the marker; `Error.Details.MarkerClearedByThisCommand` is true when this command did that. A `PAUSE_POINT_EXPIRED` error means the marker's own `enable-pause-point --timeout-seconds` window (measured from enable, not from wait) ran out first — clear and re-enable the pause point using the returned `Id` and `TimeoutSeconds`. When `--trigger` was passed, the expired envelope also carries `Error.Details.TriggerResult` (with `Completed: false` and no `Error` field when the trigger's outcome was still unknown at expiry) — such a result carries an `Explanation` field stating that the wait settled first and the trigger may still have delivered its input. The countdown freezes while a hit holds the Editor paused; a manual pause without a hit does not stop it.

`enable-pause-point` works on hot-reload patched methods: the marker resolves against
the patched body, and `RetargetedToHotReloadPatch: true` in the response confirms it is
armed on the edited code. Methods the reload did not patch are the opposite case: --line on them resolves against the last compiled source, not the edited file, so line drift from the edit can silently arm a different method. Pass `--method` with the simple method name or `Type.Method` to keep `--line` inside that method and fail instead of arming a neighbor. The response carries a Warning when this applies — check ResolvedMethod and ResolvedLineText before trusting the marker, or run 'uloop compile' and re-enable. When the statement text at the resolved line is identical in the edited file, the Warning says so and no manual comparison is needed. When the compiled statement at the resolved line differs from the edited file, the response also includes a compiled-line drift Warning and RecommendedNextAction. When enable fails with `PAUSE_POINT_RESOLVE_FAILED` and the file still has active hot-reload patches, `ResolvedMethod` and `ResolvedLineText` are empty — do not look for them. Follow `RecommendedNextAction`: recompute `--line` against the last compiled source, or run `uloop compile` and re-enable. `CapturedVariables` never includes fields added by hot reload (their values live in a side table); enable-pause-point warns when the resolved type has any.
`PAUSE_POINT_PATCHED_BY_HOT_RELOAD` is returned only when the
requested line cannot be mapped onto the patched body — the file's line map is stale or
the patch belongs to a superseded hot-reload generation. Pick a line inside the edited
method body, run `uloop hot-reload --revert-all`, or run `uloop compile`, then retry.
When the compiled line range of the patched method is known, the failure message also reports it, so you can see how far the edited file's line numbers have shifted from the compiled source.
`SuppressedByHotReload: true` on a status response means a later hot-reload transition
(apply, a newer generation, or revert) could not re-target the armed marker; the reason
is in `SuppressedByHotReloadReason` and surfaced as the status `Warning`. The marker is
not cleared — it fires again once a transition restores its line, or after
`uloop compile` and a re-enable.

Use `uloop pause-point-status --id "Assets/Scripts/Enemy.cs:42"` only when you need to confirm the marker is armed or inspect the current hit state.

For the full diagnosis flow — the `Error.Details` status fields, bisecting with a second marker on the method entry, JIT inlining, physics-callback misses, and delegate bypass — read [references/troubleshooting.md](references/troubleshooting.md).

## Fast-Progressing Games

When the game advances on its own (timers, gravity, spawners), any state you arrange with PlayMode live can be consumed by the game before your next command arrives — each CLI round-trip costs real seconds. Freeze the player loop with `control-play-mode --action Pause`, build the scenario while paused, then confirm arming, resume, and fire the input in one call:

```bash
uloop enable-pause-point --file Assets/Scripts/Enemy.cs --line 42 --timeout-seconds 60 \
  --await --resume-play --trigger "simulate-keyboard --action Press --key Digit3"
```

Digit keys are `Digit0`-`Digit9` or `Numpad0`-`Numpad9` — bare `0`-`9` is rejected.

`--resume-play` (requires `--await`; `await-pause-point` accepts it too) resumes a paused PlayMode after the marker's arming is confirmed and before `--trigger` is dispatched. Size `--timeout-seconds` generously when arming while paused: a manual Pause does not freeze the marker countdown (see Timeout Checks).

For `ResumePlayResult` semantics, why `Time.timeScale = 0` is not a substitute for pausing, the residual post-resume race, and why direct state writes may not stick while paused, read [references/fast-progressing-games.md](references/fast-progressing-games.md).

## Line Placement

- Prefer natural runtime points after input has been consumed, such as after a command is accepted, a state value changes, an evaluation step resolves, or a dependent component is updated.
- For frame-specific bugs, target the suspicious state branch or the line right after the mutation you need to freeze (the snapshot is taken before the target line runs).
- A line that runs unconditionally every frame hits on the very next frame, before the input or event you actually wanted to observe arrives. If you need to catch a specific moment, choose a line that only executes conditionally (inside an `if` guarding the event you care about) so the pause point does not fire prematurely. The opposite applies to `continuous` mode paired with a watch expression: the watch only re-evaluates on a paused frame where the marker's line executes, so a conditional line that stops being reached leaves the watch value frozen (see Watch Expressions) — pick a line reached every frame when you need continuous per-Step updates.
- To verify held input (WASD and similar) against a line that runs every frame, do **not** use `--trigger`: call `simulate-keyboard --action KeyDown --key W` first so the key is already held, then arm the marker (optionally with `--await`). Reversing that order — arm then trigger KeyDown — races the every-frame hit before the hold is applied. Release with `KeyUp` or `ReleaseAll` when done.
- When every reachable line around the state change you want runs unconditionally every frame, with no existing `if` to hang the pause point on, move the moment you want to observe into a conditional block: `if (<event condition>) { <mutation>; UnityEngine.Debug.Assert(<postcondition of the mutation>); }`, then target the pause point at the assert line: it executes only when the event actually happens, states the mutation's postcondition, and can stay in the codebase after the investigation. Use `UnityEngine.Debug.Assert`, not `System.Diagnostics.Debug.Assert`: a failed System.Diagnostics assert never reaches the Unity Console, so `get-logs` cannot observe it.
- An empty-body loop such as `while (TryMove(0, 1)) { }` has no statement inside the braces, so a pause point on the line right after the loop hits at the loop's condition re-check, not once the loop has actually finished advancing. If you need the state after the loop completes, target a line that is guaranteed to run exactly once after the loop exits, not the loop line itself.
- After a hot reload of the target file, add `--method` with the method's simple name or `Type.Method`: `--line` on methods the reload did not patch resolves against the last compiled source, not the edited file, so an edited-file line number can silently arm a neighboring method — `--method` keeps the line inside the intended method and fails instead. Patched methods already resolve against the edited file; `--method` is harmless there.
- Enable pause points after PlayMode is running: entering PlayMode with Domain Reload enabled silently removes every source pause point (`enable-pause-point` warns when this applies); with Domain Reload disabled this does not happen.
- Targeting the line that directly handles simulated input is safe: when the pause lands mid-command, the `simulate-*` command returns promptly with `InterruptedByPausePoint=true` instead of running to completion, and `simulate-mouse-ui` additionally states in `Message` whether the pointer event was already dispatched before the pause. Prefer a line after the input is consumed when you want the settled result state rather than the input-handling moment.
- Use separate pause points on distinct lines for strict phases, for example input read, state updated, and result committed, instead of one broad pause point.

## Requirements & Safety

- **Release editors switch to Debug automatically.** When the Editor's Code Optimization mode is Release, `enable-pause-point` switches it to Debug and recompiles before arming (large projects can take a while). The response `Warning` records the switch. The Debug setting does not survive an Editor restart, including `uloop launch -r` — it reverts to the Preferences > General > Code Optimization On Startup value. If automatic recovery fails, enable is rejected with `ErrorCode: PAUSE_POINT_RELEASE_CODE_OPTIMIZATION`; follow `RecommendedNextAction`.
- **Patches do not survive compiles or domain reloads.** Any script compile or domain reload removes every source pause point together with its marker, leaving the code exactly as compiled. Re-enable after the reload finishes. This is also why an interrupted CLI session never leaves stale patches behind.
- **`uloop compile` while PlayMode is running triggers this same domain reload.** The reload also resets the running PlayMode session itself, so the game state you had arranged (scene, spawned objects, progress) is gone too. Re-enter PlayMode and re-enable the pause point; do not assume the paused scenario is still intact.
- If `enable-pause-point` fails, branch on the failure `ErrorCode` and follow `RecommendedNextAction`; `Message` explains the rejection in prose. Codes: `INVALID_ARGUMENT` (fix the rejected argument and re-run), `PAUSE_POINT_RELEASE_CODE_OPTIMIZATION` (automatic Debug switch and recompile did not leave the Editor in Debug; retry after a successful compile), `PAUSE_POINT_RESOLVE_FAILED` (the file:line could not be mapped to a patch location; when the file has active hot-reload patches, `ResolvedMethod` and `ResolvedLineText` stay empty — follow `RecommendedNextAction` instead of those fields), `PAUSE_POINT_PATCH_FAILED` (the resolved method cannot be patched). Enable-failure specifics (for example "No sequence point found") are covered in [references/troubleshooting.md](references/troubleshooting.md).
- For scripts under `Packages/`, pass the package-id form of the path — `Packages/<package-id>/...`, exactly as the Unity Project window and console stack traces show it. The physical checkout path of an embedded package does not resolve.
