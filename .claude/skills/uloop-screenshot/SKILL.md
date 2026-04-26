---
name: uloop-screenshot
description: "Capture screenshots of Unity Editor windows as PNG files. Use when you need to: (1) Screenshot Game View, Scene View, Console, Inspector, or other windows, (2) Capture current visual state for debugging or documentation, (3) Save editor window appearance as PNG files with optional UI element annotations. Writes PNG files via uloop CLI."
---

# uloop screenshot

Take a screenshot of any Unity EditorWindow by name and save as PNG.

## Usage

```bash
uloop screenshot [--window-name <name>] [--resolution-scale <scale>] [--match-mode <mode>] [--capture-mode <mode>] [--annotate-elements] [--elements-only] [--output-directory <path>]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--window-name` | string | `Game` | Window name to capture. Ignored when `--capture-mode rendering`. |
| `--resolution-scale` | number | `1.0` | Resolution scale (0.1 to 1.0) |
| `--match-mode` | enum | `exact` | Window name matching mode: `exact`, `prefix`, or `contains`. Ignored when `--capture-mode rendering`. |
| `--capture-mode` | enum | `window` | `window`=capture EditorWindow including toolbar, `rendering`=capture game rendering only (PlayMode required, coordinates match simulate-mouse) |
| `--output-directory` | string | `""` | Output directory path for saving screenshots. When empty, uses default path (.uloop/outputs/Screenshots/). Accepts absolute paths. |
| `--annotate-elements` | boolean | `false` | Annotate interactive UI elements with index labels and interaction hints (A / CLICK, B / DRAG, ...). Only works with `--capture-mode rendering` in PlayMode. |
| `--elements-only` | boolean | `false` | Return only annotated element JSON without capturing a screenshot image. Requires `--annotate-elements` and `--capture-mode rendering` in PlayMode. |

## Match Modes

| Mode | Description | Example |
|------|-------------|---------|
| `exact` | Window name must match exactly (case-insensitive) | "Project" matches "Project" only |
| `prefix` | Window name must start with the input | "Project" matches "Project" and "Project Settings" |
| `contains` | Window name must contain the input anywhere | "set" matches "Project Settings" |

## Window Name

The window name is the text displayed in the window's title bar (tab). Common names: Game, Scene, Console, Inspector, Project, Hierarchy, Animation, Animator, Profiler. Custom EditorWindow titles are also supported.

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# Take a screenshot of Game View (default)
uloop screenshot

# Capture game rendering (coordinates match simulate-mouse, PlayMode required)
uloop screenshot --capture-mode rendering

# Annotate interactive UI elements with index labels (for simulate-mouse workflow)
uloop screenshot --capture-mode rendering --annotate-elements

# Get UI element coordinates without capturing an image (fastest)
uloop screenshot --capture-mode rendering --annotate-elements --elements-only

# Take a screenshot of Scene View
uloop screenshot --window-name Scene

# Capture all windows starting with "Project" (prefix match)
uloop screenshot --window-name Project --match-mode prefix

# Save screenshot to a specific directory
uloop screenshot --output-directory /tmp/screenshots

# Combine options
uloop screenshot --window-name Scene --resolution-scale 0.5 --output-directory /tmp/screenshots
```

## Output

Returns JSON with:
- `ScreenshotCount`: Number of windows captured
- `Screenshots`: Array of screenshot info, each containing:
  - `ImagePath`: Absolute path to the saved PNG file. Empty when `--elements-only` is used because no image file is written.
  - `FileSizeBytes`: Size of the saved file in bytes
  - `Width`: Captured image width in pixels
  - `Height`: Captured image height in pixels
  - `CoordinateSystem`: `"gameView"` or `"window"`
  - `ResolutionScale`: Resolution scale used for capture
  - `YOffset`: Y offset used for gameView coordinate conversion
  - `AnnotatedElements`: Array of annotated UI element metadata. Empty unless `--annotate-elements` is used.

For `AnnotatedElements` fields and gameView coordinate conversion, read [references/annotated-elements.md](references/annotated-elements.md) before using screenshot coordinates with mouse simulation tools.

When multiple windows match (e.g., multiple Inspector windows or when using `contains` mode), all matching windows are captured with numbered filenames (e.g., `Inspector_1_*.png`, `Inspector_2_*.png`).

## Notes

- Use `uloop focus-window` first if needed
- Target window must be open in Unity Editor
- Window name matching is always case-insensitive
