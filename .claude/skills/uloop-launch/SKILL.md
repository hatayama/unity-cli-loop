---
name: uloop-launch
description: "Launch Unity project with matching Editor version via uloop CLI. Use when you need to: (1) Open a Unity project with the correct Editor version, (2) Restart Unity to apply changes, (3) Switch build target when launching."
---

# uloop launch

Launch Unity Editor with the correct version for a project.

## Usage

```bash
uloop launch [project-path] [options]
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `project-path` | string | Optional. Use only when the target Unity project is not in the current directory. |
| `-r, --restart` | boolean | Kill running Unity and restart |
| `-p, --platform <P>` | string | Build target (e.g., StandaloneOSX, Android, iOS) |
| `--max-depth <N>` | number | Search depth when project-path is omitted (default: 3, -1 for unlimited) |
| `-a, --add-unity-hub` | boolean | Add to Unity Hub only (does not launch) |
| `-f, --favorite` | boolean | Add to Unity Hub as favorite (does not launch) |

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

# Add project to Unity Hub without launching
uloop launch -a
```

## Output

- Prints detected Unity version
- Prints project path
- If Unity is already running, focuses the existing window
- If launching, opens Unity in background
