---
name: uloop-get-hierarchy
description: "Get Unity scene hierarchy as a structured tree from all roots, a root path, or the current Hierarchy selection. Use this when you need the child tree, parent-child structure, or descendants under selected GameObject(s) with `--use-selection`. Use find-game-objects for selected object details and component properties. Hierarchy data is written to a JSON file on disk and the response returns the file path (not the tree inline) — open the file to read the structure."
---

# uloop get-hierarchy

Get Unity Hierarchy structure from the whole scene, a root path, or selected Hierarchy objects.

Use this for hierarchy structure, especially descendants under the current selection. Use `find-game-objects --search-mode Selected` when you need selected object details or component properties.

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

Returns JSON with:
- `message` (string): Human-readable guidance pointing at the saved file
- `hierarchyFilePath` (string): Filesystem path to the JSON file that contains the actual hierarchy data

The hierarchy itself is **not** in the response — it is written to the file at `hierarchyFilePath`. Open that file to read the `Context` and `Hierarchy` payload (GameObject tree, components, etc.).
