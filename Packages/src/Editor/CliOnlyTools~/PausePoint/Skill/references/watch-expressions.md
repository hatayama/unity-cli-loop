# Watch Expressions

Use watch expressions when the value should be evaluated automatically after each paused Play Mode Step:

```bash
uloop enable-watch --id "speed" --expression "UloopPausePoint.TryGetCapturedValue(\"speed\").Value" --max-history 20
uloop get-watch-values --id "speed"
```

## Evaluation Rules

`enable-watch` compiles the C# expression once, evaluates it immediately for a baseline, and then evaluates it once per changed `Time.frameCount`, but only while Play Mode is running and the Editor is paused (each hit pause and each `Step`); nothing is recorded while the game runs unpaused. Multiple watches run in registration order. `enable-watch` rejects a duplicate id instead of overwriting; clear with `clear-watch --id <id>` before re-registering a changed expression. `clear-watch --id <id>` removes one watch; `clear-watch --all` removes all watches. `get-watch-values` without `--id` returns every registered watch.

Because a watch only re-evaluates on a changed, paused frame, a value that looks stuck across several reads usually means no new paused frame has occurred — most often the linked pause point has not been hit again (a marker on a conditional line freezes after its first hit; see Line Placement in SKILL.md). `get-watch-values` surfaces this as a non-empty `ValueFrozenHint` on the entry once the last few evaluations came back identical; treat it as a prompt to re-trigger the code path, not as proof the value cannot legitimately stay the same.

The expression may use `UloopPausePoint.TryGetCapturedValue("name")` to inspect the latest raw pause-point capture while paused. Each history entry includes the frame and either a stringified value or an explicit error type and message. Watch values are serialized with the same preview rules as pause point `CapturedVariables`: collections become compact JSON previews (e.g. `[0,1,2]`), and types with a custom `ToString()` keep their `ToString()` form. Previews share the capture-side caps (10 elements, 1024 characters); a clipped value sets `Truncated: true` on the history entry, and the freeze hint on truncated previews warns that changes beyond the caps are invisible. A throwing expression is recorded as an error and does not stop the Editor update loop. `--max-history` accepts 1 through 100 and drops the oldest entries after the limit.

## Lifetime

Watch expressions survive a domain reload. They are saved in Editor session state, so `uloop compile`, a script recompilation, and a Play entry with Domain Reload enabled all keep them registered — the expression is recompiled after the reload and evaluation starts from a fresh baseline, so history collected before the reload is gone.

A watch whose expression no longer compiles after the reload (its type was renamed or deleted) is dropped, and the next `enable-watch` or `get-watch-values` response reports it in `Warning` with the id and the compiler message.

Watch expressions do not survive an Editor restart — session state ends with the Editor process. For reliable per-Step changes, keep the expression attached to a continuous pause point on an `Update` or `FixedUpdate` line and use `control-play-mode --action Step`.
