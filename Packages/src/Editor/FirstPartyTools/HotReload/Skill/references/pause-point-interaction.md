# Hot Reload and Pause Points

Both patch shapes discard the original IL and any prior transpiler output on the patched
method, so armed source pause points cannot survive a patch unchanged. Instead of
enforcing exclusivity, every patch transition re-targets them:

- Applying a patch re-resolves each armed marker on the edited method against the
  patched body. A marker whose line still resolves keeps firing at the edited line —
  the apply response reports those ids in a `Warnings` entry and `pause-point-status`
  shows `RetargetedToHotReloadPatch: true`. A marker whose line no longer resolves is
  suppressed instead: the apply response lists it, and status shows
  `SuppressedByHotReload: true` with the reason in `SuppressedByHotReloadReason`.
- Enabling a new pause point reads `--line` as a line of the edited file, before or after
  hot reload alike:
  - A line in a method hot reload has not patched is mapped onto the verified source
    snapshot of the last compile and armed there (`LineBasis: EditedFile`; `ResolvedLine`
    is the edited-file line).
  - A line added or changed since the last compile, or one whose next statement is
    uncompiled, is refused with `PAUSE_POINT_LINE_NOT_COMPILED`. Hot-reload the change
    (then the line is inside a patched body) or run `uloop compile`, and retry.
  - A line inside a patched method's edited body arms the patched body directly. A line
    that would round onto a patched method's compiled body, or whose uncompiled next
    statement is inside a patched method's edited body, is refused with
    `PAUSE_POINT_PATCHED_BY_HOT_RELOAD` instead, which names the edited line range to use.
  - Only a file without a verified source snapshot arms the line as a compiled line
    number, reported as `LineBasis: LastCompiledSource` with a warning to run
    `uloop compile`.
- A method hot reload *added* (an `Added` row) cannot hold a pause point until
  `uloop compile`: it has no compiled body and pause-point cannot arm its shim. Enabling
  a line inside it is refused with `PAUSE_POINT_RESOLVE_FAILED` and a message naming the
  added method; it is never armed on another method instead. Compile first, then enable
  the pause point there.
- `uloop hot-reload --revert-all` (or reverting a method's patch) re-targets armed
  markers back onto the compiled body; a marker whose line no longer resolves there
  stays suppressed with a reason until `uloop compile` and a re-enable.

A pause point enabled with `--persist` is no exception. Its re-arm after a domain reload
goes through the same enable path as a hand-issued `enable-pause-point`, so it re-resolves
against whatever body is live at that moment and re-targets onto a hot-reload patch exactly
as a fresh enable would. A domain reload drops every hot-reload patch, though, so a re-armed
marker normally lands on the compiled body — re-apply the patch afterwards if you want it
back on the edited one.

Suppressed markers are never cleared automatically — they keep their identity and fire
again as soon as a transition restores their line. The practical workflow: iterate with
hot reload and place pause points on edited lines in either order — enable then patch,
or patch then enable. `uloop compile` is needed only when a marker stays suppressed
because its line no longer resolves in any live body.

A pause point inside a Unity physics message (`OnCollisionEnter2D`, `OnTriggerEnter`,
and similar) or inside a method already bound into a delegate before enable can stay
at zero hits even though the body runs: Unity may have resolved that dispatch path
before the marker was armed (the pause-point skill's troubleshooting covers recovery).
Hot-reloading a temporary log line into the same body gives a one-way reachability
check — the log appearing (read it with `uloop get-logs`) proves the body ran even
though the marker missed. The log staying absent proves nothing, because the same
cached dispatch can bypass a hot-reload patch too.
