using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how a prefab instance in a preview scene is classified, sized, and framed before rendering.
    /// </summary>
    public sealed class PrefabCaptureFramingTests
    {
        private static readonly Vector2Int GameViewSize = new(800, 600);
        private const float Tolerance = 0.001f;

        private Scene _scene;
        private Camera _camera;

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewPreviewScene();
            GameObject cameraObject = CreateInScene("Camera");
            _camera = cameraObject.AddComponent<Camera>();
            _camera.scene = _scene;
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(_scene);
        }

        /// <summary>
        /// Verifies a UI part is sized to the union of all RectTransforms, including a child that overflows the root.
        /// </summary>
        [Test]
        public void Arrange_WithAnOverflowingChild_SizesToTheUnionOfRectTransforms()
        {
            GameObject root = CreateUiPartWithOverflowingChild(out GameObject _);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.UiPart));
            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(140, 100)));
            Assert.That(_camera.orthographic, Is.True);
            Assert.That(_camera.orthographicSize, Is.EqualTo(50f).Within(Tolerance));
            Assert.That(_camera.transform.position.x, Is.EqualTo(-20f).Within(Tolerance));
            Assert.That(_camera.transform.position.y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(_camera.cameraType, Is.EqualTo(CameraType.Game));
            Assert.That(root.transform.parent.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("UI")));
        }

        /// <summary>
        /// Verifies an inactive child does not widen the UI part.
        /// </summary>
        [Test]
        public void Arrange_WithAnInactiveOverflowingChild_IgnoresTheChild()
        {
            GameObject root = CreateUiPartWithOverflowingChild(out GameObject child);
            child.SetActive(false);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(100, 100)));
        }

        /// <summary>
        /// Verifies a stretched UI root fills the Game View size.
        /// </summary>
        [Test]
        public void Arrange_WithAStretchedUiRoot_UsesTheGameViewSize()
        {
            GameObject root = CreateRectInScene("Root");
            RectTransform rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.UiPart));
            Assert.That(frame.ImageSize, Is.EqualTo(GameViewSize));
        }

        /// <summary>
        /// Verifies a UI root with no area is rejected as having nothing to capture.
        /// </summary>
        [Test]
        public void Arrange_WithAZeroSizedUiRoot_Throws()
        {
            GameObject root = CreateRectInScene("Root");
            ((RectTransform)root.transform).sizeDelta = Vector2.zero;

            Assert.Throws<UnityCliLoopToolParameterValidationException>(
                () => PrefabCaptureFraming.Arrange(root, _camera, GameViewSize));
        }

        /// <summary>
        /// Verifies a root screen-space Canvas with a nested Canvas is captured as screen UI through the capture camera.
        /// </summary>
        [Test]
        public void Arrange_WithAnOverlayCanvasRoot_SwitchesTheRootCanvasToTheCaptureCamera()
        {
            GameObject root = CreateInScene("Root");
            Canvas rootCanvas = root.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CreateChild(root, "Nested").AddComponent<Canvas>().overrideSorting = true;

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.ScreenUi));
            Assert.That(frame.ImageSize, Is.EqualTo(GameViewSize));
            Assert.That(_camera.cameraType, Is.EqualTo(CameraType.Game));
            Assert.That(rootCanvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
            Assert.That(rootCanvas.worldCamera, Is.SameAs(_camera));
        }

        /// <summary>
        /// Verifies a screen-space-camera Canvas under a plain Transform root renders through the capture camera.
        /// </summary>
        [Test]
        public void Arrange_WithAScreenSpaceCameraCanvasChild_AssignsTheCaptureCamera()
        {
            GameObject root = CreateInScene("Root");
            Canvas canvas = CreateChild(root, "Canvas").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.ScreenUi));
            Assert.That(canvas.worldCamera, Is.SameAs(_camera));
        }

        /// <summary>
        /// Verifies a RectTransform root with a world-space Canvas is treated as a UI part, not screen UI.
        /// </summary>
        [Test]
        public void Arrange_WithAWorldSpaceCanvasRoot_TreatsItAsAUiPart()
        {
            GameObject root = CreateRectInScene("Root");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)root.transform).sizeDelta = new Vector2(100f, 100f);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.UiPart));
        }

        /// <summary>
        /// Verifies a 3D object is framed by a perspective camera that sees every corner of its bounds.
        /// </summary>
        [Test]
        public void Arrange_WithACube_FramesAllBoundsCornersInPerspective()
        {
            GameObject root = CreateInScene("Root");
            Renderer cube = CreatePrimitiveChild(root, PrimitiveType.Cube);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.Object3D));
            Assert.That(frame.ImageSize, Is.EqualTo(GameViewSize));
            Assert.That(_camera.orthographic, Is.False);
            Assert.That(_camera.cameraType, Is.EqualTo(CameraType.Preview));
            AssertCornersInView(BoundsCorners(cube.bounds));
        }

        /// <summary>
        /// Verifies a directional light is added when the 3D prefab has no light of its own.
        /// </summary>
        [Test]
        public void Arrange_WithACubeAndNoLight_AddsADirectionalLight()
        {
            GameObject root = CreateInScene("Root");
            CreatePrimitiveChild(root, PrimitiveType.Cube);
            int lightsBefore = CountLights();

            PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(CountLights(), Is.EqualTo(lightsBefore + 1));
            Assert.That(CountDirectionalLightsOutside(root), Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies no light is added when the 3D prefab carries an enabled light.
        /// </summary>
        [Test]
        public void Arrange_WithACubeAndAnEnabledLight_AddsNoLight()
        {
            GameObject root = CreateInScene("Root");
            CreatePrimitiveChild(root, PrimitiveType.Cube);
            CreateChild(root, "Light").AddComponent<Light>();
            int lightsBefore = CountLights();

            PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(CountLights(), Is.EqualTo(lightsBefore));
        }

        /// <summary>
        /// Verifies a flat object is framed straight on by an unrotated orthographic camera.
        /// </summary>
        [Test]
        public void Arrange_WithAQuad_FramesItStraightOn()
        {
            GameObject root = CreateInScene("Root");
            Renderer quad = CreatePrimitiveChild(root, PrimitiveType.Quad);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.Flat2D));
            Assert.That(_camera.orthographic, Is.True);
            Assert.That(_camera.transform.rotation, Is.EqualTo(Quaternion.identity));
            Assert.That(_camera.cameraType, Is.EqualTo(CameraType.Preview));
            AssertCornersInView(BoundsCorners(quad.bounds));
        }

        /// <summary>
        /// Verifies a prefab with neither a Renderer nor a Canvas is rejected.
        /// </summary>
        [Test]
        public void Arrange_WithNothingToRender_Throws()
        {
            GameObject root = CreateInScene("Root");

            Assert.Throws<UnityCliLoopToolParameterValidationException>(
                () => PrefabCaptureFraming.Arrange(root, _camera, GameViewSize));
        }

        /// <summary>
        /// Verifies a prefab saved with an inactive root is activated so its content can be captured.
        /// </summary>
        [Test]
        public void Arrange_WithAnInactiveRoot_ActivatesTheRoot()
        {
            GameObject root = CreateInScene("Root");
            CreatePrimitiveChild(root, PrimitiveType.Cube);
            root.SetActive(false);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(root.activeSelf, Is.True);
            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.Object3D));
        }

        /// <summary>
        /// Verifies screen UI takes precedence over 3D content in the same prefab.
        /// </summary>
        [Test]
        public void Arrange_WithAnOverlayCanvasAndACube_PrefersScreenUi()
        {
            GameObject root = CreateInScene("Root");
            CreateChild(root, "Canvas").AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CreatePrimitiveChild(root, PrimitiveType.Cube);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.ScreenUi));
            Assert.That(_camera.cameraType, Is.EqualTo(CameraType.Game));
        }

        /// <summary>
        /// Verifies a world-space Canvas under a plain Transform root is framed straight on with all four corners visible.
        /// </summary>
        [Test]
        public void Arrange_WithOnlyAWorldSpaceCanvasChild_FramesItAsFlat()
        {
            GameObject root = CreateInScene("Root");
            GameObject canvasObject = CreateChild(root, "Canvas");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = new Vector2(100f, 100f);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.Flat2D));
            Assert.That(_camera.cameraType, Is.EqualTo(CameraType.Game));
            Vector3[] corners = new Vector3[4];
            canvasRect.GetWorldCorners(corners);
            AssertCornersInView(corners);
        }

        /// <summary>
        /// Verifies a scaled UI root is sized in UI units rather than world units.
        /// </summary>
        [Test]
        public void Arrange_WithAScaledWorldSpaceCanvasRoot_SizesInUiUnits()
        {
            GameObject root = CreateRectInScene("Root");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(100f, 100f);
            rect.localScale = new Vector3(0.01f, 0.01f, 1f);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.UiPart));
            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(100, 100)));
            Assert.That(root.transform.parent.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("UI")));
        }

        /// <summary>
        /// Verifies float noise from dividing by the root scale does not add a pixel to a scaled UI part.
        /// </summary>
        [Test]
        public void Arrange_WithAScaledRootOfAnAwkwardWidth_DoesNotAddAPixel()
        {
            GameObject root = CreateRectInScene("Root");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(109f, 100f);
            rect.localScale = new Vector3(0.01f, 0.01f, 1f);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(109, 100)));
        }

        /// <summary>
        /// Verifies a root that is its own world-space Canvas is captured at the Canvas size, not widened by a child that overflows it.
        /// </summary>
        [Test]
        public void Arrange_WithAWorldSpaceCanvasRootAndAnOverflowingChild_SizesToTheCanvas()
        {
            GameObject root = CreateUiPartWithOverflowingChild(out GameObject _);
            root.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.UiPart));
            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(100, 100)));
            Assert.That(_camera.transform.position.x, Is.EqualTo(0f).Within(Tolerance));
        }

        /// <summary>
        /// Verifies a zero-sized world-space Canvas root does not hide its children: they still decide the image size.
        /// </summary>
        [Test]
        public void Arrange_WithAZeroSizedWorldSpaceCanvasRoot_SizesToItsChildren()
        {
            GameObject root = CreateUiPartWithOverflowingChild(out GameObject child);
            ((RectTransform)root.transform).sizeDelta = Vector2.zero;
            // The child covers the root's pivot, so the zero-sized root adds nothing beyond the child.
            ((RectTransform)child.transform).anchoredPosition = new Vector2(-10f, 0f);
            root.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(40, 20)));
            Assert.That(_camera.transform.position.x, Is.EqualTo(-10f).Within(Tolerance));
        }

        /// <summary>
        /// Verifies a disabled Canvas on the root does not bound the capture, so an overflowing child still widens it.
        /// </summary>
        [Test]
        public void Arrange_WithADisabledCanvasRootAndAnOverflowingChild_IncludesTheChild()
        {
            GameObject root = CreateUiPartWithOverflowingChild(out GameObject _);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.enabled = false;

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(140, 100)));
        }

        /// <summary>
        /// Verifies the part of a child that a RectMask2D clips away does not widen the capture.
        /// </summary>
        [Test]
        public void Arrange_WithAChildPartlyClippedByARectMask2D_IgnoresTheClippedPart()
        {
            GameObject viewport = CreateUiPartWithMaskedChild(new Vector2(-60f, 0f), out GameObject root);
            viewport.AddComponent<RectMask2D>();

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(100, 100)));
            Assert.That(_camera.transform.position.x, Is.EqualTo(0f).Within(Tolerance));
        }

        /// <summary>
        /// Verifies a child that lies entirely outside a RectMask2D adds nothing to the capture.
        /// </summary>
        [Test]
        public void Arrange_WithAChildEntirelyOutsideARectMask2D_IgnoresTheChild()
        {
            GameObject viewport = CreateUiPartWithMaskedChild(new Vector2(-100f, 0f), out GameObject root);
            viewport.AddComponent<RectMask2D>();

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(100, 100)));
        }

        /// <summary>
        /// Verifies a disabled RectMask2D clips nothing, so the overflowing child still widens the capture.
        /// </summary>
        [Test]
        public void Arrange_WithADisabledRectMask2D_IncludesTheOverflowingChild()
        {
            GameObject viewport = CreateUiPartWithMaskedChild(new Vector2(-60f, 0f), out GameObject root);
            viewport.AddComponent<RectMask2D>().enabled = false;

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(130, 100)));
        }

        /// <summary>
        /// Verifies a stencil Mask with a Graphic clips its children like a RectMask2D does.
        /// </summary>
        [Test]
        public void Arrange_WithAChildClippedByAMaskWithAGraphic_IgnoresTheClippedPart()
        {
            GameObject viewport = CreateUiPartWithMaskedChild(new Vector2(-60f, 0f), out GameObject root);
            viewport.AddComponent<Image>();
            viewport.AddComponent<Mask>();

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(100, 100)));
        }

        /// <summary>
        /// Verifies a Mask without a Graphic masks nothing, so the overflowing child still widens the capture.
        /// </summary>
        [Test]
        public void Arrange_WithAMaskWithoutAGraphic_IncludesTheOverflowingChild()
        {
            GameObject viewport = CreateUiPartWithMaskedChild(new Vector2(-60f, 0f), out GameObject root);
            viewport.AddComponent<Mask>();

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(130, 100)));
        }

        /// <summary>
        /// Verifies a Mask whose Graphic is disabled masks nothing, as uGUI skips it when counting stencil depth.
        /// </summary>
        [Test]
        public void Arrange_WithAMaskWhoseGraphicIsDisabled_IncludesTheOverflowingChild()
        {
            GameObject viewport = CreateUiPartWithMaskedChild(new Vector2(-60f, 0f), out GameObject root);
            viewport.AddComponent<Image>().enabled = false;
            viewport.AddComponent<Mask>();

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(130, 100)));
        }

        /// <summary>
        /// Verifies a Graphic with maskable turned off is not clipped, because no mask reaches it in the game.
        /// </summary>
        [Test]
        public void Arrange_WithAnUnmaskableGraphicUnderARectMask2D_IncludesTheGraphic()
        {
            GameObject viewport = CreateUiPartWithMaskedChild(new Vector2(-60f, 0f), out GameObject root);
            viewport.AddComponent<RectMask2D>();
            viewport.transform.GetChild(0).gameObject.AddComponent<Image>().maskable = false;

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(130, 100)));
        }

        /// <summary>
        /// Verifies a sorting-override Canvas on the masked element shields it from the mask above, as uGUI does.
        /// </summary>
        [Test]
        public void Arrange_WithASortingOverrideCanvasBelowTheMask_IncludesTheOverflowingChild()
        {
            GameObject viewport = CreateUiPartWithMaskedChild(new Vector2(-60f, 0f), out GameObject root);
            viewport.AddComponent<RectMask2D>();
            Canvas canvas = AddSortingOverrideCanvas(viewport.transform.GetChild(0).gameObject, root);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(canvas.overrideSorting, Is.True);
            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(130, 100)));
        }

        /// <summary>
        /// Verifies a sorting-override Canvas between the mask and an element stops the mask from reaching the element.
        /// </summary>
        [Test]
        public void Arrange_WithASortingOverrideCanvasBetweenTheMaskAndTheChild_IncludesTheOverflowingChild()
        {
            GameObject viewport = CreateUiPartWithMaskedChild(new Vector2(-60f, 0f), out GameObject root);
            viewport.AddComponent<RectMask2D>();
            Transform child = viewport.transform.GetChild(0);
            GameObject middle = new GameObject("Middle", typeof(RectTransform));
            middle.transform.SetParent(viewport.transform, false);
            ((RectTransform)middle.transform).sizeDelta = new Vector2(100f, 100f);
            child.SetParent(middle.transform, false);
            Canvas canvas = AddSortingOverrideCanvas(middle, root);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(canvas.overrideSorting, Is.True);
            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(130, 100)));
        }

        /// <summary>
        /// Verifies a mask on the same object as a sorting-override Canvas still clips the children below it.
        /// </summary>
        [Test]
        public void Arrange_WithASortingOverrideCanvasOnTheMaskObject_StillClips()
        {
            GameObject viewport = CreateUiPartWithMaskedChild(new Vector2(-60f, 0f), out GameObject root);
            viewport.AddComponent<RectMask2D>();
            Canvas canvas = AddSortingOverrideCanvas(viewport, root);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(canvas.overrideSorting, Is.True);
            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(100, 100)));
        }

        /// <summary>
        /// Verifies a RectMask2D on the root itself limits the capture to the root.
        /// </summary>
        [Test]
        public void Arrange_WithARectMask2DOnTheRoot_SizesToTheRoot()
        {
            GameObject root = CreateUiPartWithOverflowingChild(out GameObject _);
            root.AddComponent<RectMask2D>();

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(100, 100)));
            Assert.That(_camera.transform.position.x, Is.EqualTo(0f).Within(Tolerance));
        }

        /// <summary>
        /// Verifies disabled screen-space Canvases do not make a prefab screen UI.
        /// </summary>
        [Test]
        public void Arrange_WithDisabledOverlayCanvases_TreatsTheRectRootAsAUiPart()
        {
            GameObject root = CreateInScene("Root");
            Canvas rootCanvas = root.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas nestedCanvas = CreateChild(root, "Nested").AddComponent<Canvas>();
            nestedCanvas.overrideSorting = true;
            rootCanvas.enabled = false;
            nestedCanvas.enabled = false;
            ((RectTransform)root.transform).sizeDelta = new Vector2(100f, 100f);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.UiPart));
        }

        /// <summary>
        /// Verifies a disabled Renderer is not counted, so a prefab with nothing else is rejected.
        /// </summary>
        [Test]
        public void Arrange_WithOnlyADisabledRenderer_Throws()
        {
            GameObject root = CreateInScene("Root");
            CreatePrimitiveChild(root, PrimitiveType.Cube).enabled = false;

            Assert.Throws<UnityCliLoopToolParameterValidationException>(
                () => PrefabCaptureFraming.Arrange(root, _camera, GameViewSize));
        }

        /// <summary>
        /// Verifies an overlay Canvas nested in a Canvas-less UI part does not turn the part into screen UI.
        /// </summary>
        [Test]
        public void Arrange_WithAnOverlayCanvasInsideACanvaslessUiRoot_TreatsItAsAUiPart()
        {
            GameObject root = CreateRectInScene("Root");
            ((RectTransform)root.transform).sizeDelta = new Vector2(100f, 100f);
            GameObject child = new GameObject("Child", typeof(RectTransform));
            child.transform.SetParent(root.transform, false);
            Canvas childCanvas = child.AddComponent<Canvas>();
            childCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            childCanvas.overrideSorting = true;
            RectTransform childRect = (RectTransform)child.transform;
            childRect.anchorMin = new Vector2(0.5f, 0.5f);
            childRect.anchorMax = new Vector2(0.5f, 0.5f);
            childRect.sizeDelta = new Vector2(40f, 20f);
            childRect.anchoredPosition = new Vector2(-70f, 0f);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.UiPart));
            Assert.That(frame.ImageSize, Is.EqualTo(new Vector2Int(140, 100)));
        }

        /// <summary>
        /// Verifies a prefab with renderers and a world-space Canvas uses a preview camera and frames both.
        /// </summary>
        [Test]
        public void Arrange_WithACubeAndAWorldSpaceCanvas_UsesAPreviewCameraAndFramesBoth()
        {
            GameObject root = CreateInScene("Root");
            Renderer cube = CreatePrimitiveChild(root, PrimitiveType.Cube);
            RectTransform canvasRect = CreateWorldSpaceCanvasChild(root);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.Object3D));
            Assert.That(_camera.cameraType, Is.EqualTo(CameraType.Preview));
            AssertCornersInView(BoundsCorners(cube.bounds));
            Vector3[] corners = new Vector3[4];
            canvasRect.GetWorldCorners(corners);
            AssertCornersInView(corners);
        }

        /// <summary>
        /// Verifies a renderer with empty bounds does not switch a world-space Canvas prefab to a preview camera, which would hide the Canvas.
        /// </summary>
        [Test]
        public void Arrange_WithAnEmptyParticleSystemAndAWorldSpaceCanvas_KeepsAGameCamera()
        {
            GameObject root = CreateInScene("Root");
            CreateChild(root, "Particles").AddComponent<ParticleSystem>();
            CreateWorldSpaceCanvasChild(root);

            PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(frame.Kind, Is.EqualTo(PrefabCaptureKind.Flat2D));
            Assert.That(_camera.cameraType, Is.EqualTo(CameraType.Game));
        }

        /// <summary>
        /// Verifies a MeshRenderer without a mesh has empty bounds and is rejected instead of rendering a blank image.
        /// </summary>
        [Test]
        public void Arrange_WithOnlyAMeshRendererWithoutAMesh_Throws()
        {
            GameObject root = CreateInScene("Root");
            CreateChild(root, "Renderer").AddComponent<MeshRenderer>();

            Assert.Throws<UnityCliLoopToolParameterValidationException>(
                () => PrefabCaptureFraming.Arrange(root, _camera, GameViewSize));
        }

        /// <summary>
        /// Verifies a particle system that has not emitted has empty bounds and is rejected instead of rendering a blank image.
        /// </summary>
        [Test]
        public void Arrange_WithOnlyAParticleSystem_Throws()
        {
            GameObject root = CreateInScene("Root");
            CreateChild(root, "Particles").AddComponent<ParticleSystem>();

            Assert.Throws<UnityCliLoopToolParameterValidationException>(
                () => PrefabCaptureFraming.Arrange(root, _camera, GameViewSize));
        }

        /// <summary>
        /// Verifies a disabled light in the prefab does not count, so a directional light is still added.
        /// </summary>
        [Test]
        public void Arrange_WithACubeAndADisabledLight_AddsADirectionalLight()
        {
            GameObject root = CreateInScene("Root");
            CreatePrimitiveChild(root, PrimitiveType.Cube);
            CreateChild(root, "Light").AddComponent<Light>().enabled = false;
            int lightsBefore = CountLights();

            PrefabCaptureFraming.Arrange(root, _camera, GameViewSize);

            Assert.That(CountLights(), Is.EqualTo(lightsBefore + 1));
            Assert.That(CountDirectionalLightsOutside(root), Is.EqualTo(1));
        }

        private GameObject CreateUiPartWithOverflowingChild(out GameObject child)
        {
            GameObject root = CreateRectInScene("Root");
            ((RectTransform)root.transform).sizeDelta = new Vector2(100f, 100f);
            child = new GameObject("Child", typeof(RectTransform));
            child.transform.SetParent(root.transform, false);
            RectTransform childRect = (RectTransform)child.transform;
            childRect.sizeDelta = new Vector2(40f, 20f);
            childRect.anchoredPosition = new Vector2(-70f, 0f);
            return root;
        }

        // Root 100x100 with a 100x100 viewport child; a 40x20 grandchild under the viewport sits at childPosition.
        private GameObject CreateUiPartWithMaskedChild(Vector2 childPosition, out GameObject root)
        {
            root = CreateRectInScene("Root");
            ((RectTransform)root.transform).sizeDelta = new Vector2(100f, 100f);
            GameObject viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(root.transform, false);
            ((RectTransform)viewport.transform).sizeDelta = new Vector2(100f, 100f);
            GameObject child = new GameObject("Child", typeof(RectTransform));
            child.transform.SetParent(viewport.transform, false);
            RectTransform childRect = (RectTransform)child.transform;
            childRect.sizeDelta = new Vector2(40f, 20f);
            childRect.anchoredPosition = childPosition;
            return viewport;
        }

        // Unity keeps overrideSorting only on a nested Canvas, so a temporary Canvas on the root makes the target
        // nested while the flag is set; the stored flag survives once the root Canvas is gone.
        private static Canvas AddSortingOverrideCanvas(GameObject target, GameObject root)
        {
            Canvas temporaryRootCanvas = root.AddComponent<Canvas>();
            Canvas canvas = target.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            Object.DestroyImmediate(temporaryRootCanvas);
            return canvas;
        }

        private static RectTransform CreateWorldSpaceCanvasChild(GameObject parent)
        {
            GameObject canvasObject = CreateChild(parent, "Canvas");
            canvasObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            RectTransform canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = new Vector2(100f, 100f);
            return canvasRect;
        }

        private GameObject CreateInScene(string name)
        {
            GameObject gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, _scene);
            return gameObject;
        }

        private GameObject CreateRectInScene(string name)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(gameObject, _scene);
            return gameObject;
        }

        private static GameObject CreateChild(GameObject parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private Renderer CreatePrimitiveChild(GameObject parent, PrimitiveType type)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            SceneManager.MoveGameObjectToScene(primitive, _scene);
            primitive.transform.SetParent(parent.transform, false);
            return primitive.GetComponent<Renderer>();
        }

        private int CountLights()
        {
            int count = 0;
            foreach (GameObject sceneRoot in _scene.GetRootGameObjects())
            {
                count += sceneRoot.GetComponentsInChildren<Light>(true).Length;
            }

            return count;
        }

        private int CountDirectionalLightsOutside(GameObject prefabRoot)
        {
            int count = 0;
            foreach (GameObject sceneRoot in _scene.GetRootGameObjects())
            {
                if (sceneRoot == prefabRoot)
                {
                    continue;
                }

                foreach (Light light in sceneRoot.GetComponentsInChildren<Light>(true))
                {
                    if (light.type == LightType.Directional)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static Vector3[] BoundsCorners(Bounds bounds)
        {
            Vector3[] corners = new Vector3[8];
            int index = 0;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        corners[index++] = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    }
                }
            }

            return corners;
        }

        private void AssertCornersInView(Vector3[] corners)
        {
            _camera.aspect = GameViewSize.x / (float)GameViewSize.y;
            foreach (Vector3 corner in corners)
            {
                Vector3 viewport = _camera.WorldToViewportPoint(corner);
                Assert.That(viewport.x, Is.InRange(0f, 1f), $"corner {corner} x");
                Assert.That(viewport.y, Is.InRange(0f, 1f), $"corner {corner} y");
                Assert.That(viewport.z, Is.GreaterThan(0f), $"corner {corner} z");
            }
        }
    }
}
