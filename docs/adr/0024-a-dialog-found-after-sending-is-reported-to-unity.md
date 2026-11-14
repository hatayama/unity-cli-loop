# A Dialog Found After Sending Is Reported to Unity

Date: 2026-11-14

## Decision

While a sent request is still unanswered, the CLI looks at the Editor 2 seconds after sending and
then at the dialog-check interval (10 seconds, 30 seconds while a pause point holds) until 30
seconds have passed. When it finds a modal dialog, it tells Unity through the internal bridge
command `report-blocking-dialog`, which carries the name of the command the send was for.

- Unity then fails a request of that command that has not entered its tool code, and that its
  sender's disconnect would cancel, with `main_thread_blocked`, as soon as its main thread has not
  ticked since the report and has been still for at least 1 second. Every other request keeps the
  30-second answer. A report without a name counts as no report. The
  CLI turns that answer into `BLOCKED_BY_DIALOG` with a Message that says the command `was not run`.
- A request whose tool code has started is left alone, as before.
- The CLI never hangs up on the request itself. It keeps waiting on the same connection for Unity's
  answer.
- A send that ends its own wait for the answer (a default `compile`, which hangs up after 2 seconds and then
  waits on the compile state) is not watched; see the compile section below.
- While the request holds the command slot, the Editor answers `Busy`. The CLI looks at Unity's
  windows behind that `Busy` once the request has waited for a main thread still for 1 second,
  instead of from the 5-second stall that `uloop status` uses.
- Only a send whose own request Unity accepted and has not answered is watched, from that accept
  until the attempt ends; a retry that Unity accepts again is watched afresh. Unity acks a request
  before it takes the command slot, so a send it then refuses as busy was accepted too: that attempt
  ends at once and never reports. The watch also reports only when the Editor names a command of the
  send's name as the slot holder, so it does not send a report that could fail nothing.
- Invariant: a request that outlives its sender's disconnect (a default `compile`, and a PlayMode
  `run-tests` that keeps the Enter Play Mode settings) is never failed early by a report. Those are
  the only requests uloop may hang up on and say keep running once the dialog closes, so this keeps
  that true. Unity enforces it from the request itself, not the CLI by name or timing: the command
  slot can change hands between the CLI's look and its report, a busy answer can arrive late, and a
  `compile --no-wait-for-domain-reload`, which waits for its answer and is reported, shares the name
  `compile` with a default compile. Such a run-tests keeps the 30-second answer.
- When Unity answers `main_thread_blocked` at most 3 seconds after the start of the look whose
  dialog the watch of that attempt reported, the CLI names that dialog in `BLOCKED_BY_DIALOG`
  without looking again. Freshness counts from the look because the error names what that look saw.
  On macOS the watch's look, one look at Unity's windows, takes about a second, and Unity fails a
  reported request at its next check, which runs every second, so an answer the report caused
  arrives about 2 seconds after the look. Unity fails it only while its main thread has not run
  since the report, so the dialog the look saw usually still holds it. An older answer may have
  another cause: Unity's own 30-second answer, or a second dialog that opened as the reported one
  closed, without the main thread running in between, which can happen at any point of the watch and
  goes unseen for up to a look interval. So freshness from the look, not the 30-second window,
  decides. A later look that finds no dialog, or a report Unity refuses, drops the dialog; a look or
  report cut short because Unity answered changes nothing. Without a fresh reported dialog, and for
  `UNITY_EDITOR_UNRESPONSIVE`, the CLI looks once as before, through the `uloop status` probe. On
  macOS that probe takes about 2 seconds, because it asks the Editor again after a second before it
  looks at Unity's windows; that is the time the reuse saves.
- A send refused as busy, while the request holding the slot waits for a main thread that has been
  still for a second, looks for a dialog once. When it finds one it ends with the same
  `BLOCKED_BY_DIALOG` (`was not run`) as the check before sending, instead of retrying and then
  answering `UNITY_SERVER_BUSY`. It only looks and never reports, so the holder is not failed.
  This covers sends that retry on busy; hot reload hands busy straight back to its own wait,
  which already watches for dialogs.
- The 30-second limit of ADR 0020 does not change. It still applies when no dialog is reported.

### Compile

A default `compile` is never reported to Unity: its request stays in Unity after the send hangs up
and runs once the dialog closes, so it reads no answer to a report, and Unity would not fail it early
anyway. `compile --no-wait-for-domain-reload` waits for its answer like other commands and is reported.
A default `compile` finds the dialog earlier instead, and still answers with the `had already started`
form that the next `uloop compile` reattaches to:

