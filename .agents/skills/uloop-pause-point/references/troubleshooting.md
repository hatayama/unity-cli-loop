# Pause Point Troubleshooting

Read this when a wait times out, `HitCount` stays `0`, or `enable-pause-point` fails.

## Timeout Diagnosis

A `PAUSE_POINT_EXPIRED` error carries the same `Error.Details.Hint` as a timeout plus a shell-neutral `Error.Details.RecommendedNextAction`. Inspect `Error.Details.Status`, `HitCount`, `Generation`, `EnabledAtUtc`, `EditorState`, `ElapsedSinceEnabledMilliseconds`, and `RemainingMilliseconds` to distinguish input not being consumed, stale evidence from an older marker generation, runtime conditions not being met, an id mismatch, or Unity already being paused. `ElapsedSinceEnabledMilliseconds` is measured from `enable-pause-point`, not from `await-pause-point`.

The `--timeout-seconds` countdown freezes only while a pause-point hit holds the Editor paused; the elapsed pause duration is credited back onto the marker's expiry on resume, so inspecting a paused hit for as long as you need does not erode the remaining timeout budget. A manual pause without a hit does not stop the countdown.

## Locating Where Control Flow Stops

To locate where control flow stops before an unhit line, bisect with a second pause point on the method's entry (its first executable line). If the entry point hits while the target line stays at `HitCount=0`, an early return or a branch between the two lines is filtering execution — inspect the guard values in the entry hit's `CapturedVariables` instead of retrying the original line.

## JIT Inlining

Mono can inline very small target methods into callers, and the pause point then never fires even though the line runs. If a line demonstrably runs but the pause point stays unhit and nothing else explains it, move the pause point into the calling method.

## Physics Message Methods and One-Hop Helpers

Physical Unity message methods (`OnCollisionEnter2D`, `OnTriggerEnter2D`, and similar callbacks) can silently miss: a GameObject that already existed at enable time may keep calling the pre-patch code, so `HitCount` stays `0` even though the method body runs. The condition is environment-dependent. On `enable-pause-point --await`, that enable-time patch diagnostic appears as top-level `EnableTimeWarning` (omitted when empty) — it is independent of whether the marker later hits, and it is not folded into hit-time `Warning`. On a non-hit failure, the same text is under `Error.Details.EnableWarning`. The same applies one hop out — a helper called from a physics message method in the same compiled assembly; deeper call chains or callers in other assemblies are not detected by the warning but can fail the same way.

Recovery order:

1. Confirm the body actually ran after arming, via evidence from fresh contact — a stale pre-arm counter or log proves nothing.
2. `clear-pause-point` the marker, `enable-pause-point` it again, and wait for the next fresh contact.
3. Recreate the GameObject after enabling.
4. Embed `UloopPausePoint.Pause("<id>")` in the method body and use an id-only marker.

## Pre-Bound Delegates

A method already bound into a delegate or event before `enable-pause-point` may not fire through that delegate: the pre-bound invocation path can bypass the patch. Workarounds: enable the pause point before the delegate is created, recreate the subscribing GameObject, or re-bind the delegate (e.g. via `execute-dynamic-code`) after enabling.

## Hot-Reload Interaction

`uloop hot-reload` transitions (apply, a newer generation, revert) re-target armed source pause points automatically. `SuppressedByHotReload: true` on a status or wait response means the last transition could not re-target that marker — its line no longer resolves in the code now executing. The reason is in `SuppressedByHotReloadReason` (surfaced as the status `Warning`), and the marker stays armed but silent. Recover by reverting the patch (`uloop hot-reload --revert-all`), editing so the line exists again and re-running `uloop hot-reload`, or running `uloop compile` and re-enabling the marker. `RetargetedToHotReloadPatch: true` is not a problem: it confirms the marker follows the patched body and keeps firing at the edited line.
If the file has active hot-reload patches and the marker landed on an unpatched method, the line resolved against the last compiled source — verify ResolvedMethod, or run 'uloop compile' and re-enable.

## Enable Failures

If enable fails with a "No sequence point found" error even for clearly executable lines, that script's assembly lacks debug sequence points and no line in the file can be patched. Move the pause point to a script in an assembly that carries them, such as a script under `Assets/`.
