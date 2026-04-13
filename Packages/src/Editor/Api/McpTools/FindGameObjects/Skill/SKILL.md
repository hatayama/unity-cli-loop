---
name: uloop-find-game-objects
description: "Find GameObjects in the active scene by various criteria. Use when you need to: (1) Search for objects by name, regex, or path, (2) Find objects with specific components, tags, or layers, (3) Get currently selected GameObjects in Unity Editor. Returns matching GameObjects with hierarchy paths and components."
---

# uloop find-game-objects

Find GameObjects with search criteria or get currently selected objects.

## Usage

```bash
uloop find-game-objects [options]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--name-pattern` | string | - | Name pattern to search |
| `--search-mode` | string | `Exact` | Search mode: `Exact`, `Path`, `Regex`, `Contains`, `Selected` |
| `--required-components` | array | - | Required components |
| `--tag` | string | - | Tag filter |
| `--layer` | integer | - | Layer filter (layer number) |
| `--max-results` | integer | `20` | Maximum number of results |
| `--include-inactive` | boolean | `false` | Include inactive GameObjects |
| `--include-inherited-properties` | boolean | `false` | Include inherited properties in results |

## Search Modes

| Mode | Description |
|------|-------------|
| `Exact` | Exact name match (default) |
| `Path` | Hierarchy path search (e.g., `Canvas/Button`) |
| `Regex` | Regular expression pattern |
| `Contains` | Partial name match |
| `Selected` | Get currently selected GameObjects in Unity Editor |

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# Find by name
uloop find-game-objects --name-pattern "Player"

# Find with component
uloop find-game-objects --required-components Rigidbody

# Find by tag
uloop find-game-objects --tag "Enemy"

# Regex search
uloop find-game-objects --name-pattern "UI_.*" --search-mode Regex

# Get selected GameObjects
uloop find-game-objects --search-mode Selected

# Get selected including inactive
uloop find-game-objects --search-mode Selected --include-inactive
```

## Output

Returns JSON with matching GameObjects.

For `Selected` mode with multiple objects, results are exported to file:
- Single selection: JSON response directly
- Multiple selection: File at `.uloop/outputs/FindGameObjectsResults/`
- No selection: Empty results with message
