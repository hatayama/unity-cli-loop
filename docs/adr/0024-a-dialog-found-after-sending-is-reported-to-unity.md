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
  waits on the compile state) is not watched.
- While the request holds the command slot, the Editor answers `Busy`. The CLI looks at Unity's
  windows behind that `Busy` once the request has waited for a main thread still for 1 second,
  instead of from the 5-second stall that `uloop status` uses.
- The 30-second limit of ADR 0020 does not change. It still applies when no dialog is reported.

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
- `compile`, and `run-tests` that compiles first, keep their earlier behavior: the compile has
  already started and goes on once the dialog closes.
- An older Unity package answers the report with an error; the CLI then stops reporting for that
  request, and the request keeps the 30-second answer.
- On Linux before Unity 6000.7 the CLI cannot look at Unity's windows, so it keeps the 30-second
  answer.
- When the main thread stops for 1 second or more right after a dialog closes, for some other work,
  a request that was waiting may fail once; `SafeToRetry` says it can be run again.
- `report-blocking-dialog` is a new internal bridge command, so the protocol version does not change.

## Revisit When

- A report fails a request that a dialog did not hold: raise the 1-second floor
  (`MainThreadEntryWatchdog.DialogReportedStallSeconds`) or the 2-second delay before the first look
  (`earlyDialogLookDelay`).
