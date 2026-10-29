---
name: uloop-set-game-view-size
toolName: set-game-view-size
description: "Set, inspect, or reset the Unity Game View custom rendering resolution."
---

# uloop set-game-view-size

Set, inspect, or reset the Unity Game View custom rendering resolution.

## Usage

```bash
uloop set-game-view-size [--width <width> --height <height>] [--reset]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--width` | integer | unset | Target Game View rendering width in pixels. Provide with `--height` to change the resolution. |
| `--height` | integer | unset | Target Game View rendering height in pixels. Provide with `--width` to change the resolution. |
| `--reset` | flag | - | Removes the `uloop` size entries this tool added from every platform group of the Game View size list, saves the list, and restores the size that was selected before this tool selected its entry. Cannot be combined with `--width` / `--height`. |

## Output

Returns JSON containing:

- `Success`: `false` when the Device Simulator (or another non-Game View window) is the Play Mode view and a change or reset was requested, when only one of `--width` / `--height` is given, when a value is not positive, or when `--reset` is combined with a size. Nothing changes in those cases.
- `View`: Play Mode view after the request: `GameView`, `Simulator`, `None` (no Play Mode view open), or `Other` (a third-party Play Mode view).
- `PreviousWidth` / `PreviousHeight`: Rendering resolution before the request (`0` when no view was open).
- `CurrentWidth` / `CurrentHeight`: Rendering resolution after the request.
- `Changed`: Whether the rendering resolution changed.
- `SelectedSize`: Name of the entry selected in the Game View size dropdown after the request, e.g. `uloop (1280x720)` or `Free Aspect`. Present when a Game View is showing.
- `Removed`: Number of `uloop` entries removed. Present with `--reset`.
- `Message`: Human-readable status.
- `Warning`: Present after a change; says that the `uloop` entry persists and how to remove it.

## Examples

```bash
# Read the current rendering resolution and the selected size entry
uloop set-game-view-size

# Pin a Full HD rendering resolution
uloop set-game-view-size --width 1920 --height 1080

# Remove the uloop entry and restore the previous selection
uloop set-game-view-size --reset
```

## Notes

- Changing the size adds an entry named `uloop (<width>x<height>)` to the Game View size dropdown and selects it; later calls update that one entry, and entries whose name only starts with `uloop` (such as `uloop-phone`) are never touched. Unity saves the list to the user-wide `GameViewSizes.asset` in the Editor preferences folder (macOS: `~/Library/Preferences/Unity/Editor-5.x/`, Windows: `%APPDATA%\Unity\Editor-5.x\Preferences\`), so the entry outlives the Editor session and appears in every project. One entry exists per platform group (Standalone, iOS, Android, ...), so it can sit in more than one group after switching the build target.
- `--reset` removes those entries from every group and selects the size that was selected before the first change in the current Editor session. That earlier selection is remembered only within the Editor session (it survives a domain reload, not an Editor restart); when it is unknown or no longer exists, the first entry of the group (`Free Aspect`) is selected and `Message` says so. A selection you made yourself in the meantime is kept. The selection is restored only for the current platform group (the one of the active build target); in other groups the entry is removed and Unity keeps or clamps the selection on its own.
- Under the Device Simulator the Game View size does not apply: a change or `--reset` returns `Success: false` and changes nothing. Run `uloop game-view-mode --to GameView` first, or pick a device with `uloop game-view-mode --device <name>` to change the simulated screen size. Reading without arguments still works and reports the Simulator's rendering size with `View: Simulator`.
- No Play Mode view open: reading reports `View: None` with `0x0` and opens nothing; a change opens a Game View with the requested size; `--reset` removes the entries and has no selection to restore.
- Width and height must be provided together and be positive.
