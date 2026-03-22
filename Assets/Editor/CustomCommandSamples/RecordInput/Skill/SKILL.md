---
name: uloop-record-input
description: Record keyboard and mouse input during PlayMode into a JSON file. Use when you need to: (1) Capture human gameplay input for later replay, (2) Record input sequences for E2E testing, (3) Save input for bug reproduction.
---

# uloop record-input

Record keyboard and mouse input during PlayMode frame-by-frame into a JSON file. Captures key presses, mouse movement, clicks, and scroll events via Input System device state diffing.

## Usage

```bash
# Start recording
uloop record-input --action Start

# Start recording with key filter
uloop record-input --action Start --keys "W,A,S,D,Space"

# Stop recording and save
uloop record-input --action Stop

# Stop and save to specific path
uloop record-input --action Stop --output-path scripts/my-play.json
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--action` | enum | `Start` | `Start` - begin recording, `Stop` - stop and save |
| `--output-path` | string | auto | Save path. Auto-generates under `.uloop/outputs/InputRecordings/` |
| `--keys` | string | `""` | Comma-separated key filter. Empty = all common game keys |

## Output

Returns JSON with:
- `Success`: Whether the operation succeeded
- `Message`: Status message
- `OutputPath`: Path to saved recording (Stop only)
- `TotalFrames`: Number of frames recorded (Stop only)
- `DurationSeconds`: Recording duration in seconds (Stop only)
