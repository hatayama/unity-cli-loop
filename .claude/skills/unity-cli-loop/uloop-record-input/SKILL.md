---
name: uloop-record-input
description: "Record keyboard and mouse input during PlayMode into a JSON file. Use when you need to: (1) Capture human gameplay input for later replay, (2) Record input sequences for E2E testing, (3) Save input for bug reproduction."
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

## Design Guidelines for Deterministic Replay

Replay injects input frame-by-frame, so the game must produce identical results given identical input on each run. The following patterns break determinism and must be avoided in replay-targeted code:

| Avoid | Use Instead | Why |
|-------|-------------|-----|
| `Time.deltaTime` for movement | Fixed per-frame constant (e.g. `MOVE_SPEED = 0.1f`) | deltaTime varies between runs even at the same target frame rate |
| `Random.Range()` / `UnityEngine.Random` | Seeded random (`new System.Random(fixedSeed)`) or remove randomness | Different random sequence each run |
| `Rigidbody` / Physics simulation | Kinematic movement via `Transform.Translate` | Physics is non-deterministic across runs |
| `WaitForSeconds(n)` in coroutines | `WaitForEndOfFrame` or frame counting | Real-time waits depend on frame timing |
| `Time.time` / `Time.realtimeSinceStartup` | Frame counter (`Time.frameCount - startFrame`) | Time values drift between runs |
| `FindObjectsOfType` without sort | `FindObjectsByType(FindObjectsSortMode.InstanceID)` | Iteration order is non-deterministic |
| `async/await` with `Task.Delay` | Frame-based waiting | Real-time delays are non-deterministic |

Set `Application.targetFrameRate = 60` (or your target) to reduce frame timing variance. See `InputReplayVerificationController` for a complete example of deterministic game logic.

## Prerequisites

- Unity must be in **PlayMode**
- **Input System package** must be installed (`com.unity.inputsystem`)
- Active Input Handling must be set to `Input System Package (New)` or `Both` in Player Settings

## Output

Returns JSON with:
- `Success`: Whether the operation succeeded
- `Message`: Status message
- `OutputPath`: Path to saved recording (Stop only)
- `TotalFrames`: Number of frames recorded (Stop only)
- `DurationSeconds`: Recording duration in seconds (Stop only)
