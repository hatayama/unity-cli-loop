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
- `isPlaying`: Whether Unity is currently in play mode
- `isPaused`: Whether play mode is paused
- `changed`: Whether the requested action changed the current play mode state
- `wasAlreadyStopped`: Whether `Stop` was requested while Play Mode was already stopped
- `message`: Description of the action performed

## Notes

- Play action starts the game in the Unity Editor (also resumes from pause)
- Stop action exits play mode and returns to edit mode. If Play Mode was already stopped, `changed` is `false`, `wasAlreadyStopped` is `true`, and `message` is `Play mode was already stopped`.
- Pause action pauses the game while remaining in play mode
- Step action advances exactly one frame and leaves play mode paused (the Editor's Next Frame button; independent of Time.timeScale). Requires PlayMode to be running; repeat to walk transitions frame by frame
- Useful for automated testing workflows
- The command waits for the requested state before returning. Increase `--timeout-seconds` for projects with slow PlayMode entry.
