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

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# Start play mode
uloop control-play-mode --action Play

# Start play mode with a longer wait budget
uloop control-play-mode --action Play --timeout-seconds 600

# Stop play mode
uloop control-play-mode --action Stop

# Pause play mode
uloop control-play-mode --action Pause

# Advance exactly one frame while paused (Next Frame button)
uloop control-play-mode --action Step
```

## Output

Returns JSON with the current play mode state:

- `IsPlaying`: Whether Unity is currently in play mode
- `IsPaused`: Whether play mode is paused
- `Changed`: Whether the requested action changed the current play mode state
- `WasAlreadyStopped`: Whether `Stop` was requested while Play Mode was already stopped
- `Message`: Description of the action performed

## Notes

- Stop on an already-stopped Editor sets `Changed: false`, `WasAlreadyStopped: true`
- Step is independent of `Time.timeScale`; requires PlayMode to be running
- The command waits for the requested state before returning. Increase `--timeout-seconds` for projects with slow PlayMode entry.
