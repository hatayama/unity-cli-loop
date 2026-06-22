---
name: uloop-record-input
toolName: record-input
description: "Record PlayMode keyboard and mouse input to JSON. Use to capture gameplay, bug repro, or E2E input sequences for replay."
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

## Deterministic Replay

Replay injects input frame-by-frame, so the game must also be deterministic to produce identical results. If the recording will be used for E2E tests, bug reproduction, or replay verification, read [references/deterministic-replay.md](references/deterministic-replay.md) before designing assertions or game logic.

## Prerequisites

- Unity must be in **PlayMode**
- **Input System package** (`com.unity.inputsystem`) must be installed; this tool only works with the New Input System.

## Output

The CLI prints JSON with:

- `Success`: Whether the operation succeeded
- `Message`: Status message
- `Action`: Echoes which action was executed (`Start` or `Stop`)
- `OutputPath`: Path to saved recording (nullable; populated on `Stop` only)
- `TotalFrames`: Number of frames recorded (nullable int; populated on `Stop` only)
- `DurationSeconds`: Recording duration in seconds (nullable float; populated on `Stop` only)

Frame data lives only in the JSON file at `OutputPath`; internal metadata fields are not included in the response.
