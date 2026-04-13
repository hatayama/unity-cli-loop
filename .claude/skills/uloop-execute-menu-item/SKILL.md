---
name: uloop-execute-menu-item
description: "Execute Unity Editor menu commands programmatically. Use when you need to: (1) Trigger menu commands like save, build, or refresh, (2) Automate editor actions via menu paths, (3) Run custom menu items defined in project scripts."
---

# uloop execute-menu-item

Execute Unity MenuItem.

## Usage

```bash
uloop execute-menu-item --menu-item-path "<path>"
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--menu-item-path` | string | - | Menu item path (e.g., "GameObject/Create Empty") |
| `--use-reflection-fallback` | boolean | `true` | Use reflection fallback |

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# Create empty GameObject
uloop execute-menu-item --menu-item-path "GameObject/Create Empty"

# Save scene
uloop execute-menu-item --menu-item-path "File/Save"

# Open project settings
uloop execute-menu-item --menu-item-path "Edit/Project Settings..."
```

## Output

Returns JSON with execution result.

## Notes

- Use `uloop execute-dynamic-code` to discover available menu paths if needed
- Some menu items may require specific context or selection
