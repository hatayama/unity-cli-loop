---
name: uloop-get-hierarchy
description: "Get Unity scene hierarchy as a structured tree. Use when you need to: (1) Inspect scene structure and parent-child relationships, (2) Explore GameObjects and their components, (3) Get hierarchy from a specific root path or selected objects. Returns the scene's GameObject tree."
---

# uloop get-hierarchy

Get Unity Hierarchy structure.

## Usage

```bash
uloop get-hierarchy [options]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--root-path` | string | - | Root GameObject path to start from |
| `--max-depth` | integer | `-1` | Maximum depth (-1 for unlimited) |
| `--include-components` | boolean | `true` | Include component information |
| `--include-inactive` | boolean | `true` | Include inactive GameObjects |
| `--include-paths` | boolean | `false` | Include full path information |
| `--use-components-lut` | string | `auto` | Use LUT for components (`auto`, `true`, `false`) |
| `--use-selection` | boolean | `false` | Use selected GameObject(s) as root(s). When true, `--root-path` is ignored. |

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# Get entire hierarchy
uloop get-hierarchy

# Get hierarchy from specific root
uloop get-hierarchy --root-path "Canvas/UI"

# Limit depth
uloop get-hierarchy --max-depth 2

# Without components
uloop get-hierarchy --include-components false

# Get hierarchy from currently selected GameObjects
uloop get-hierarchy --use-selection
```

## Output

Returns JSON with hierarchical structure of GameObjects and their components.
