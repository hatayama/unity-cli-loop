# Persisting a pause point across a domain reload

Without `--persist`, every armed pause point disappears the moment Unity reloads the domain:
a `uloop compile`, a script recompilation Unity starts on its own, or entering Play Mode with
Domain Reload enabled. `--persist` records the enable request instead, and re-issues it on the
other side of the reload.

## What is restored

The whole enable request, not the pause point's state: `--file`/`--line` or `--id`, and every
capture option you passed (`--mode`, `--timeout-seconds`, `--max-history`, `--hit-when`,
`--max-preview-elements`, `--max-caller-frames`, `--method`, `--snapshot-timing`). What is not
restored is everything the old session accumulated — hit history, hit counts, and the skipped
and error counters all start from zero, because the re-armed pause point is a new arming of the
same request.

`--timeout-seconds` restarts too. A pause point armed for 60 seconds that survives a reload
gets a fresh 60 seconds from the moment it is re-armed, not the remainder of the original
window.

The record lives in Unity's Editor session state, so it survives reloads but not the Editor
process. Quitting Unity discards it; on the next launch nothing is re-armed.

## When the re-arm happens

On the first Editor update tick after the reload, once the domain is fully built. That is late
enough to apply Harmony patches and read the code-optimization mode, and it means a hit in the
very first frames after a Play entry can be missed. When you need the first frame, arm the
pause point after Play Mode has started rather than relying on the re-arm.

Because the re-arm goes through the ordinary enable path, a re-armed marker resolves against
whatever body is live at that moment, exactly as a hand-issued `enable-pause-point` would.

## Reading the result

`uloop pause-point-status` with no target returns `DomainReloadRearmReport`, one line per
persisted pause point, present only when the last reload re-armed something. A successful line
names the pause point and, for a source pause point, the resolved line and its text. A failure
line starts with `Could not re-arm pause point` and carries the error code and message; it is
also written to the Console as a warning, so `uloop get-logs` finds it.

Each pause point also reports `Persisted: true` in its own status while it is armed with
`--persist`.

The most common re-arm failure is `uloop set-code-optimization` sitting on Release: source
pause points cannot be patched in that mode, so the re-arm fails the same way a fresh enable
would. Switch back to Debug and enable again.

## Turning it off

Enabling the same pause point again without `--persist` clears the flag, and clearing the pause
point removes the record. Nothing else is needed.
