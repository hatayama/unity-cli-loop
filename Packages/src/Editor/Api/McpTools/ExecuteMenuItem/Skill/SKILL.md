---
name: uloop-execute-menu-item
description: "Execute Unity Editor menu commands programmatically. Use when you need to: (1) Trigger built-in menu commands like save, refresh, or build, (2) Automate editor actions that already exist as menu paths, (3) Run custom MenuItem commands defined in project scripts. Executes Unity menu paths via uloop CLI."
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

Returns JSON with:
- `MenuItemPath` (string): The menu item path that was executed
- `Success` (boolean): Whether the execution succeeded
- `ExecutionMethod` (string): `"EditorApplication"` (primary path) or `"Reflection"` (fallback path)
- `MenuItemFound` (boolean): Whether the menu item exists in the system
- `ErrorMessage` (string): Error text if execution failed; empty on success
- `Details` (string): Additional information about the execution
- `WarningMessage` (string): Warning text if there are issues with the menu item (e.g., duplicate attributes); empty when none

## Notes

- Use `uloop execute-dynamic-code` to discover available menu paths if needed
- Some menu items may require specific context or selection
