using System.Collections.Generic;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies which UI objects the annotator collects and how it classifies them, using in-test canvases with
    /// no EventSystem so every element's center counts as reachable. Also covers label assignment and the
    /// conversion to top-left coordinates. Every GameObject is destroyed in TearDown.
    /// </summary>
    public sealed class UIElementAnnotatorCollectTests
    {
        private const string Prefix = "UIElementAnnotatorCollectTests_";

        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject createdObject in _createdObjects)
            {
                if (createdObject != null)
                {
                    Object.DestroyImmediate(createdObject);
                }
            }

            _createdObjects.Clear();
        }

        /// <summary>
        /// Verifies each kind of interactable selectable under a raycastable canvas is collected with its type.
        /// </summary>
        [Test]
        public void CollectInteractiveElements_WithEachSelectableKind_ClassifiesThem()
        {
            Assume.That(EventSystem.current, Is.Null);
            Transform canvas = CreateCanvas("Overlay", RenderMode.ScreenSpaceOverlay, true, null);
            CreateUiChild<Button>(canvas, "Button");
            CreateUiChild<Toggle>(canvas, "Toggle");
            CreateUiChild<Slider>(canvas, "Slider");
            CreateUiChild<Dropdown>(canvas, "Dropdown");
            CreateUiChild<InputField>(canvas, "InputField");
            CreateUiChild<Scrollbar>(canvas, "Scrollbar");
            CreateUiChild<UIElementAnnotatorTestDraggableSelectable>(canvas, "DraggableSelectable");
            CreateUiChild<UIElementAnnotatorTestDropSelectable>(canvas, "DropSelectable");
            CreateUiChild<Selectable>(canvas, "PlainSelectable");

            Dictionary<string, string> typesByPath = CollectTypesByPath();

            string root = Prefix + "Overlay/";
            Assert.That(typesByPath[root + "Button"], Is.EqualTo("Button"));
            Assert.That(typesByPath[root + "Toggle"], Is.EqualTo("Toggle"));
            Assert.That(typesByPath[root + "Slider"], Is.EqualTo("Slider"));
            Assert.That(typesByPath[root + "Dropdown"], Is.EqualTo("Dropdown"));
            Assert.That(typesByPath[root + "InputField"], Is.EqualTo("InputField"));
            Assert.That(typesByPath[root + "Scrollbar"], Is.EqualTo("Scrollbar"));
            Assert.That(typesByPath[root + "DraggableSelectable"], Is.EqualTo("Draggable"));
            Assert.That(typesByPath[root + "DropSelectable"], Is.EqualTo("DropTarget"));
            Assert.That(typesByPath[root + "PlainSelectable"], Is.EqualTo("Selectable"));
        }

        /// <summary>
        /// Verifies scripts that only handle pointer events are collected, with drag taking priority over click.
        /// </summary>
        [Test]
        public void CollectInteractiveElements_WithEventHandlerScripts_ClassifiesThem()
        {
            Assume.That(EventSystem.current, Is.Null);
            Transform canvas = CreateCanvas("Handlers", RenderMode.ScreenSpaceOverlay, true, null);
            CreateUiChild<UIElementAnnotatorTestDragAndClickHandler>(canvas, "Drag");
            CreateUiChild<UIElementAnnotatorTestDropHandler>(canvas, "Drop");
            CreateUiChild<UIElementAnnotatorTestClickHandler>(canvas, "Click");
            CreateUiChild<UIElementAnnotatorTestPointerDownHandler>(canvas, "PointerDown");

            Dictionary<string, string> typesByPath = CollectTypesByPath();

            string root = Prefix + "Handlers/";
            Assert.That(typesByPath[root + "Drag"], Is.EqualTo("Draggable"));
            Assert.That(typesByPath[root + "Drop"], Is.EqualTo("DropTarget"));
            Assert.That(typesByPath[root + "Click"], Is.EqualTo("Button"));
            Assert.That(typesByPath[root + "PointerDown"], Is.EqualTo("Button"));
        }

        /// <summary>
        /// Verifies an object with both a selectable and a handler script is collected once, as the selectable.
        /// </summary>
        [Test]
        public void CollectInteractiveElements_WithAHandlerOnASelectable_CollectsItOnce()
        {
            Assume.That(EventSystem.current, Is.Null);
            Transform canvas = CreateCanvas("Shared", RenderMode.ScreenSpaceOverlay, true, null);
            GameObject shared = CreateUiChild<Button>(canvas, "Shared").gameObject;
            shared.AddComponent<UIElementAnnotatorTestDragAndClickHandler>();

            List<UIElementInfo> matches = CollectByPath(Prefix + "Shared/Shared");

            Assert.That(matches.Count, Is.EqualTo(1));
            Assert.That(matches[0].Type, Is.EqualTo("Button"));
        }

        /// <summary>
        /// Verifies disabled handlers, objects without a RectTransform, and objects under a canvas without a
        /// GraphicRaycaster are left out.
        /// </summary>
        [Test]
        public void CollectInteractiveElements_WithUnreachableObjects_LeavesThemOut()
        {
            Assume.That(EventSystem.current, Is.Null);
            Transform canvas = CreateCanvas("Skipped", RenderMode.ScreenSpaceOverlay, true, null);
            CreateUiChild<UIElementAnnotatorTestClickHandler>(canvas, "DisabledHandler").enabled = false;
            GameObject noRect = Track(new GameObject(Prefix + "NoRect"));
            noRect.AddComponent<UIElementAnnotatorTestClickHandler>();
            Transform noRaycaster = CreateCanvas("NoRaycaster", RenderMode.ScreenSpaceOverlay, false, null);
            CreateUiChild<Button>(noRaycaster, "Button");

            Dictionary<string, string> typesByPath = CollectTypesByPath();

            Assert.That(typesByPath.ContainsKey(Prefix + "Skipped/DisabledHandler"), Is.False);
            Assert.That(typesByPath.ContainsKey(Prefix + "NoRect"), Is.False);
            Assert.That(typesByPath.ContainsKey(Prefix + "NoRaycaster/Button"), Is.False);
        }

        /// <summary>
        /// Verifies a camera-space canvas projects its elements through its camera and records the canvas sorting order.
        /// </summary>
        [Test]
        public void CollectInteractiveElements_WithACameraCanvas_ProjectsThroughItsCamera()
        {
            Assume.That(EventSystem.current, Is.Null);
            Camera camera = Track(new GameObject(Prefix + "Camera")).AddComponent<Camera>();
            Transform canvas = CreateCanvas("CameraSpace", RenderMode.ScreenSpaceCamera, true, camera);
            canvas.GetComponent<Canvas>().sortingOrder = 7;
            CreateUiChild<Button>(canvas, "Button");

            List<UIElementInfo> matches = CollectByPath(Prefix + "CameraSpace/Button");

            Assert.That(matches.Count, Is.EqualTo(1));
            Assert.That(matches[0].SortingOrder, Is.EqualTo(7));
            Assert.That(matches[0].SimX, Is.EqualTo((matches[0].BoundsMinX + matches[0].BoundsMaxX) / 2f).Within(0.01f));
        }

        /// <summary>
        /// Verifies labels follow front-to-back order (higher sorting order, then higher sibling index) and continue
        /// with two letters after Z.
        /// </summary>
        [Test]
        public void AssignLabels_OrdersFrontToBackAndContinuesPastZ()
        {
            List<UIElementInfo> elements = new List<UIElementInfo>();
            for (int index = 0; index < 26; index++)
            {
                elements.Add(new UIElementInfo { Name = "Back" + index, SortingOrder = 0, SiblingIndex = index });
            }

            elements.Add(new UIElementInfo { Name = "FrontLow", SortingOrder = 5, SiblingIndex = 1 });
            elements.Add(new UIElementInfo { Name = "FrontHigh", SortingOrder = 5, SiblingIndex = 2 });

            UIElementAnnotator.AssignLabels(elements);

            Assert.That(elements[0].Name, Is.EqualTo("FrontHigh"));
            Assert.That(elements[0].Label, Is.EqualTo("A"));
            Assert.That(elements[1].Name, Is.EqualTo("FrontLow"));
            Assert.That(elements[2].Name, Is.EqualTo("Back25"));
            Assert.That(elements[25].Label, Is.EqualTo("Z"));
            Assert.That(elements[26].Label, Is.EqualTo("AA"));
            Assert.That(elements[27].Label, Is.EqualTo("AB"));
            Assert.That(elements[27].Name, Is.EqualTo("Back0"));
        }

        /// <summary>
        /// Verifies the point and bounds are flipped to a top-left origin with the bounds kept in min-max order.
        /// </summary>
        [Test]
        public void ConvertToSimCoordinates_FlipsThePointAndBounds()
        {
            UIElementInfo element = new UIElementInfo { SimY = 30f, BoundsMinY = 10f, BoundsMaxY = 50f };

            UIElementAnnotator.ConvertToSimCoordinates(new List<UIElementInfo> { element }, 100);

            Assert.That(element.SimY, Is.EqualTo(70f));
            Assert.That(element.BoundsMinY, Is.EqualTo(50f));
            Assert.That(element.BoundsMaxY, Is.EqualTo(90f));
        }

        private Transform CreateCanvas(string name, RenderMode renderMode, bool withRaycaster, Camera camera)
        {
            GameObject canvasObject = Track(new GameObject(Prefix + name));
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = renderMode;
            canvas.worldCamera = camera;
            if (withRaycaster)
            {
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            return canvasObject.transform;
        }

        private static T CreateUiChild<T>(Transform parent, string name) where T : Component
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            RectTransform rectTransform = child.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(40f, 20f);
            return child.AddComponent<T>();
        }

        private GameObject Track(GameObject gameObject)
        {
            _createdObjects.Add(gameObject);
            return gameObject;
        }

        private static Dictionary<string, string> CollectTypesByPath()
        {
            Dictionary<string, string> typesByPath = new Dictionary<string, string>();
            foreach (UIElementInfo element in UIElementAnnotator.CollectInteractiveElements())
            {
                if (element.Path.StartsWith(Prefix))
                {
                    typesByPath[element.Path] = element.Type;
                }
            }

            return typesByPath;
        }

        private static List<UIElementInfo> CollectByPath(string path)
        {
            return UIElementAnnotator.CollectInteractiveElements().FindAll(element => element.Path == path);
        }
    }

    internal sealed class UIElementAnnotatorTestDraggableSelectable : Selectable, IDragHandler
    {
        public void OnDrag(PointerEventData eventData)
        {
        }
    }

    internal sealed class UIElementAnnotatorTestDropSelectable : Selectable, IDropHandler
    {
        public void OnDrop(PointerEventData eventData)
        {
        }
    }

    internal sealed class UIElementAnnotatorTestDragAndClickHandler : MonoBehaviour, IDragHandler, IPointerClickHandler
    {
        public void OnDrag(PointerEventData eventData)
        {
        }

        public void OnPointerClick(PointerEventData eventData)
        {
        }
    }

    internal sealed class UIElementAnnotatorTestDropHandler : MonoBehaviour, IDropHandler
    {
        public void OnDrop(PointerEventData eventData)
        {
        }
    }

    internal sealed class UIElementAnnotatorTestClickHandler : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData eventData)
        {
        }
    }

    internal sealed class UIElementAnnotatorTestPointerDownHandler : MonoBehaviour, IPointerDownHandler
    {
        public void OnPointerDown(PointerEventData eventData)
        {
        }
    }
}
