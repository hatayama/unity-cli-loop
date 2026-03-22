---
name: uloop-replay-input
description: Replay recorded input during PlayMode with frame-precise injection. Use when you need to: (1) Reproduce recorded gameplay exactly, (2) Run E2E tests from recorded input, (3) Generate demo videos with consistent input.
---

# uloop replay-input

Replay recorded keyboard and mouse input during PlayMode. Loads a JSON recording and injects input frame-by-frame via Input System with zero CLI overhead. Supports looping and progress monitoring.

## Usage

```bash
# Start replay (auto-detect latest recording)
uloop replay-input --action Start

# Start replay with specific file
uloop replay-input --action Start --input-path scripts/my-play.json

# Start replay with looping
uloop replay-input --action Start --loop true

# Check replay progress
uloop replay-input --action Status

# Stop replay
uloop replay-input --action Stop
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--action` | enum | `Start` | `Start`, `Stop`, `Status` |
| `--input-path` | string | auto | JSON path. Auto-detects latest in `.uloop/outputs/InputRecordings/` |
| `--show-overlay` | boolean | `true` | Show replay progress overlay |
| `--loop` | boolean | `false` | Loop continuously |

## Output

Returns JSON with:
- `Success`: Whether the operation succeeded
- `Message`: Status message
- `InputPath`: Path to recording file (Start only)
- `CurrentFrame`: Current replay frame
- `TotalFrames`: Total frames in recording
- `Progress`: Replay progress (0.0 - 1.0)
- `IsReplaying`: Whether replay is active
