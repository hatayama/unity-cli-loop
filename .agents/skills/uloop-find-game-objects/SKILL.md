---
name: uloop-find-game-objects
description: "Find GameObjects in the active scene by various criteria. Use when you need to: (1) Search for objects by name, regex, or path, (2) Find objects with specific components, tags, or layers, (3) Get currently selected GameObjects in Unity Editor. Returns matching GameObjects with hierarchy paths and components (or writes to a file when multiple GameObjects are selected)."
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

Returns JSON with:
- `results` (array): Matching GameObjects, each containing:
  - `name` (string): GameObject name
  - `path` (string): Hierarchy path (e.g., `Canvas/Panel/Button`)
  - `isActive` (boolean): Active state in hierarchy
  - `tag` (string): GameObject tag
  - `layer` (number): Layer index
  - `components` (array): Each entry has `type` (short name, e.g., `Rigidbody`), `fullTypeName` (e.g., `UnityEngine.Rigidbody`), and `properties` (array of Inspector-visible `{name, type, value}` pairs)
- `totalFound` (number): Number of results returned inline, or number exported for multi-selection file output. For search modes, this is after `--max-results` clipping and serialization.
- `errorMessage` (string): Top-level failure summary (empty on success)
- `processingErrors` (array): Selected-mode per-GameObject serialization failures, each `{gameObjectName, gameObjectPath, error}`. Omitted/null or empty on clean runs.

### Multi-selection file export

For `Selected` mode with **multiple** successfully serialized GameObjects, inline `results` is not populated and the data is written to a file instead. Two extra fields appear:
- `resultsFilePath` (string): Relative path under `.uloop/outputs/FindGameObjectsResults/`
- `message` (string): Human-readable summary (e.g., "5 GameObjects exported")

Single-selection and search-mode calls (`Exact`, `Path`, `Regex`, `Contains`) always return inline. No selection (`Selected` mode with empty selection) returns empty `results` plus a `message`.
