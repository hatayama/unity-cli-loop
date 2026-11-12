---
name: uloop-status
description: "Check whether the Unity Editor for this project can take a uloop command now, without side effects; also covers `uloop cancel` for stopping the command it is running and `uloop get-dialogs` / `uloop press-dialog-button` for listing the dialogs that block it and pressing one of their buttons. Use before sending commands when Unity may be starting, compiling, busy, or closed, and when BUSY does not clear or a command does not finish."
---

# uloop status

Report whether the Unity Editor for this project can take a uloop command now, without changing anything.

## Usage

```bash
uloop status
uloop status --project-path <path>
```

## States

The states are checked in this order, and the first one that matches is reported. A dialog found in Unity's windows replaces `Ready`, `Compiling`, `ImportingAssets`, `Busy`, `MainThreadBlocked`, or `ServerUnavailable`.

| State | Meaning | Exit code |
|-------|---------|-----------|
| `BlockedByDialog` | A modal dialog is open and Unity's main thread waits for it; how it is found and how to answer it are in Notes | 1 |
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
- `RunningToolName`, `RunningToolElapsedSeconds`, `RunningToolPhase` (`Executing` or `WaitingForMainThread`): the running command; `Busy`, and `BlockedByDialog` while a command runs
- `IsPlaying`, `IsPaused`, `IsCompiling`, `IsUpdating`: Unity's play and compile state; only when Unity answered and has recorded that state
- `SecondsSinceLastMainThreadTick`: seconds since Unity's main thread last ran; only when Unity answered
- `UnityProcessRunning`: whether Unity is running for this project; `ServerUnavailable` and `NotRunning` only, and `BlockedByDialog` found while the server did not answer
- `ConnectionError`: the connection error as reported; `NotResponding`, `ServerUnavailable`, `NotRunning`, and `Unreachable` only, and `BlockedByDialog` found while the server did not answer
- `Dialogs`: each open dialog's `Title`, `Message`, and `Buttons` (button labels; empty when uloop cannot read them); `BlockedByDialog` only
- `DialogCheckError`: why uloop could not look in Unity's windows for a dialog; macOS and Windows only, with `Busy`, `MainThreadBlocked`, or `ServerUnavailable`

Play Mode does not change the state: a paused Play Mode session is `Ready`, with `IsPlaying` and `IsPaused` set.

## Notes

- Exits 0 only for Ready.
- When `Busy` does not clear and the running command shows no sign of finishing, stop it with `uloop cancel`; its outcomes, what it cannot cancel, and what to do after it are in [references/cancel.md](references/cancel.md).
- It never focuses the window, launches Unity, or takes the command slot, so it is safe to poll.
- A sandboxed shell that cannot open the Unix socket gets an error, not NotRunning; run it outside the sandbox.
- uloop cannot detect Safe Mode itself; ServerUnavailable that does not clear is the signal. On macOS and Windows, the prompt that offers Safe Mode shows as BlockedByDialog.
- Before reporting MainThreadBlocked it wakes the Editor's main loop and asks again about a second later, so an Editor that is only idle in the background reads as Ready.
- Before sending any command other than `status`, `get-dialogs`, `cancel`, `press-dialog-button`, and `focus-window`, uloop asks the Editor once whether a modal dialog holds it; when one does, uloop does not send it and fails at once with `BLOCKED_BY_DIALOG` (`was not run`). A dialog it does not find there is caught as follows. A command sent while the main thread is blocked waits for it. Once the stall reaches 30 seconds before any of the command's code has run, Unity stops the wait and the command fails with `MAIN_THREAD_BLOCKED` (nothing ran, so running it again after the block clears is safe). When uloop finds a dialog behind the block (the Unity 6000.7 note below says where it can look), it fails with `BLOCKED_BY_DIALOG` instead, otherwise with `MAIN_THREAD_BLOCKED`; when that Message says the command `was not run`, nothing ran either, so follow its `NextActions`. A command whose own code blocks the main thread is not stopped; the CLI prints a stall notice on stderr every 60 seconds instead and gives up after 5 minutes with `UNITY_EDITOR_UNRESPONSIVE`. When a dialog holds the main thread after the command started, uloop stops waiting with `BLOCKED_BY_DIALOG` whose Message says the command `had already started`; do not rerun it blindly; follow its `NextActions`, whose last step says whether running it again is the way on (as for `uloop compile`, which reattaches). `execute-dynamic-code` is the exception: its own snippet may block the main thread for as long as it runs.
- For `BlockedByDialog`, read `Title`, `Message`, and `Buttons`, and press the button that fits what the user asked for with `uloop press-dialog-button --label "<button>"`. Ask the user first when no button clearly fits or the choice would discard work (such as `Don't Save`). When `NextActions` says uloop cannot press the dialog, show it to the user and ask them to answer it in Unity. Pressing, its errors, and what it can press are in [references/dialog.md](references/dialog.md).
- Unity 6000.7 or later reports message boxes and modal Editor windows itself, on every OS. On macOS and Windows, uloop also looks in Unity's windows when the main thread has not run for 1 second or more and still has not when asked again (5 seconds or more while a command runs), or when Unity runs without its server, so it finds dialogs in older Editors and startup prompts. On Linux it does not look: with an Editor older than 6000.7, or before the package has loaded, such a dialog shows as `Busy`, `MainThreadBlocked`, or `ServerUnavailable`.
- On macOS that look needs Accessibility and Automation (System Events) access for the app that runs uloop, and the first one can make macOS ask whether the app may control System Events; on Windows it needs no permission. When it fails, the state is unchanged and, once the main thread has not run for 5 seconds or more, `DialogCheckError` says why; a dialog may still be open.
