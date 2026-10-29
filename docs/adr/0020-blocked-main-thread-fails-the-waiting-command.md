# A Blocked Main Thread Fails the Waiting Command

Date: 2026-10-29

## Decision

A command that holds the command slot but has not entered its tool code yet (it is still waiting
for its first switch to the Editor main thread) is failed by Unity once the main thread has stalled
for 30 seconds. Unity cancels the wait through the same lease cancellation as `uloop cancel`,
releases the slot, and answers `main_thread_blocked` with the tool name and the stall seconds; the
CLI reports it as `MAIN_THREAD_BLOCKED`, safe to retry, because no tool code ran.

- The stall is the time since the main thread last ticked, not how long the command waited.
- The first check happens one poll (1 second) after the wait starts, then once a second. An idle
  Editor's loop sleeps and its stall counter keeps growing; the switch request wakes it, and one
  poll interval is enough for it to tick.
- A command whose tool code has started is never stopped this way. Its stall stays visible through
  the CLI's 30-second notice (on stderr every 60 seconds when no spinner is shown) and the CLI gives
  up after 5 minutes with `UNITY_EDITOR_UNRESPONSIVE`, as before.
- Requests `uloop cancel` may not stop (a compile with a domain reload, run-tests that respects the
  Enter Play Mode settings) are failed too: before the tool code runs there is nothing to break.
- 30 seconds is the same "longest ordinary busy" limit the CLI already uses for its stall notice and
  for compile's silent-wait warning.

## Context

Issue #3411: with a modal dialog holding the main thread, `uloop status` answered
`MainThreadBlocked` within seconds and a second command got `UNITY_SERVER_BUSY` saying the holder
was waiting for a main thread that had not responded for N seconds, but the command holding the slot
waited silently. In the report's 60-second block it returned success after 52 seconds with nothing
printed in between; without anyone dismissing the dialog it would have waited for the CLI's
5-minute limit. The stall was detected, just never used for the waiting command itself.

## Considered Options

- Give up on the CLI side: rejected. A request sent with `cancelOnClientDisconnect=false` keeps
  running in Unity after the CLI leaves, so the command could still run later, after the agent has
  moved on, and the slot would stay taken in the meantime.
- 5 seconds (the BUSY report threshold): rejected. Scene saves, small imports, and entering Play
  Mode routinely exceed it, so ordinary commands would fail and be sent again.
- 15 seconds: rejected for the same reason at a lower rate, and it matches no other limit the user
  already meets.
- 60 seconds: rejected. A dialog would be reported a minute late, which is longer than the wrappers
  agents put around commands (about 30 seconds in the report).

## Consequences

- A command sent during a synchronous main-thread operation longer than 30 seconds (a large import)
  fails once and has to be run again; `SafeToRetry` says so.
- When an `execute-dynamic-code` snippet blocks the main thread for 30 seconds or more, another
  `execute-dynamic-code` waiting behind it in the shared slot fails with `MAIN_THREAD_BLOCKED`.
  Before, it ran after the first snippet finished.
- The error is an additive `type`; older CLIs show it as `UNITY_RPC_ERROR`, so the protocol version
  does not change.

## Revisit When

- (a) In real use, main-thread work that is not a dialog and lasts over 30 seconds causes
  `MAIN_THREAD_BLOCKED` several times a day: raise the threshold.
- (b) The main complaint is that dialogs are detected too late: lower it.

Both are the single constant `MainThreadEntryWatchdog.BlockedStallSeconds`.
