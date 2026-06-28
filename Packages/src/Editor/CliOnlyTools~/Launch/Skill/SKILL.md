---
name: uloop-launch
description: "Launch or restart Unity Editor. Use when Unity needs to be opened or restarted."
---

# uloop launch

Launch Unity Editor with the correct version for a project.

`uloop launch` is not fire-and-forget. When Unity needs to start or restart, the command waits
until Unity is actually ready for CLI operations before it exits.

## Usage

```bash
uloop launch [project-path] [options]
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `project-path` | string | Optional. Use only when the target Unity project is not in the current directory. |
| `-r, --restart` | flag | Kill running Unity and restart |
| `-q, --quit` | flag | Kill an existing Unity process for the project without launching |
| `-i, --ignore-compiler-errors` | flag | Continue opening Unity even when the project has compiler errors |
| `-p, --platform <P>` | string | Build target (e.g., StandaloneOSX, Android, iOS) |
| `--max-depth <N>` | number | Search depth when project-path is omitted (default: 3, -1 for unlimited) |

## Examples

```bash
# Search for Unity project in current directory and launch
uloop launch

# Launch specific project
uloop launch /path/to/project

# Restart Unity (kill existing and relaunch)
uloop launch -r

# Launch with build target
uloop launch -p Android

# Launch even when the project has compiler errors
uloop launch -i

# Quit running Unity without launching
uloop launch --quit
```

## Output

May print status/progress lines before the final JSON payload, such as project path, detected Unity version, or readiness wait messages.

The final JSON payload includes:

- `Success`: whether the command completed
- `Ready`: whether Unity CLI Loop is ready for commands
- `ServerReady`: whether the Unity CLI Loop server accepted requests
- `ProjectIpcReady`: whether the project IPC path accepted tool requests
- `AlreadyRunning`: whether an existing Unity process was reused
- `Launched`: whether this command launched a Unity process
- `Restarted`: whether this command stopped an existing process and launched a new one
- `Quit`: whether this command stopped Unity without launching a new process
- `PreviousProcessId`: process ID stopped by restart or quit, when available
- `CurrentProcessId`: current Unity process ID, when available
- `ProjectRoot`: resolved project root
- `Message`: readiness summary

## Notes

- If Unity is already running, focuses the existing window and verifies tool readiness
- If process scan is blocked (e.g. sandboxed `ps`), plain launch falls back to IPC probing; `--restart` and `--quit` still fail because they need the process id
- `-i, --ignore-compiler-errors` only affects new Unity processes; it has no effect when reusing an already-running Editor
- The command waits until Unity finishes startup and the CLI can connect before returning
