# Annotated Elements and Coordinates

Read this when using `uloop screenshot --capture-mode rendering --annotate-elements` to find coordinates for `simulate-mouse-ui` or `simulate-mouse-input`.

## annotatedElements Fields

`annotatedElements` is empty unless `--annotate-elements` is used. Entries are sorted by z-order, frontmost first. Each item contains:

- `label`: Index label in JSON (`A` = frontmost, `B` = next, ...). Screenshot labels also include the interaction hint, such as `A / CLICK` or `B / DRAG`.
- `name`: Element name
- `path`: Hierarchy path from the scene root, for example `Canvas/Panel/Button`. Use this as `simulate-mouse-ui --target-path` when bypassing raycast blockers.
- `type`: Element type (`Button`, `Toggle`, `Slider`, `Dropdown`, `InputField`, `Scrollbar`, `Draggable`, `DropTarget`, `Selectable`)
- `interaction`: Derived interaction category (`Click`, `Drag`, `Drop`, `Text`). Use this to choose between `simulate-mouse-ui --action Click` and drag actions.
- `simX`, `simY`: Center position in simulate-mouse coordinates. Use these directly with `--x` and `--y`.
- `boundsMinX`, `boundsMinY`, `boundsMaxX`, `boundsMaxY`: Bounding box in simulate-mouse coordinates
- `sortingOrder`: Canvas sorting order. Higher values are in front.
- `siblingIndex`: Transform sibling index under the element's direct parent. Do not use it as a reliable z-order signal across nested UI hierarchies.

## Coordinate Conversion

When `coordinateSystem` is `"gameView"`, convert image pixel coordinates to simulate-mouse coordinates:

```text
sim_x = image_x / resolutionScale
sim_y = image_y / resolutionScale + yOffset
```

When `resolutionScale` is `1.0`, this simplifies to:

```text
sim_x = image_x
sim_y = image_y + yOffset
```

## Annotation Readability

Annotated screenshots compensate border thickness for `ResolutionScale`, so the saved PNG keeps the intended outline width after downscaling. The neutral contrast borders are 2 output pixels each, and the colored middle border is 4 output pixels. Label outlines are also compensated and are separated from element borders by a 4 output pixel gap.
