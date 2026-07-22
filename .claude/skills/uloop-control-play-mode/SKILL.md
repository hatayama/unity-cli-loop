---
name: uloop-control-play-mode
toolName: control-play-mode
description: "Control Unity Editor Play Mode. Use to start, stop, pause, or step Play Mode, or query its state without side effects, for runtime behavior checks and frame inspection."
---

# uloop control-play-mode

Control Unity Editor play mode (play/stop/pause/step) or query its state without side effects (status).

## Usage

```bash
uloop control-play-mode [options]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--action` | string | `Play` | Action to perform: `Play`, `Stop`, `Pause`, `Step`, `Status` |
| `--timeout-seconds` | integer | `180` | Maximum seconds to wait for the requested play mode state |

## Output

Returns JSON with the current play mode state:

- `IsPlaying`: Whether Unity is currently in play mode
- `IsPaused`: Whether play mode is paused
- `Changed`: Whether the requested action changed the current play mode state
- `WasAlreadyStopped`: Whether `Stop` was requested while Play Mode was already stopped
- `ResumedFromPause`: Whether `Play` resumed a paused Play Mode session instead of starting a new one
- `Message`: Description of the action performed

## Notes

- Stop on an already-stopped Editor sets `Changed: false`, `WasAlreadyStopped: true`
- `Play` on an Editor that is already playing is a no-op: it sets `Changed: false` and leaves the current session (its accumulated state, spawned objects, progress) untouched instead of restarting it. If you need a clean state for verification, explicitly `Stop` then `Play` rather than relying on `Play` alone to reset anything.
- `Play` while Play Mode is paused resumes the same session: it sets `Changed: true`, `ResumedFromPause: true`, and `Message: "Play mode resumed"` — the session is not restarted.
- `Step` advances exactly one frame and leaves PlayMode paused (the Editor's Next Frame button); it is independent of `Time.timeScale` and requires PlayMode to be running
- The command waits for the requested state before returning. Increase `--timeout-seconds` for projects with slow PlayMode entry.
- Before relying on PlayMode behavior as verification evidence, check `uloop get-logs --log-type Error` for pre-existing errors. An error already present when PlayMode starts can otherwise be mistaken for one caused by the action under test.
- `Status` reads the current state without touching anything: no state change (`Changed` is always `false`), no waiting, and none of `Play`'s side effects — it does not save dirty scenes and is never rejected by compile errors. It does report whether compile errors would currently block `Play` (`BlockedByCompileErrors` with the `CompileErrors` list), read from the last compile result without triggering a new compile. It does not predict unsaved-changes blocking: `BlockedByUnsavedChanges` describes a failed save attempt during a `Play` request, and `Status` never attempts one. Use it when you only need to know whether Play Mode is running or paused; the other actions report the same state fields, but only as part of performing their action.
- `Play` fails immediately with a `CONTROL_PLAY_MODE_UNSAVED_CHANGES` error when unsaved changes cannot be saved quietly — most commonly an Untitled scene, which has no path to save to. The error message lists exactly which scenes or prefab stages blocked it; save the Untitled scene to an explicit path (or discard the changes), then retry.
