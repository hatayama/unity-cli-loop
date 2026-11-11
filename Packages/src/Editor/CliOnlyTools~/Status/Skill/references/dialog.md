# uloop get-dialogs / uloop press-dialog-button

`uloop get-dialogs` lists the modal dialogs that block the Unity Editor for this project. `uloop press-dialog-button` presses a button in the frontmost one. Both talk to the operating system, not to Unity's uloop server, so they work while a dialog holds Unity's main thread.

## When to Use

- `uloop status` reports `BlockedByDialog` and its `NextActions` point at `uloop press-dialog-button`.
- A command you sent opened a dialog (for example a save prompt) and you know which button the user's request calls for.

## Usage

```bash
uloop get-dialogs
uloop press-dialog-button --label "<button>"
uloop get-dialogs --project-path <path>
```

`get-dialogs` takes no options. `press-dialog-button` needs `--label` exactly once, with the button label exactly as listed in `Buttons`: same case, same spaces. It presses that button in the frontmost modal dialog.

## Output

`get-dialogs` exits 0 and returns JSON with:

- `Success`: `true`
- `Dialogs`: each open modal dialog's `Title`, `Message`, and `Buttons`, frontmost first; an empty array when none is open

The order of `Buttons` can differ from the one `uloop status` reports for the same dialog (seen on Windows with Unity 6000.7 and three buttons). The labels are the same; choose a button by its label, never by its position.

`press-dialog-button` presses the button and then looks again, up to 5 times, until the pressed dialog is gone:

- The dialog is gone: exit 0, with `NextDialogs` when another dialog is open.
- A look failed or was interrupted: exit 0 without `NextDialogs`, because the press already happened. Run `uloop get-dialogs` when you need to know what is open now.
- The dialog is still open after every look: exit 1 with `DIALOG_ANSWER_FAILED`, even though the button was pressed.

uloop treats the dialog as still open while the frontmost dialog has the same `Title`, `Message`, and `Buttons` it read before the press. So a new dialog that looks exactly the same as the pressed one ends in `DIALOG_ANSWER_FAILED`, and the same dialog that stays open with different content counts as closed and exits 0. In either case, run `uloop get-dialogs` to see what is open now.

On exit 0 it returns JSON with:

- `Success`: `true`
- `Pressed`: the label that was pressed
- `Title`, `Message`, `Buttons`: the dialog that was pressed, as read just before the press
- `NextDialogs`: every modal dialog open after the press, frontmost first, if any (the first one is what `press-dialog-button` presses next)

## Errors

Errors of `get-dialogs` and `press-dialog-button` go to stderr, nothing goes to stdout, and they exit 1. `BLOCKED_BY_DIALOG` comes from the command that a dialog blocked instead: it goes to stderr and exits 1, and when a first `uloop hot-reload` apply had already answered, that answer is on stdout.

| ErrorCode | Meaning | What to do |
|-----------|---------|------------|
| `DIALOG_NOT_OPEN` | `press-dialog-button` found no modal dialog | Run `uloop status` to see what Unity is doing |
| `DIALOG_BUTTON_NOT_FOUND` | The frontmost dialog has no button with that label; `Details.Buttons` lists the labels it has | Run `uloop press-dialog-button --label` again with one of those labels, spelled exactly |
| `DIALOG_NOT_ANSWERABLE` | uloop cannot look or press on this system, or the dialog has no button labels uloop can read | Ask the user to answer the dialog in Unity |
| `DIALOG_CHECK_FAILED` | Listing failed, for example because Unity's process was not found or the operating system did not let uloop read its windows; `Message` says why | Follow `NextActions` |
| `DIALOG_ANSWER_FAILED` | The press failed or timed out, or the dialog was still open after the press | Run `uloop get-dialogs` to see whether the dialog is still open before pressing again; if it stays open, ask the user |
| `BLOCKED_BY_DIALOG` | A modal dialog holds Unity's main thread, and uloop stopped waiting. The Message tells which case: `was not run` (nothing ran), `had already started` (the command goes on in Unity once the dialog is closed, or, while uloop waited for its response, may still finish there), or neither when uloop cannot tell. `uloop hot-reload` stops with a Message that says `nothing was applied` instead, and nothing ran | Read `Details.Dialogs` and follow `NextActions`: answer the dialog, then take the last step. Run a command that had already started again only where that step says so, and check `SafeToRetry` first |

`DIALOG_ANSWER_FAILED` has `SafeToRetry: false` once the press was attempted: it may have landed, and pressing the same label again could press a button of the dialog that opened next. It has `SafeToRetry: true` only when the press failed before anything was clicked (for example, Unity's process was not found).

## What It Can Press

- Message boxes whose button labels uloop can read, such as `EditorUtility.DisplayDialog` prompts and the prompts Unity shows while it starts.
- Not modal Editor windows, file pickers, or (Windows) dialogs with an editable text box, shown or hidden: uloop lists no button for them, so `Buttons` is empty and `press-dialog-button` returns `DIALOG_NOT_ANSWERABLE`. Ask the user to answer those.
- Not a button the dialog has disabled (Windows): it is listed in `Buttons`, but `press-dialog-button` presses nothing and returns `DIALOG_ANSWER_FAILED` with `SafeToRetry: true`.

## Before Pressing

Press a button only when it clearly fits what the user asked for. Ask the user first when no button clearly fits, or when the choice would discard work (such as `Don't Save` or `Discard`).

## Supported Systems

- Listing and pressing: macOS and Windows.
- On macOS, both need Accessibility access for the app that runs uloop (System Settings > Privacy & Security > Accessibility). On Windows, neither needs a permission.
- Not a Unity Editor started as administrator on Windows while uloop runs as a standard user: uloop cannot tell which project that Editor has open, so `get-dialogs` returns `DIALOG_CHECK_FAILED` and `press-dialog-button` returns `DIALOG_ANSWER_FAILED` with `SafeToRetry: true`. Nothing is pressed; ask the user to answer the dialog.
