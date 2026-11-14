# A Dialog Found After Sending Is Reported to Unity

Date: 2026-11-14

## Decision

While a sent request is still unanswered, the CLI looks at the Editor 2 seconds after sending and
then at the dialog-check interval (10 seconds, 30 seconds while a pause point holds) until 30
seconds have passed. When it finds a modal dialog, it tells Unity through the internal bridge
command `report-blocking-dialog`.

- Unity then fails a request that has not entered its tool code with `main_thread_blocked`, as soon
  as its main thread has not ticked since the report and has been still for at least 1 second. The
  CLI turns that answer into `BLOCKED_BY_DIALOG` with a Message that says the command `was not run`.
- A request whose tool code has started is left alone, as before.
- The CLI never hangs up on the request itself. It keeps waiting on the same connection for Unity's
  answer.
- A send that ends its own wait for the answer (`compile`, which hangs up after 2 seconds and then
  waits on the compile state) is not watched; see the compile section below.
- While the request holds the command slot, the Editor answers `Busy`. The CLI looks at Unity's
  windows behind that `Busy` once the request has waited for a main thread still for 1 second,
  instead of from the 5-second stall that `uloop status` uses.
- Only a send whose own request Unity accepted and has not answered is watched, from that accept
  until the attempt ends; a retry that Unity accepts again is watched afresh. Unity acks a request
  before it takes the command slot, so a send it then refuses as busy was accepted too: that attempt
  ends at once and never reports. Why: a report fails whichever request waits for the main thread,
  and the one holding the slot may be another command's compile that uloop said keeps running.
- A send refused as busy, while the request holding the slot waits for a main thread that has been
  still for a second, looks for a dialog once. When it finds one it ends with the same
  `BLOCKED_BY_DIALOG` (`was not run`) as the check before sending, instead of retrying and then
  answering `UNITY_SERVER_BUSY`. It only looks and never reports, so the holder is not failed.
- The 30-second limit of ADR 0020 does not change. It still applies when no dialog is reported.

### Compile

`compile` is never reported to Unity: its request stays in Unity after the send hangs up and runs
once the dialog closes, so a report would make Unity drop a compile that uloop says keeps running.
It finds the dialog earlier instead, and still answers with the `had already started` form that the
next `uloop compile` reattaches to:

- When Unity accepted the compile but did not answer within the send's 2 seconds, uloop looks for a
  dialog once before its first compile status query.
- In the compile status wait, fresh or reattached, a status query Unity accepted but did not answer
  makes uloop look at once instead of after a 5-second stall. A query that fails to connect, as
  during a domain reload, still waits for the stall, so a reload does not look at Unity's windows.
- Before reattaching, the probe of the earlier compile looks after such an unanswered query, at most
  once per dialog-check interval. Without a dialog it keeps retrying, so its last answer still
  decides between waiting and sending a new compile.

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

- A command sent just after a dialog opens answers `BLOCKED_BY_DIALOG` (`was not run`) about 6
  seconds after sending (measured on macOS) instead of after about 30.
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
