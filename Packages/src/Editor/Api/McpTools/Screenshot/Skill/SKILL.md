---
name: uloop-screenshot
description: "Capture Unity Editor windows or Game View rendering as PNG. Use for visual checks, debugging, documentation, or annotated UI element coordinates."
---

# uloop screenshot

Take a screenshot of any Unity EditorWindow by name and save as PNG.

## Usage

```bash
uloop screenshot [--window-name <name>] [--resolution-scale <scale>] [--match-mode <mode>] [--capture-mode <mode>] [--annotate-elements] [--annotate-raycast-grid] [--raycast-layer-mask <layers>] [--elements-only] [--output-directory <path>]
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
| `--annotate-raycast-grid` | boolean | `false` | Annotate 3D physics raycast candidate points (R1, R2, ...). Only works with `--capture-mode rendering`; uses Camera.main and its culling mask. |
| `--raycast-layer-mask` | string | `""` | Comma-separated physics layer names for `--annotate-raycast-grid`. Hits are limited to layers also visible to Camera.main.cullingMask. When set, dense raycast samples are clustered by collider and returned as `Type="PhysicsCollider"` entries in `AnnotatedElements`; samples blocked by EventSystem `GraphicRaycaster` UI hits are skipped. |
| `--elements-only` | boolean | `false` | Return only annotation JSON without capturing a screenshot image. Requires `--annotate-elements` or `--annotate-raycast-grid`, and `--capture-mode rendering` in PlayMode. |

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

# Capture game rendering (default scale coordinates match simulate-mouse, PlayMode required)
uloop screenshot --capture-mode rendering

# Annotate interactive UI elements with index labels (for simulate-mouse workflow)
uloop screenshot --capture-mode rendering --annotate-elements

# Annotate 3D physics raycast candidate points
uloop screenshot --capture-mode rendering --annotate-raycast-grid

# Inspect hit layers, then rerun with the layer used by game input
uloop screenshot --capture-mode rendering --annotate-raycast-grid --elements-only
uloop screenshot --capture-mode rendering --annotate-raycast-grid --raycast-layer-mask Default --elements-only

# Annotate clustered 3D collider candidates on selected layers
uloop screenshot --capture-mode rendering --annotate-raycast-grid --raycast-layer-mask Ground,Clickable

# Get UI element coordinates without capturing an image (fastest)
uloop screenshot --capture-mode rendering --annotate-elements --elements-only

# Get clustered 3D collider coordinates without capturing an image
uloop screenshot --capture-mode rendering --annotate-raycast-grid --raycast-layer-mask Default --elements-only

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
  - `ImageCoordinateSystem`: `"top-left-game-view"` for `--capture-mode rendering`, or `"top-left-window"` for window captures
  - `ResolutionScale`: Resolution scale used for capture
  - `ImageToInputOffsetY`: Y offset added after unscaling image pixels to get mouse-input coordinates
  - `GameViewWidth` / `GameViewHeight`: Game View size used by mouse input tools
  - `ScreenshotToInputFormula`: Formula for turning image coordinates into mouse-input coordinates
  - `UnityInputFormula`: Formula used internally by mouse input tools to inject `Mouse.current.position`
  - `AnnotatedElements`: Array of annotated UI element metadata. Also includes clustered `PhysicsCollider` entries when `--annotate-raycast-grid --raycast-layer-mask <layers>` is used.
  - `RaycastGridPoints`: Array of coarse 3D raycast candidate metadata. Empty unless `--annotate-raycast-grid` is used without `--raycast-layer-mask`.
  - `RaycastLayerSummaries`: Dense raycast hit counts by layer when `--annotate-raycast-grid` is used without `--raycast-layer-mask`. Empty when a mask is provided.

For `AnnotatedElements` fields and Game View coordinates, read [references/annotated-elements.md](references/annotated-elements.md) before using screenshot coordinates with mouse simulation tools.

When multiple windows match (e.g., multiple Inspector windows or when using `contains` mode), all matching windows are captured with numbered filenames (e.g., `Inspector_1_*.png`, `Inspector_2_*.png`).

## Notes

- Use `uloop focus-window` first if needed
- Target window must be open in Unity Editor
- Window name matching is always case-insensitive
- Use `--capture-mode rendering` for coordinates that should be passed to `simulate-mouse-input`, `simulate-mouse-ui`, or `raycast`.
- Use `ScreenshotToInputFormula` before passing raw image pixels to mouse tools. `AnnotatedElements[].SimX/SimY` and `RaycastGridPoints[].InputX/InputY` are already mouse-input coordinates.
- To discover a useful physics layer, first run `--annotate-raycast-grid` without `--raycast-layer-mask`, inspect `RaycastLayerSummaries`, then rerun with `--raycast-layer-mask <Layer>`.
- Use `--raycast-layer-mask` with layer names from `find-game-objects` or project code when the game input code raycasts only specific layers. Layers hidden by Camera.main.cullingMask are not reported because they are not visible to the screenshot camera.
- Clustered `PhysicsCollider` entries avoid points where the frontmost EventSystem hit comes from a `GraphicRaycaster` UI element, including world-space Canvas UI. PhysicsRaycaster and other non-uGUI hits are not treated as UI occlusion. If the centroid-nearest sample is covered, the nearest uncovered sampled hit is used; if every sampled hit in the cluster is covered, that collider is omitted.
- Do not use `window` captures as mouse-input coordinates because they include Unity Editor chrome.
