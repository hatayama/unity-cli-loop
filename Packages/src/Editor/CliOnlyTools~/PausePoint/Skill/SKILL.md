---
name: uloop-wait-for-pause-point
description: "Pause Unity at a named UnityCliLoopDebug.Break marker when input or gameplay state is hard to verify."
---

# uloop wait-for-pause-point

Pause Unity when execution reaches a named marker in user code.

## When to use

- Use when logs or screenshots cannot prove that a gameplay path was reached.
- Use during development debugging, not only E2E tests.
- Use after simulated keyboard or mouse input when you need to inspect exact runtime state.

## Workflow

1. Add a marker at the state you want to inspect:

```csharp
using io.github.hatayama.UnityCliLoop.Runtime;

UnityCliLoopDebug.Break("player-jumped");
```

2. Compile the project.
3. Arm the marker before triggering the code path:

```bash
uloop arm-pause-point --id player-jumped --timeout-seconds 30
```

4. Trigger the behavior, such as `simulate-keyboard`, `simulate-mouse-input`, UI interaction, or dynamic code.
5. Wait for the marker:

```bash
uloop wait-for-pause-point --id player-jumped --timeout-seconds 30
```

6. While Unity is paused, inspect state with `uloop get-logs`, `uloop get-hierarchy`, `uloop find-game-objects`, screenshots, or `uloop execute-dynamic-code`.
7. Clear the marker if you stop waiting:

```bash
uloop clear-pause-point --id player-jumped
```

## Safety

- `UnityCliLoopDebug.Break` uses Unity's conditional-call pattern and is compiled out of non-Editor call sites.
- Do not pass side-effect expressions as the id argument. Use stable string ids.
- This does not collect logs or state snapshots. Use existing inspection commands after Unity pauses.
