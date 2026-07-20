---
name: uloop-control-play-mode
toolName: control-play-mode
description: "Control Unity Editor Play Mode. Use to start, stop, or pause Play Mode for runtime behavior checks and frame inspection."
---

# uloop control-play-mode

Control Unity Editor play mode (play/stop/pause).

## Usage

```bash
uloop control-play-mode [options]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--action` | string | `Play` | Action to perform: `Play`, `Stop`, `Pause`, `Step` |
| `--timeout-seconds` | integer | `180` | Maximum seconds to wait for the requested play mode state |

## Output

Returns JSON with the current play mode state:

- `IsPlaying`: Whether Unity is currently in play mode
- `IsPaused`: Whether play mode is paused
- `Changed`: Whether the requested action changed the current play mode state
- `WasAlreadyStopped`: Whether `Stop` was requested while Play Mode was already stopped
- `Message`: Description of the action performed

## Notes

- Stop on an already-stopped Editor sets `Changed: false`, `WasAlreadyStopped: true`
- `Play` on an Editor that is already playing is a no-op: it sets `Changed: false` and leaves the current session (its accumulated state, spawned objects, progress) untouched instead of restarting it. If you need a clean state for verification, explicitly `Stop` then `Play` rather than relying on `Play` alone to reset anything.
- `Step` advances exactly one frame and leaves PlayMode paused (the Editor's Next Frame button); it is independent of `Time.timeScale` and requires PlayMode to be running
- The command waits for the requested state before returning. Increase `--timeout-seconds` for projects with slow PlayMode entry.
- Before relying on PlayMode behavior as verification evidence, check `uloop get-logs --log-type Error` for pre-existing errors. An error already present when PlayMode starts can otherwise be mistaken for one caused by the action under test.
