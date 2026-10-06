# Hold a macOS Activity Only While a Command Runs

Date: 2026-10-07

## Decision

While the Unity Editor processes a command — a tool or an internal bridge command — it holds a
macOS process activity started with `NSActivityUserInitiatedAllowingIdleSystemSleep`. The
activity begins when the execution router starts the command and ends when the router returns,
whether the command succeeded, failed, or was canceled. `get-editor-status` holds nothing: it
answers before the router starts any command, and it must keep answering while the main thread
is stuck. Every activity still held is ended before a domain reload. Other platforms hold
nothing.

## Context

macOS throttles an application that is not frontmost (App Nap). An Editor left in the
background enters this suppression within about a minute, and from then on everything it does
runs several times slower. An investigation on Unity 2022.3 measured this with a probe that read
the process's suppression state:

- A CPU-only loop with no allocation took 48–62 ms unsuppressed and 59–660 ms suppressed, while
  its thread CPU time stayed at 53–77 ms. No unsuppressed sample was slow.
- Hot reload of 200 methods took about 2.0–2.3 s unsuppressed and 16.7–20.2 s suppressed.
- Accepting a command — accept, router, start on the main thread — was barely delayed while
  suppressed (a few ms to 43 ms on the Editor side). What slowed down was the processing after
  it, so holding an activity only while a command is processed is enough.
- Beginning an activity with `NSActivityUserInitiatedAllowingIdleSystemSleep` (`0x00EFFFFF`)
  from inside the Editor lifted the suppression within 2 ms even when it was already in place
  (3 of 3). The suppression never came back while the activity was held (30 s three times,
  77 s once), and it came back 62–556 ms after `endActivity:`.
- Each compile left the Editor unsuppressed for about 34–55 s; what lifts it is unconfirmed.

The token that `beginActivityWithOptions:reason:` returns is autoreleased, so the adapter
retains it before its own autorelease pool drains and releases it after `endActivity:`.

## Rejected Alternatives

- **`NSActivityBackground` or `NSActivityLatencyCritical`.** Measured: the suppression came back
  while either was held.
- **Keep holding for a while after the last command.** It saves only the tens of milliseconds of
  accepting the next command, and the hold would have to outlive domain reloads, which discard
  every managed object.
- **Hold for as long as the server runs.** An Editor nobody is using would keep drawing power as
  if it were in front.
- **Ask users to set `NSAppSleepDisabled` with `defaults`.** It changes the user's settings and
  turns App Nap off for the whole Editor, not only while a command runs.
- **Bring the Editor to the front.** It takes focus away from whatever the user is doing.

## Consequences

- A long command, such as `run-tests`, holds the activity for its whole length.
- A command that never returns — a request waiting on a main thread that stays blocked, a tool
  that never finishes — keeps its activity until the next domain reload or until the Editor
  exits. This is intended: the hold ends with the command.
- Each activity token is ended exactly once. A second end does not throw: `endActivity:` on an
  ended token crashes the Editor with SIGTRAP, as a mutation test of the registry showed.
- On a macOS whose native entry points are missing, the Editor logs one warning and runs
  commands without the activity until the next domain reload.
- Work that continues after a command has responded is not covered. The commands known to leave
  such work are `compile` (it responds when the compile finishes; the domain reload follows),
  `control-play-mode` (entering or leaving Play Mode and the reload that comes with it),
  `execute-dynamic-code` when it waits for a domain reload (the compile and the reload),
  `run-tests` in Play Mode (the run continues in the new domain after the reload cuts off the
  request), `record-video`, `replay-input`, and `enable-watch` (they register per-frame work and
  return), and the internal bridge command `set-code-optimization-debug` (the recompile and
  reload after the switch).
- Windows and Linux have not been investigated; they hold nothing.
- A Begin and End pair costs about 4 µs inside the Editor (median of 100 pairs; the slowest took
  0.11 ms).

## Reversal condition

Reopen this when Unity or macOS stops suppressing a background Editor that receives work over
IPC, or when delays in work that continues after a command has responded (such as Play Mode
tests) are reported as real harm and the hold has to cover more than the command itself.
