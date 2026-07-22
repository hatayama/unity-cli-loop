# CapturedVariables Semantics

Read this before interpreting unexpected, missing, or truncated captured values, nested previews, `continuous`-mode history, or when you need live references while Unity is still paused.

## Snapshot Timing

- The snapshot is taken **before** the resolved line executes, exactly like an IDE breakpoint on that line. To inspect a value after an assignment, place the pause point on the following line.
- The pause itself only takes effect at the next frame boundary: the frame that hit the pause point still runs to completion first, so any event that fires later in that same frame (a chained collision, a cascading destroy) has already happened by the time Unity actually stops. Trust `CapturedVariables` (the pre-line snapshot) as evidence for what was true up to the patched line; do not assume the paused state still matches it for events later in the same frame.
- `execute-dynamic-code` during the pause sees the interrupted method's **post-interrupt** state, not this pre-line snapshot. Use `CapturedVariables` for pre-line evidence; use the raw capture API below when you need live references while paused. If you suspect a captured value is stale or wrong, cross-check it against the live scene object with `execute-dynamic-code` (for example reading `transform.position` off the instance found via `UnityObjectPath`) rather than trusting either source alone. `execute-dynamic-code` responses also carry `EditorPaused` and `ActivePausePointId` — these fields appear only while the Editor is paused, so a call made while a pause point still has Unity paused is unambiguous instead of looking like a stale or buggy result.

## Scopes and the `this` Entry

- `Scope` is `Local`, `Parameter`, `InstanceField`, or `This`. `InstanceField` entries come from a reflection walk of the paused instance's declared type, not from the method's IL usage, so a field the method never reads can still appear — and `MaxCapturedVariableCount` still caps the total entry count across all scopes, so a field-heavy type can push some instance fields out of the snapshot. If a specific field you want is missing, read it directly from the live instance instead of waiting on the capped snapshot: while still paused, `UloopPausePoint.TryGetCapturedValue("this")` returns the live `this` reference, so `execute-dynamic-code` can read any field or property off it regardless of the cap.
- The snapshot also includes a synthetic `this` entry (Scope `This`) for the paused instance itself, so you can tell which instance or GameObject was hit via its `UnityObjectPath` and `UnityObjectInstanceId`. For an async or coroutine method it resolves to the original outer instance, not the compiler-generated state machine, and static methods emit no `this` entry. While Unity is still paused, `UloopPausePoint.TryGetCapturedValue("this")` returns the live instance reference (for example so a watch expression can read `transform.position`).
- async and coroutine methods work: hoisted locals and the original `this` fields appear under their normal names.
- Auto-implemented properties are captured as instance fields under the property name (the compiler-generated backing field is un-mangled), so you do not need to rewrite them as explicit fields for verification.
- If the patched method ran off the main thread, values degrade to type names with a `(captured off main thread)` note; the hit itself is still recorded.

## Value Rendering, Previews, and Caps

- Nested previews stop at `MaxCollectionPreviewDepth` (2 levels) below each captured variable: past that, an object or collection renders as type-name-only text instead of expanding — a type name where you expected contents means you hit this cap, not a bug. The budget is counted per captured variable, so reaching a value through `this` costs one extra level compared to reading it as a direct local: `this.CurrentPiece.Origin` bottoms out as a type name, while a `dropped` local holding the same piece expands to `{Kind, RotationState, Origin: {X, Y}}`. When the value you need sits too deep, pick a pause point line where it is a direct local or parameter — as its own top-level entry it starts with a fresh full budget. Primitive leaves (numbers, strings, booleans, and any type that overrides `ToString()`) always render regardless of depth; only nested objects and collections get cut off.
- A value's `Value` string is not always its plain `ToString()`. A materialized collection (`List<T>`, arrays, dictionaries, ...) previews as a shallow JSON array/object instead of the default type-name text. A custom struct/class whose declared type does not override `ToString()` previews the same way — a shallow JSON object of its fields — so you do not need to add a temporary `ToString()` override just to see its contents. A type that does override `ToString()` keeps using that result unchanged. Either kind of preview is capped by depth, element count, and length like any other captured value; the element-count cap (default 10) and the preview's character budget both scale with `enable-pause-point --max-preview-elements`.
- A multidimensional array (`int[,]`, `int[,,]`, ...) previews as `{"Shape":"Int32[2,3]","TotalElements":6,"Elements":[...]}` instead of a bare JSON array, since `Elements` alone would flatten every rank in row-major order with no way to tell it apart from an empty or 1D collection; a `T[]` or jagged `T[][]` array is unaffected and still previews as a plain JSON array.
- `CapturedVariablesTruncated=true` means at least one value was clipped to the length cap or the variable-count cap stopped enumeration; clipped values are still present up to the cap.

## Unity Object Values

