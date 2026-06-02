---
name: uloop-wait-for-debug-break
description: "Wait until Unity Editor pauses after Debug.Break. Use when log output is too noisy to inspect live, or when input simulation needs a precise pause point for state inspection."
---

# uloop wait-for-debug-break

Wait until Unity Editor becomes paused after a `Debug.Break()` call.

## Usage

```bash
uloop wait-for-debug-break
```

## Parameters

None.

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Workflow

1. Add a temporary marker before the break point:

```csharp
UnityEngine.Debug.Log("[DebugBreak] reached target state");
UnityEngine.Debug.Break();
```

2. Start the wait command before triggering the behavior:

```bash
uloop wait-for-debug-break
```

3. Trigger the action, input simulation, or reproduction path.
4. When the command returns success, inspect Unity Console, Hierarchy, Inspector, or other uLoop state commands while Unity is paused.
5. Resume Unity after inspection:

```bash
uloop control-play-mode --action Play
```

6. Remove the temporary `Debug.Break()` and marker log unless the break is intentionally part of the debugging workflow.

## Output

Returns JSON with:

- `Success`: Whether the pause was observed
- `IsPlaying`: Current Unity PlayMode state
- `IsPaused`: Current Unity paused state
- `ElapsedMilliseconds`: Time spent waiting
- `Message`: Status message

## Notes

- Start this command while Unity is not paused. If Unity is already paused, the command fails to avoid confusing an old pause with the target `Debug.Break()`.
- This command polls a lightweight internal PlayMode-state bridge and can be left waiting while other uLoop commands are used.
- This command only observes Unity pause state. If it was started accidentally, stop the CLI process from the runner/session that started it.
- Resume a Debug.Break pause with `uloop control-play-mode --action Play`; existing control-play-mode behavior clears `EditorApplication.isPaused`.
- Remove the temporary `Debug.Break()` call from code to prevent future pauses.
- Use marker logs when several code paths can pause Unity, then inspect the Console after the wait completes.
