# Annotated Elements and Coordinates

Read this when using `uloop screenshot --capture-mode rendering --annotate-elements` to find coordinates for `simulate-mouse-ui` or `simulate-mouse-input`.

## AnnotatedElements Fields

`AnnotatedElements` is empty unless `--annotate-elements` is used. Entries are sorted by z-order, frontmost first. Each item contains:

- `Label`: Index label shown on the screenshot (`A` = frontmost, `B` = next, ...)
- `Name`: Element name
- `Type`: Element type (`Button`, `Toggle`, `Slider`, `Dropdown`, `InputField`, `Scrollbar`, `Draggable`, `DropTarget`, `Selectable`)
- `SimX`, `SimY`: Center position in simulate-mouse coordinates. Use these directly with `--x` and `--y`.
- `BoundsMinX`, `BoundsMinY`, `BoundsMaxX`, `BoundsMaxY`: Bounding box in simulate-mouse coordinates
- `SortingOrder`: Canvas sorting order. Higher values are in front.
- `SiblingIndex`: Transform sibling index under the element's direct parent. Do not use it as a reliable z-order signal across nested UI hierarchies.

## Coordinate Conversion

When `CoordinateSystem` is `"gameView"`, convert image pixel coordinates to simulate-mouse coordinates:

```text
sim_x = image_x / ResolutionScale
sim_y = image_y / ResolutionScale + YOffset
```

When `ResolutionScale` is `1.0`, this simplifies to:

```text
sim_x = image_x
sim_y = image_y + YOffset
```
