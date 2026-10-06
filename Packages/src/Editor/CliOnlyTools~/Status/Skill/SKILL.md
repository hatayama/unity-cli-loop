---
name: uloop-status
description: "Check whether the Unity Editor for this project can take a uloop command now, without side effects. Use before sending commands when Unity may be starting, compiling, busy, or closed."
---

# uloop status

Report whether the Unity Editor for this project can take a uloop command now, without changing anything.

## Usage

```bash
uloop status
uloop status --project-path <path>
```

## States

The states are checked in this order, and the first one that matches is reported.

| State | Meaning | Exit code |
|-------|---------|-----------|
| `Busy` | Unity answered, and a uloop command is running and holds the command slot | 1 |
| `Starting` | Unity answered but has not recorded its play and compile state yet | 1 |
| `MainThreadBlocked` | Unity answered, but its main thread has not run for 5 seconds or more, even when asked again after being woken | 1 |
| `Compiling` | Unity answered and is compiling scripts | 1 |
| `ImportingAssets` | Unity answered and is importing assets | 1 |
| `Ready` | Unity answered and none of the above applies | 0 |
| `NotResponding` | Connected to Unity, but it did not answer within 5 seconds | 1 |
| `ServerUnavailable` | The uloop server refused or dropped the connection, and Unity is running for this project | 1 |
| `NotRunning` | The uloop server refused or dropped the connection, and no Unity Editor is running for this project | 1 |
| `Unreachable` | The uloop server refused or dropped the connection, and the Unity process list could not be read | 1 |

Some failures are errors, not states: the operating system refusing the connection (as a sandbox does), an error answered by Unity (such as a package too old to support `uloop status`), an unreadable answer, or an unknown option. These print an error to stderr, nothing to stdout, and exit 1.

## Output

Returns JSON with:

- `State`: one of the states above
- `Ready`: `true` only for `Ready`
- `Message`: what the state means for this Editor
- `NextActions`: what to do next; an empty array for `Ready`
- `RunningToolName`, `RunningToolElapsedSeconds`, `RunningToolPhase` (`Executing` or `WaitingForMainThread`): the running command; `Busy` only
- `IsPlaying`, `IsPaused`, `IsCompiling`, `IsUpdating`: Unity's play and compile state; only when Unity answered and has recorded that state
- `SecondsSinceLastMainThreadTick`: seconds since Unity's main thread last ran; only when Unity answered
- `UnityProcessRunning`: whether Unity is running for this project; `ServerUnavailable` and `NotRunning` only
- `ConnectionError`: the connection error as reported; `NotResponding`, `ServerUnavailable`, `NotRunning`, and `Unreachable` only

Play Mode does not change the state: a paused Play Mode session is `Ready`, with `IsPlaying` and `IsPaused` set.

## Notes

- Exits 0 only for Ready.
- It never focuses the window, launches Unity, or takes the command slot, so it is safe to poll.
- A sandboxed shell that cannot open the Unix socket gets an error, not NotRunning; run it outside the sandbox.
- uloop cannot detect Safe Mode directly; ServerUnavailable that does not clear is the signal.
- Before reporting MainThreadBlocked it wakes the Editor's main loop and asks again about a second later, so an Editor that is only idle in the background reads as Ready.
