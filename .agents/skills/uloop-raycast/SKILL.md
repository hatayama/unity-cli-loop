---
name: uloop-raycast
toolName: raycast
description: "Raycast from Camera.main through a Game View coordinate. Use when you need to check what a screenshot coordinate would hit in 3D physics before clicking or long-pressing with simulate-mouse-ui."
---

# uloop raycast

Check what a top-left Game View coordinate hits in 3D physics.

## Usage

```bash
uloop raycast --x <x> --y <y> [--layer-mask <mask>] [--max-distance <distance>]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--x` | number | `0` | Target X position in Game View pixels (origin: top-left). |
| `--y` | number | `0` | Target Y position in Game View pixels (origin: top-left). |
| `--layer-mask` | number | Unity default raycast layers | Physics layer mask used by the raycast. |
| `--max-distance` | number | `1000` | Maximum raycast distance in world units. |

## Coordinate System

- `--x` / `--y` use the same top-left Game View input coordinates as `simulate-mouse-ui`.
- Do not flip Y in the caller. The tool converts internally:

```text
unity_x = input_x
unity_y = gameViewHeight - input_y
```

- Device Simulator play view is supported. Prefer coordinates from `uloop screenshot --capture-mode rendering` (or its annotated `SimX`/`SimY`).

## Examples

```bash
# Check what is under a screenshot coordinate
uloop raycast --x 960 --y 540

# Check only specific layers
uloop raycast --x 960 --y 540 --layer-mask 1
```

## Output

Returns JSON with:
- `Success`: Whether the command completed
- `Message`: Status message
- `CameraName` / `CameraPath`: The camera that `Camera.main` resolved to and that the ray was cast from, reported on both hit and no-hit responses. When a `No physics hit` result looks wrong, check these first — another camera in the scene carrying the `MainCamera` tag can silently win the `Camera.main` resolution, so the ray may not come from the viewpoint you expect
- `Hit`: Whether physics hit anything
- `HitGameObjectName` / `HitGameObjectPath`: Hit object identity when `Hit` is true
- `HitLayer` / `HitLayerName`: Hit object layer when `Hit` is true
- `Distance`, `HitPointX/Y/Z`, `HitNormalX/Y/Z`: Hit details when `Hit` is true
- `InputCoordinateSystem`, `UnityCoordinateSystem`, `GameViewWidth/Height`, `InputPositionX/Y`, `InjectedUnityPositionX/Y`, `CoordinateConversionFormula`: Coordinate conversion details

## Notes

- Requires an active `Camera.main`.
- Uses Unity Physics raycasts, not UI EventSystem raycasts.