- When Unity accepted the compile but did not answer within the send's 2 seconds, uloop looks for a
  dialog once before its first compile status query.
- In the compile status wait, fresh or reattached, a status query Unity accepted but did not answer
  makes uloop look at once instead of after a 5-second stall. A query that fails to connect, as
  during a domain reload, still waits for the stall, so a reload does not look at Unity's windows.
- Before reattaching, the probe of the earlier compile looks after such an unanswered query, at most
  once per dialog-check interval. Without a dialog it keeps retrying, so its last answer still
  decides between waiting and sending a new compile.

### Pause-Point Trigger

A pause-point wait sends its `--trigger` through the same dispatch, so the trigger's own send can be
the one Unity fails with `main_thread_blocked`. The wait reads that result from the trigger's error
envelope (`BLOCKED_BY_DIALOG` with `Details.CommandNotRun`) and ends at once, saying the trigger was not
run and to wait again with the same `--trigger`, instead of waiting until its own 30-second-interval
watch finds the dialog and telling the agent to drop a trigger that was never run. When its own watch
finds the dialog first and the look's report shows the trigger holding the slot while it waits for the
main thread, the wait reports the dialog to Unity under the trigger's name and waits up to 6 seconds for
the trigger's answer (Unity's check, then the trigger's own second look, take about 3 on macOS); a
`main_thread_blocked` answer ends the wait the same way, naming the watch's dialog when the trigger's
own look found none. A `main_thread_blocked` answer without any dialog, as after a 30-second stall,
abandons the wait as a trigger that did not run, the way a rejected trigger does. Why ask Unity instead
of judging from the look: the look is one moment, and the dialog can close and let the trigger run
before uloop exits. A request that outlives the disconnect, such as a default `compile`, is not failed
by the report, so no answer comes. Without that answer, and in every other case, the wait keeps the
`had already started` form, which never risks sending the trigger twice.

## Context

A dialog that opened after the check before sending was only noticed
when Unity's 30-second watchdog failed the waiting request: a `get-logs` sent just after a dialog
opened took 32.5 seconds to answer on macOS.

## Considered Options

- The CLI stops waiting by itself: rejected. A request sent with `cancelOnClientDisconnect=false`
  keeps running in Unity after the CLI leaves, so the command could still run later, after the agent
  has moved on.
- Unity notices the dialog by itself and fails the request: rejected. Unity can report its own
  dialogs only from 6000.7 on, so older Editors would keep the 30-second answer.
- Lower the 30-second threshold itself: rejected for the reasons ADR 0020 gives; ordinary
  main-thread work such as scene saves and imports would fail commands.
- A per-request token so that a report fails one request only: rejected. Every request waiting for
  the main thread is held by the same dialog and would get the same answer 30 seconds later.

## Consequences

- A command sent just after a dialog opens answers `BLOCKED_BY_DIALOG` (`was not run`) about 3
  seconds after sending (measured on macOS; about 6 before the answer reused the reported dialog)
  instead of after about 30.
- Accepted for those 2 seconds: within the 3 seconds after the look, the dialog it saw may close, be
  replaced by another, or give way to the rest of the code that opened it, all without the main
  thread running, and the error then names a dialog that is gone. The answer still means the command
  was not run, so running it again stays safe, and `uloop get-dialogs` shows what is open now.
- `compile`, and `run-tests` that compiles first, answer `BLOCKED_BY_DIALOG` (`had already started`)
  about 3 seconds after sending, and about 7 seconds when they reattach to an earlier compile
  (measured on macOS), instead of 13 to 23 seconds.
- An older Unity package answers the report with an error; the CLI then stops reporting for that
  request, and the request keeps the 30-second answer. `compile` needs nothing from the package and
  answers early with it too.
- On Linux before Unity 6000.7 the CLI cannot look at Unity's windows, so other commands keep the
  30-second answer and `compile` keeps its earlier answer.
- A command sent while a dialog-stopped compile holds the slot answers `BLOCKED_BY_DIALOG` in about
  1 second (measured on macOS) instead of `UNITY_SERVER_BUSY` after about 10 seconds of retries.
- When the main thread stops for 1 second or more right after a dialog closes, for some other work,
  a request that was waiting may fail once; `SafeToRetry` says it can be run again.
- `report-blocking-dialog` is a new internal bridge command, so the protocol version does not change.

## Revisit When

- A report fails a request that a dialog did not hold: raise the 1-second floor
  (`MainThreadEntryWatchdog.DialogReportedStallSeconds`) or the 2-second delay before the first look
  (`earlyDialogLookDelay`).
