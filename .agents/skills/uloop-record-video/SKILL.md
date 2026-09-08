---
name: uloop-record-video
toolName: record-video
description: "Record the Unity Game View or any Editor window (Scene, Inspector, Console, ...) to a video file (H.264 .mp4, or VP8 .webm). Use when a still screenshot is not enough: motion, animation, transitions, physics, a gameplay sequence, or an Editor window changing over time. `start` returns at once and other uloop commands keep working while it records."
---

# Task

Record the Unity Game View while Play Mode is running, or any Editor window with `--window-name`, then hand the file path to the user or inspect frames from it.

## Workflow

1. For a Game View recording, ensure Play Mode is running (`uloop control-play-mode --action Play`) and the Game View is open; `start` fails in Edit Mode. With `--window-name` the recording targets that Editor window instead and needs no Play Mode.
2. `uloop record-video --action start [options]`. It returns immediately; encoding continues inside the Editor.
3. Drive the scene with other uloop commands (`simulate-keyboard`, `simulate-mouse-input`, `replay-input`, ...). Do **not** run `uloop compile` or exit Play Mode mid-recording: both auto-stop and finalize the file.
4. `uloop record-video --action stop`. The file is playable only after this call (or after an auto-stop).
5. Read `OutputPath` from the JSON and use exactly that path. The output directory holds earlier recordings too (the newest 20 per extension are kept), so `ls -t` can pick a stale file.
6. To inspect the content yourself, extract stills with ffmpeg (e.g. `ffmpeg -i "<OutputPath>" -vf fps=1 frames_%03d.png`) and view the PNGs. Otherwise report the path and duration to the user.

## Tool Reference

```bash
uloop record-video --action start [--frame-rate <fps>] [--max-duration-seconds <sec>] [--resolution-scale <0.1-1.0>] [--quality <low|medium|high>] [--window-name <name>] [--match-mode <exact|prefix|contains>] [--output-path <file>]
uloop record-video --action status
uloop record-video --action stop
```

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--action` | enum | `start` | `start` - begin recording, `stop` - finalize the file, `status` - report progress |
| `--frame-rate` | integer | `30` | Output video fps. Valid range 1–60. Frames are paced by wall-clock time; when the Editor renders slower than this, the previous frame is repeated. Used by `start` only. |
| `--max-duration-seconds` | integer | `60` | Auto-stop safety limit in seconds. Valid range 1–600. Used by `start` only. |
| `--resolution-scale` | number | `1.0` | Resolution scale (0.1 to 1.0) applied to the Game View size before encoding. `0.5` cuts file size and encoding cost to about a quarter. Used by `start` only. |
| `--quality` | enum | `medium` | Encoder bitrate preset: `low`, `medium`, or `high`. Used by `start` only. |
| `--window-name` | string | empty | Editor window title to record instead of the Game View (for example Scene, Inspector, Console). Empty records the Game View and requires Play Mode. A window recording does not require Play Mode, brings the tab to the front, repaints it every frame, and keeps running when Play Mode stops. Used by `start` only. |
| `--match-mode` | enum | `exact` | Window title matching for `--window-name`: `exact`, `prefix`, or `contains` (case-insensitive). Used by `start` only. |
| `--output-path` | string | empty | Output file path. Empty uses `.uloop/outputs/Videos/gameview_<yyyyMMdd_HHmmss_fff>.mp4` (`.webm` on Linux). Extension must be `.mp4` (H.264) or `.webm` (VP8). Linux rejects `.mp4`. Used by `start` only. |

### Actions

| Action | Behavior | Typical use |
|--------|----------|-------------|
| `start` | Validates, opens the encoder, returns. One recording at a time; a second `start` fails until `stop`. | Begin capture before driving input |
| `status` | Reports live counters. When idle, reports the most recent auto-stopped recording. | Check progress or find out why a recording ended |
| `stop` | Finalizes the file and returns the final counters. When nothing is recording, returns the last auto-stopped recording once, then "No recording is in progress." | End capture |

## Output

Returns JSON containing:

- `Success`: Whether the request completed.
- `Message`: Human-readable status.
- `Action`: Echoes the executed action.
- `IsRecording`: Whether a recording is active after this call.
- `OutputPath`: Absolute path of the video file. Open this path; do not search the directory. A window recording uses the `window_` name prefix instead of `gameview_`.
- `Width` / `Height`: Encoded resolution after `--resolution-scale` and even rounding (0 when none).
- `FrameRate`: Output fps (0 when none).
- `Quality`: Bitrate preset in use.
- `EncodedFrameCount`: Frames written to the encoder.
- `SkippedFrameCount`: Frame slots that could not be captured (Game View closed or resized, or encoder refused a frame). The video keeps its timeline; skipped slots are simply missing.
- `ElapsedSeconds`: Seconds since start (frozen after stop).
- `StoppedBy`: Why the recording ended — `"cli"`, `"max-duration"`, `"play-mode-exit"`, `"window-closed"`, `"assembly-reload"`, or `"editor-quit"`. Omitted while recording.

## Interpreting results

- `Success: false` with `Message` "A recording is already in progress" → run `stop` first, then `start` again.
- `Success: false` with "Play Mode view RenderTexture is not available" → open the Game View tab (`uloop focus-window`) and make sure a camera renders.
- `SkippedFrameCount` growing while `EncodedFrameCount` stays flat → the Game View is closed, hidden, or resized. Restore it; recording resumes without restarting.
- `EncodedFrameCount` far below `ElapsedSeconds × FrameRate` with few skips → the Editor is unfocused and throttling draws; run `uloop focus-window` before the next recording.
- `Success: false` with "Window '...' not found" → pick the right title from the `Open windows:` list in the message, or use `--match-mode prefix`.
- `StoppedBy` is not `"cli"` → the recording ended on its own; the file is still valid up to that point.

## Notes

- Odd Game View sizes are rounded down to even for H.264 (for example 1286×723 → 1286×722; the last pixel row/column is dropped).
- The recording is wall-clock paced, so Play Mode pause records a still image, not a gap.
- A window recording survives Play Mode stopping, but entering Play Mode with domain reload enabled (Unity's default) ends it with `StoppedBy: "assembly-reload"`.
- Default output is H.264 `.mp4` on macOS/Windows and VP8 `.webm` on Linux. An explicit `.webm` path uses VP8 on every host.
- Default recordings under `.uloop/outputs/Videos/` keep only the newest 20 files per extension; a custom `--output-path` is never pruned.
