---
name: uloop-record-video
toolName: record-video
description: "Record the Unity Game View to an MP4 (WebM on Linux) while Play Mode runs. Use to capture gameplay for later review; other uloop commands can run while recording."
---

# uloop record-video

Record the Unity Game View to a video file while Play Mode is running. `Start` returns immediately; encoding continues in the Editor so other uloop commands can run during the capture.

## Usage

```bash
uloop record-video --action Start
uloop simulate-keyboard --key W --duration 2
uloop record-video --action Status
uloop record-video --action Stop
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--action` | enum | `Start` | `Start` - begin recording, `Stop` - finalize the file, `Status` - report progress |
| `--frame-rate` | integer | `30` | Output video fps. Valid range 1–60. Used by `Start` only. |
| `--max-duration-seconds` | integer | `60` | Auto-stop safety limit in seconds. Valid range 1–600. Used by `Start` only. |
| `--output-path` | string | empty | Output file path. Empty uses `.uloop/outputs/Videos/gameview_<yyyyMMdd_HHmmss>.mp4` (`.webm` on Linux). Extension must be `.mp4` or `.webm`. Linux rejects `.mp4`. Used by `Start` only. |

## Output

Returns JSON containing:

- `Success`: Whether the request completed.
- `Message`: Human-readable status.
- `Action`: Echoes the executed action (`Start`, `Stop`, or `Status`).
- `IsRecording`: Whether a recording is active after this call.
- `OutputPath`: Absolute path of the video file when known.
- `Width` / `Height`: Recording resolution (0 when none).
- `FrameRate`: Output fps (0 when none).
- `EncodedFrameCount`: Frames written to the encoder.
- `SkippedFrameCount`: Frames skipped for size mismatch, missing RT, or `AddFrame` failure.
- `ElapsedSeconds`: Seconds since start (frozen after stop).
- `StoppedBy`: `"cli"`, `"max-duration"`, `"play-mode-exit"`, `"assembly-reload"`, or `"editor-quit"` after stop; omitted while recording.

## Notes

- `Start` is allowed only while Play Mode is active. Edit Mode returns `Success: false`.
- Recording continues in the Editor after `Start` returns, so other uloop commands can run during capture.
- Recording stops automatically when `MaxDurationSeconds` is reached.
- Play Mode exit, assembly reload (`uloop compile` and similar), and Editor quit also auto-stop and finalize the file.
- Default output is H.264 `.mp4` on macOS/Windows and VP8 `.webm` on Linux. An explicit `.webm` path uses VP8 on every host.
- Changing Game View size during recording skips those frames instead of failing.
- An unfocused Editor can skip or thin Game View draws. Use `uloop focus-window` when frame counts look low.