- `UnityEngine.Object` values additionally carry `UnityObjectKind` (`SceneObject`, `PrefabAsset`, `Asset`, `RuntimeInstance`, or `Destroyed`), `UnityObjectPath`, and `UnityObjectInstanceId`. These three fields appear only for Unity object values; a non-Unity-object variable (an `int`, a `string`, a plain class) omits all three from the JSON entirely instead of sending them as empty/zero. Check whether `UnityObjectKind` is present to tell the two cases apart. Use the fields as handles for the next dig: a `SceneObject` path feeds `get-hierarchy`/`find-game-objects`, an asset path locates the asset, and the InstanceID works with `execute-dynamic-code`.
- A captured `UnityEngine.Object` value's `Value` string is only the object's `name` — its fields never appear there, and its `ToString()` is not consulted either. A `MonoBehaviour` parameter therefore reads as something like `Block(Clone)`, indistinguishable from every other clone, with none of its `[SerializeField]` values visible. To tell instances apart in snapshots, assign distinguishing names when you create them (for example `gameObject.name = $"Block_{blockId}"`). To read a specific field, stay paused and read it off the live instance with `execute-dynamic-code` (via `UnityObjectPath`/`UnityObjectInstanceId`, or `UloopPausePoint.TryGetCapturedValue("this")` for the paused instance itself).

## Pulling More Than the Default Response Carries

The hit and status responses are push-first and kept lean by default: no field is ever a re-summary of another field, and a variable's `Value` is the only per-entry cost. For a class with dozens of `[SerializeField]` fields, a `continuous` marker's history still multiplies entry count by `MaxHistory` (default 20), which can be a lot of `Value` strings to carry around when you only need to know which names were captured.

Pull only what you need instead of paying for it all up front:

- `--captured-variables names` on `await-pause-point`/`pause-point-status` drops `Value` from every captured variable (including every history frame) and keeps `Name`/`Scope`/`TypeName`. Use it first on a field-heavy class, then fetch specific values afterward.
- `uloop pause-point-status --id <id>` returns the full response again, including every `Value`, whenever you need it — call it plain (no `--captured-variables`) for the complete picture after a lightweight `names` scan.

## Choosing the Right Evidence Source

Three different sources answer three different questions about a captured variable; pick by what you actually need:

| Need | Source | Notes |
|---|---|---|
| A value type's value at capture time | `UloopPausePoint.TryGetCapturedValue("name")` | Faithful: value types are a boxed copy taken at capture time, so this never drifts. |
| A reference type's *live* current state | `UloopPausePoint.TryGetCapturedValue("name")` | The reference itself is live, so the object it points to may have changed since capture (or been destroyed/resumed away). Only available while Unity is still paused. |
| A reference type's state *as it was at capture time* | `uloop pause-point-status --id <id>` | The only faithful source for this: the response is a formatted string snapshot taken at capture time and stored in the registry, so it never drifts and stays retrievable after resume until the next clear or domain reload. |

Capturing a deep copy at hit time was deliberately not adopted: it would cost hot-path performance and risk getter side effects, so the formatted-string snapshot (`pause-point-status`) remains the only way to get capture-time-faithful evidence for reference types.

## Raw Capture API While Paused

While Unity is paused on a hit, `execute-dynamic-code` can read live captured references through `UloopPausePoint`:

- `TryGetCapturedValue(string name)` returns `(bool Found, object Value)` for the latest hit only. When multiple captured variables share the same name, the last one wins.
- `GetCapturedNames()` lists captured variable names from that snapshot.
- `GetCapturedPausePointId()` returns the pause-point id for the held snapshot.

The holder clears when Unity resumes (not when you `Step` while still paused), when the matching pause point is cleared, when a new hit replaces the snapshot, or when PlayMode exits. After resume, `TryGetCapturedValue` returns `Found=false`. Re-enabling the same pause point while still paused (for example to refresh its timeout during a step session) keeps the held references, because a re-enable does not resume Unity.

For a self-progressing game (a board that advances on a timer, an opponent that keeps moving), arranging a specific scenario through real input alone is a race you will usually lose: each `simulate-*` call is a separate CLI round trip, and the gap between two calls is often longer than the game's own tick. Instead, while paused on a hit, use `TryGetCapturedValue("this")` to get the live instance and call its production methods directly to build up the exact state you need, then send real simulated input for only the one action you are verifying. The setup becomes deterministic while the observed action still exercises the real input path.

## Warnings and Marker Freshness

`await-pause-point`'s hit response also carries a top-level `Warning` (omitted when empty): it flags multiple hits, multiple matching logs, or truncated matching logs, so you can tell a single clean hit apart from evidence that needs closer inspection. `MatchingLogs` (log entries whose text contains the marker id) is still embedded, but source-derived ids rarely appear in log text, so treat `CapturedVariables` as the primary variable evidence.

Use `Generation`, `EnabledAtUtc`, and the hit sequence fields from the hit or status response to tell a fresh marker from stale evidence with the same id. `RemainingMilliseconds` and `Expired` are returned directly so you do not need to infer marker lifetime from elapsed time.
