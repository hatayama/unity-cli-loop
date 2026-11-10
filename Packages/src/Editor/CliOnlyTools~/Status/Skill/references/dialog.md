# uloop dialog

List the modal dialogs that block the Unity Editor for this project, or press a button in the frontmost one. It talks to the operating system, not to Unity's uloop server, so it works while a dialog holds Unity's main thread.

## When to Use

- `uloop status` reports `BlockedByDialog` and its `NextActions` point at `uloop dialog --answer`.
- A command you sent opened a dialog (for example a save prompt) and you know which button the user's request calls for.

## Usage

```bash
uloop dialog
uloop dialog --answer "<button>"
uloop dialog --project-path <path>
```

`--answer` takes the button label exactly as listed in `Buttons`: same case, same spaces. It presses that button in the frontmost modal dialog. `--answer` may be given once.

## Output

Without `--answer`, it exits 0 and returns JSON with:

- `Success`: `true`
- `Dialogs`: each open modal dialog's `Title`, `Message`, and `Buttons`, frontmost first; an empty array when none is open

With `--answer`, it exits 0 once the button was pressed. uloop then looks again until the pressed dialog is gone; if that look fails or is interrupted, it still exits 0 (the press already happened) but leaves out `NextDialogs`, so run `uloop dialog` when you need to know what is open now. It returns JSON with:

- `Success`: `true`
- `Pressed`: the label that was pressed
- `Title`, `Message`, `Buttons`: the dialog that was pressed, as read just before the press
- `NextDialogs`: every modal dialog open after the press, frontmost first, if any (the first one is what `--answer` presses next)

## Errors

Errors go to stderr, nothing goes to stdout, and it exits 1.

| ErrorCode | Meaning | What to do |
|-----------|---------|------------|
| `DIALOG_NOT_OPEN` | `--answer` found no modal dialog | Run `uloop status` to see what Unity is doing |
| `DIALOG_BUTTON_NOT_FOUND` | The frontmost dialog has no button with that label; `Details.Buttons` lists the labels it has | Run `uloop dialog --answer` again with one of those labels, spelled exactly |
| `DIALOG_NOT_ANSWERABLE` | uloop cannot look or press on this system, or the dialog has no button labels uloop can read | Ask the user to answer the dialog in Unity |
| `DIALOG_CHECK_FAILED` | Listing failed, for example because Unity's process was not found or the operating system did not let uloop read its windows; `Message` says why | Follow `NextActions` |
| `DIALOG_ANSWER_FAILED` | The press failed or timed out, or the dialog was still open after the press | Run `uloop dialog` to see whether the dialog is still open before pressing again; if it stays open, ask the user |

`DIALOG_ANSWER_FAILED` has `SafeToRetry: false` once the press was attempted: it may have landed, and pressing the same label again could press a button of the dialog that opened next. It has `SafeToRetry: true` only when the press failed before anything was clicked (for example, Unity's process was not found).

## What It Can Press

- Message boxes whose button labels uloop can read, such as `EditorUtility.DisplayDialog` prompts and the prompts Unity shows while it starts.
- Not modal Editor windows or file pickers: their buttons have no readable labels, so `Buttons` is empty and `--answer` returns `DIALOG_NOT_ANSWERABLE`. Ask the user to answer those.

## Before Pressing

Press a button only when it clearly fits what the user asked for. Ask the user first when no button clearly fits, or when the choice would discard work (such as `Don't Save` or `Discard`).

## Supported Systems

- Listing: macOS and Windows.
- Pressing: macOS only for now.
- On macOS, both need Accessibility access for the app that runs uloop (System Settings > Privacy & Security > Accessibility).
