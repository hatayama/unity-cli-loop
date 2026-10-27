# Prefab Capture (`--prefab-path`)

`uloop screenshot --prefab-path <asset path>` instantiates the prefab or model asset in a temporary preview scene, renders it with a temporary camera, and returns one PNG. No window opens, and open scenes, Prefab Mode, and the asset itself are left untouched. It works in Edit Mode and Play Mode.

```bash
uloop screenshot --prefab-path Assets/UI/Panel.prefab
uloop screenshot --prefab-path Assets/UI/Panel.prefab --prefab-background-color "#00000000"
```

## How the prefab is framed

The first matching rule decides the image (the response `Message` says which one was used):

1. **UI part** — the root is a RectTransform with no enabled Canvas of its own, or its own Canvas is World Space. The image is sized to the union of all active RectTransforms, so children that overflow the root are included, and nested Canvases inside the part are drawn with it. Overflow that an enabled RectMask2D, or a Mask with a Graphic, clips is left out (the mask's rect, moved by RectMask2D padding as uGUI does; softness is ignored). When the root is itself an enabled World Space Canvas with a non-zero size, the image is the Canvas's own size and content outside it is cut off. Sizes are counted in UI units, so a world-space Canvas saved at a small scale keeps its pixel size. A stretched root fills the Game View size. The temporary Canvas that hosts the part sits on the built-in UI layer.
2. **Screen UI** — the root itself has an enabled Screen Space Canvas (Overlay or Camera), or the root is not a RectTransform and contains one. The outermost Canvases are switched to the capture camera and the image is the Game View size, so the layout matches the game. The prefab's own Canvases keep their layers, so a pipeline whose game cameras skip that layer draws nothing and the capture is rejected.
3. **Flat object** — content with no depth (sprites, quads, a lone world-space Canvas) is viewed straight on with an orthographic camera at the Game View size. A lone world-space Canvas is drawn on its own layer, so a pipeline whose game cameras skip that layer draws nothing and the capture is rejected.
4. **3D object** — anything else with an enabled Renderer is viewed from the front-left with a perspective camera at the Game View size, with the whole object in frame.

Prefabs with visible renderers (rules 3 and 4) are drawn with a preview camera, the kind Inspector previews use, so they appear even in pipelines whose game cameras draw only certain layers. Preview cameras do not draw uGUI, so in a prefab that mixes renderers and a world-space Canvas, the Canvas does not appear.

A prefab with nothing to render is rejected, including one whose renderers have empty bounds, such as a particle system that has not emitted. A render in which nothing appears (every pixel transparent or the background color) is rejected too; for a prefab that is a single flat color, pass a different `--prefab-background-color`. When a prefab has both Screen Space UI and 3D content, the UI rule wins and the 3D content may not appear.

The Game View size comes from the current Game View; change it with `uloop set-game-view-size`.

## Options

- `--window-name` and `--match-mode` are ignored.
- `--capture-mode` other than `auto`, `--annotate-elements`, `--annotate-raycast-grid`, `--raycast-layer-mask`, and `--elements-only` are rejected.
- `--resolution-scale` shrinks the image after it is rendered.
- `--prefab-background-color` takes `#RRGGBB` or `#RRGGBBAA`. The default `#808080` keeps both dark and white text readable. When the render pipeline clears the capture camera to the background color, the drawn colors are kept and only made opaque. When it does not, the background is composited after rendering under unpainted and translucent pixels. `#00000000` gives a transparent background with URP; transparent and translucent backgrounds depend on the render pipeline.
- `--prefab-background-color` without `--prefab-path` is rejected.

## What happens to the instance

- If the prefab has no enabled Light, one directional light is added to the preview scene.
- A root saved inactive is activated so it can be captured; children keep their saved state.
- Inside the preview scene, scripts other than `[ExecuteAlways]` ones do not run, even in Play Mode. uGUI draws normally.
- The prefab is rendered right after it is instantiated, so content that scripts assign later (asynchronous loads, Start, or Update) is missing, and post-processing such as bloom is not applied.

## Output

`ResolvedCaptureMode` is `"prefab"` and `ImageCoordinateSystem` is `"top-left-prefab-image"`. Prefab images are not Game View captures, so `ScreenshotToInputFormula` reports that mouse-input coordinates are unavailable.
