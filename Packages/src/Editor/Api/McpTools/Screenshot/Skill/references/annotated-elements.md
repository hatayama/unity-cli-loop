# Annotated Elements and Coordinates

Read this when using `uloop screenshot --capture-mode rendering --annotate-elements` to find coordinates for `simulate-mouse-ui` or `simulate-mouse-input`.

## AnnotatedElements Fields

`AnnotatedElements` is empty unless `--annotate-elements` is used. Entries are sorted by z-order, frontmost first. Each item contains:

- `Label`: Index label in JSON (`A` = frontmost, `B` = next, ...). Screenshot labels also include the interaction hint, such as `A / CLICK` or `B / DRAG`.
- `Name`: Element name
- `Path`: Hierarchy path from the scene root, for example `Canvas/Panel/Button`. Use this as `simulate-mouse-ui --target-path` when bypassing raycast blockers.
- `Type`: Element type (`Button`, `Toggle`, `Slider`, `Dropdown`, `InputField`, `Scrollbar`, `Draggable`, `DropTarget`, `Selectable`)
- `Interaction`: Derived interaction category (`Click`, `Drag`, `Drop`, `Text`). Use this to choose between `simulate-mouse-ui --action Click` and drag actions.
- `SimX`, `SimY`: Center position in top-left Game View coordinates. Use these directly with `simulate-mouse-ui --x/--y`, `simulate-mouse-input --x/--y`, or `raycast --x/--y`.
- `BoundsMinX`, `BoundsMinY`, `BoundsMaxX`, `BoundsMaxY`: Bounding box in simulate-mouse coordinates
- `SortingOrder`: Canvas sorting order. Higher values are in front.
- `SiblingIndex`: Transform sibling index under the element's direct parent. Do not use it as a reliable z-order signal across nested UI hierarchies.

## Coordinate Conversion

When `ImageCoordinateSystem` is `"top-left-game-view"`, convert raw image pixel coordinates from `screenshot --capture-mode rendering` with the formula returned in `ScreenshotToInputFormula`:

```text
input_x = image_x / resolutionScale
input_y = image_y / resolutionScale + imageToInputOffsetY
```

When `ResolutionScale` is `1.0` and `ImageToInputOffsetY` is `0`, raw image pixel coordinates already match mouse-input coordinates. `AnnotatedElements[].SimX/SimY` and `RaycastGridPoints[].InputX/InputY` are always returned as mouse-input coordinates, so pass those values directly.

The mouse input tools convert internally to Unity Input System coordinates:

```text
unity_x = input_x
unity_y = gameViewHeight - input_y
```

Do not flip Y in the caller.

## Annotation Readability

Annotated screenshots compensate border thickness for `ResolutionScale`, so the saved PNG keeps the intended outline width after downscaling. The neutral contrast borders are 2 output pixels each, and the colored middle border is 4 output pixels. Label outlines are also compensated and are separated from element borders by a 4 output pixel gap.
