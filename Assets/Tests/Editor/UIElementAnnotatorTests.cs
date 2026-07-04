using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace io.github.hatayama.uLoopMCP
{
    public class UIElementAnnotatorTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();
        private EventSystem _previousEventSystem;
        private bool _previousEventSystemEnabled;

        [SetUp]
        public void SetUp()
        {
            _previousEventSystem = EventSystem.current;
            if (_previousEventSystem != null)
            {
                _previousEventSystemEnabled = _previousEventSystem.enabled;
                // EditMode EventSystems are not reliably registered, so clear stale scene state instead.
                _previousEventSystem.enabled = false;
            }

            GameObject eventSystem = CreateGameObject("UIElementAnnotatorTestsEventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_previousEventSystem != null)
            {
                _previousEventSystem.enabled = _previousEventSystemEnabled;
            }

            _previousEventSystem = null;
            _previousEventSystemEnabled = false;

            for (int i = _createdObjects.Count - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(_createdObjects[i]);
            }

            _createdObjects.Clear();
        }

        [Test]
        public void GetAnnotationColorForElement_WhenLabelsAreDifferent_ShouldReturnDifferentColors()
        {
            Color firstColor = UIElementAnnotator.GetAnnotationColorForElement(new UIElementInfo { Label = "A", Type = "Button" });
            Color secondColor = UIElementAnnotator.GetAnnotationColorForElement(new UIElementInfo { Label = "B", Type = "Button" });

            Assert.That(firstColor, Is.Not.EqualTo(secondColor));
        }

        [Test]
        public void GetAnnotationColorForElement_WhenTypeIsDifferentButLabelMatches_ShouldReturnSameColor()
        {
            Color buttonColor = UIElementAnnotator.GetAnnotationColorForElement(new UIElementInfo { Label = "C", Type = "Button" });
            Color draggableColor = UIElementAnnotator.GetAnnotationColorForElement(new UIElementInfo { Label = "C", Type = "Draggable" });

            Assert.That(buttonColor, Is.EqualTo(draggableColor));
        }

        [Test]
        public void GetAnnotationColorForElement_WhenFirstSixteenLabelsAreUsed_ShouldReturnUniqueColors()
        {
            HashSet<string> colorKeys = new HashSet<string>();
            for (int i = 0; i < 16; i++)
            {
                string label = ((char)('A' + i)).ToString();
                Color color = UIElementAnnotator.GetAnnotationColorForElement(new UIElementInfo { Label = label, Type = "Button" });

                colorKeys.Add(CreateColorKey(color));
            }

            Assert.That(colorKeys.Count, Is.EqualTo(16));
        }

        [Test]
        public void GetContrastingTextColor_WhenBackgroundIsBright_ShouldReturnDarkText()
        {
            Color textColor = UIElementAnnotator.GetContrastingTextColor(new Color(1f, 0.9f, 0f, 0.95f));

            Assert.That(textColor, Is.EqualTo(new Color(0f, 0f, 0f, 0.95f)));
        }

        [Test]
        public void GetContrastingTextColor_WhenBackgroundIsDark_ShouldReturnLightText()
        {
            Color textColor = UIElementAnnotator.GetContrastingTextColor(new Color(0.15f, 0.55f, 1f, 0.95f));

            Assert.That(textColor, Is.EqualTo(new Color(1f, 1f, 1f, 0.95f)));
        }

        [Test]
        public void GetContrastPartnerColor_WhenColorIsBright_ShouldReturnLightOutlineForDarkText()
        {
            Color outlineColor = UIElementAnnotator.GetContrastPartnerColor(new Color(1f, 0.9f, 0f, 0.95f));

            Assert.That(outlineColor, Is.EqualTo(new Color(1f, 1f, 1f, 0.95f)));
        }

        [Test]
        public void GetContrastPartnerColor_WhenColorIsDark_ShouldReturnDarkOutlineForLightText()
        {
            Color outlineColor = UIElementAnnotator.GetContrastPartnerColor(new Color(0.15f, 0.55f, 1f, 0.95f));

            Assert.That(outlineColor, Is.EqualTo(new Color(0f, 0f, 0f, 0.95f)));
        }

        [Test]
        public void CalculateBorderEdgeRects_WhenBoundsAreProvided_ShouldPlaceEdgesInsideTheBounds()
        {
            UIElementAnnotator.BorderEdgeRects edgeRects = UIElementAnnotator.CalculateBorderEdgeRects(
                10f, 20f, 110f, 70f, 2f);

            Assert.That(edgeRects.Top, Is.EqualTo(new Rect(10f, 68f, 100f, 2f)));
            Assert.That(edgeRects.Bottom, Is.EqualTo(new Rect(10f, 20f, 100f, 2f)));
            Assert.That(edgeRects.Left, Is.EqualTo(new Rect(10f, 22f, 2f, 46f)));
            Assert.That(edgeRects.Right, Is.EqualTo(new Rect(108f, 22f, 2f, 46f)));
        }

        [Test]
        public void GetAnnotationBorderColors_WhenAnnotationColorIsProvided_ShouldPutAnnotationColorInTheMiddle()
        {
            Color annotationColor = new Color(1f, 0.15f, 0.65f, 0.95f);
            UIElementAnnotator.AnnotationBorderColors borderColors =
                UIElementAnnotator.GetAnnotationBorderColors(annotationColor);

            Assert.That(borderColors.Inner, Is.EqualTo(new Color(0f, 0f, 0f, 0.95f)));
            Assert.That(borderColors.Middle, Is.EqualTo(annotationColor));
            Assert.That(borderColors.Outer, Is.EqualTo(new Color(1f, 1f, 1f, 0.95f)));
        }

        [Test]
        public void CreateAnnotationOverlay_WhenElementIsAnnotated_ShouldUseThickerColorBorder()
        {
            List<UIElementInfo> elements = new List<UIElementInfo>
            {
                new UIElementInfo
                {
                    Label = "A",
                    Type = "Button",
                    Interaction = "Click",
                    BoundsMinX = 10f,
                    BoundsMinY = 20f,
                    BoundsMaxX = 110f,
                    BoundsMaxY = 70f
                }
            };
            GameObject overlay = UIElementAnnotator.CreateAnnotationOverlay(elements, 1f);

            try
            {
                RectTransform innerTop = overlay.transform.Find("Border_DarkInner_Top").GetComponent<RectTransform>();
                RectTransform colorTop = overlay.transform.Find("Border_ColorMiddle_Top").GetComponent<RectTransform>();
                RectTransform outerTop = overlay.transform.Find("Border_LightOuter_Top").GetComponent<RectTransform>();

                Assert.That(innerTop.sizeDelta.y, Is.EqualTo(2f));
                Assert.That(colorTop.sizeDelta.y, Is.EqualTo(4f));
                Assert.That(outerTop.sizeDelta.y, Is.EqualTo(2f));
            }
            finally
            {
                UIElementAnnotator.DestroyAnnotationOverlay(overlay);
            }
        }

        [Test]
        public void CreateAnnotationOverlay_WhenElementIsAnnotated_ShouldKeepLabelOutlineAwayFromBorder()
        {
            List<UIElementInfo> elements = new List<UIElementInfo>
            {
                new UIElementInfo
                {
                    Label = "A",
                    Type = "Button",
                    Interaction = "Click",
                    BoundsMinX = 10f,
                    BoundsMinY = 20f,
                    BoundsMaxX = 110f,
                    BoundsMaxY = 70f
                }
            };
            GameObject overlay = UIElementAnnotator.CreateAnnotationOverlay(elements, 1f);

            try
            {
                RectTransform label = overlay.transform.Find("LabelBg").GetComponent<RectTransform>();

                Assert.That(label.anchoredPosition.y, Is.EqualTo(82f));
            }
            finally
            {
                UIElementAnnotator.DestroyAnnotationOverlay(overlay);
            }
        }

        public void CreateAnnotationOverlay_WhenMultipleElementsAreAnnotated_ShouldDrawLabelsAboveAllBorders()
        {
            List<UIElementInfo> elements = new List<UIElementInfo>
            {
                new UIElementInfo
                {
                    Label = "A",
                    Type = "Button",
                    Interaction = "Click",
                    BoundsMinX = 10f,
                    BoundsMinY = 20f,
                    BoundsMaxX = 110f,
                    BoundsMaxY = 70f
                },
                new UIElementInfo
                {
                    Label = "B",
                    Type = "Slider",
                    Interaction = "Drag",
                    BoundsMinX = 80f,
                    BoundsMinY = 90f,
                    BoundsMaxX = 210f,
                    BoundsMaxY = 130f
                }
            };
            GameObject overlay = UIElementAnnotator.CreateAnnotationOverlay(elements, 1f);

            try
            {
                int lastBorderSiblingIndex = -1;
                int firstLabelSiblingIndex = int.MaxValue;

                for (int i = 0; i < overlay.transform.childCount; i++)
                {
                    Transform child = overlay.transform.GetChild(i);
                    if (child.name.StartsWith("Border_"))
                    {
                        lastBorderSiblingIndex = i;
                    }

                    if (child.name == "LabelBg" && firstLabelSiblingIndex == int.MaxValue)
                    {
                        firstLabelSiblingIndex = i;
                    }
                }

                Assert.That(firstLabelSiblingIndex, Is.GreaterThan(lastBorderSiblingIndex));
            }
            finally
            {
                UIElementAnnotator.DestroyAnnotationOverlay(overlay);
            }
        }

        [Test]
        public void CreateAnnotationOverlay_WhenResolutionScaleIsHalf_ShouldCompensateBorderThickness()
        {
            List<UIElementInfo> elements = new List<UIElementInfo>
            {
                new UIElementInfo
                {
                    Label = "A",
                    Type = "Button",
                    Interaction = "Click",
                    BoundsMinX = 10f,
                    BoundsMinY = 20f,
                    BoundsMaxX = 110f,
                    BoundsMaxY = 70f
                }
            };
            GameObject overlay = UIElementAnnotator.CreateAnnotationOverlay(elements, 0.5f);

            try
            {
                RectTransform innerTop = overlay.transform.Find("Border_DarkInner_Top").GetComponent<RectTransform>();
                RectTransform colorTop = overlay.transform.Find("Border_ColorMiddle_Top").GetComponent<RectTransform>();
                RectTransform outerTop = overlay.transform.Find("Border_LightOuter_Top").GetComponent<RectTransform>();
                RectTransform label = overlay.transform.Find("LabelBg").GetComponent<RectTransform>();
                Outline labelOutline = overlay.transform.Find("LabelBg").GetComponent<Outline>();

                Assert.That(innerTop.sizeDelta.y, Is.EqualTo(4f));
                Assert.That(colorTop.sizeDelta.y, Is.EqualTo(8f));
                Assert.That(outerTop.sizeDelta.y, Is.EqualTo(4f));
                Assert.That(label.anchoredPosition.y, Is.EqualTo(94f));
                Assert.That(labelOutline.effectDistance, Is.EqualTo(new Vector2(4f, -4f)));
            }
            finally
            {
                UIElementAnnotator.DestroyAnnotationOverlay(overlay);
            }
        }

        [UnityTest]
        public IEnumerator CollectInteractiveElements_WhenCanvasIsDisabled_ShouldSkipButton()
        {
            GameObject canvas = CreateCanvas("DisabledCanvas", false);
            CreateButton("HiddenButton", canvas.transform, Vector2.zero);
            Canvas.ForceUpdateCanvases();
            yield return null;

            List<UIElementInfo> elements = UIElementAnnotator.CollectInteractiveElements();

            Assert.That(
                elements.Exists((UIElementInfo element) => element.Path == "DisabledCanvas/HiddenButton"),
                Is.False);
        }

        [UnityTest]
        public IEnumerator CollectInteractiveElements_WhenButtonIsVisible_ShouldIncludeButton()
        {
            GameObject canvas = CreateCanvas("VisibleCanvas", true);
            CreateButton("VisibleButton", canvas.transform, Vector2.zero);
            Canvas.ForceUpdateCanvases();
            yield return null;

            List<UIElementInfo> elements = UIElementAnnotator.CollectInteractiveElements();

            Assert.That(
                elements.Exists((UIElementInfo element) => element.Path == "VisibleCanvas/VisibleButton"),
                Is.True);
        }

        [Test]
        public void GetInteractionForType_WhenTypeIsButton_ShouldReturnClick()
        {
            string interaction = UIElementAnnotator.GetInteractionForType("Button");

            Assert.That(interaction, Is.EqualTo("Click"));
        }

        [Test]
        public void GetInteractionForType_WhenTypeIsSlider_ShouldReturnDrag()
        {
            string interaction = UIElementAnnotator.GetInteractionForType("Slider");

            Assert.That(interaction, Is.EqualTo("Drag"));
        }

        [Test]
        public void GetInteractionForType_WhenTypeIsDraggable_ShouldReturnDrag()
        {
            string interaction = UIElementAnnotator.GetInteractionForType("Draggable");

            Assert.That(interaction, Is.EqualTo("Drag"));
        }

        [Test]
        public void GetInteractionForType_WhenTypeIsDropTarget_ShouldReturnDrop()
        {
            string interaction = UIElementAnnotator.GetInteractionForType("DropTarget");

            Assert.That(interaction, Is.EqualTo("Drop"));
        }

        [Test]
        public void CreateDisplayLabel_WhenElementIsDraggable_ShouldAppendInteraction()
        {
            UIElementInfo element = new UIElementInfo
            {
                Label = "B",
                Type = "Draggable",
                Interaction = "Drag"
            };

            string displayLabel = UIElementAnnotator.CreateDisplayLabel(element);

            Assert.That(displayLabel, Is.EqualTo("B / DRAG"));
        }

        private static string CreateColorKey(Color color)
        {
            return $"{color.r:F3}:{color.g:F3}:{color.b:F3}:{color.a:F3}";
        }

        private GameObject CreateCanvas(string name, bool enabled)
        {
            GameObject canvasGo = CreateGameObject(name);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.enabled = enabled;
            canvasGo.AddComponent<GraphicRaycaster>();
            return canvasGo;
        }

        private Button CreateButton(string name, Transform parent, Vector2 anchoredPosition)
        {
            GameObject buttonGo = CreateGameObject(name);
            buttonGo.transform.SetParent(parent, false);
            RectTransform rectTransform = buttonGo.AddComponent<RectTransform>();
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = new Vector2(120f, 80f);
            Image image = buttonGo.AddComponent<Image>();
            image.raycastTarget = true;
            Button button = buttonGo.AddComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        private GameObject CreateGameObject(string name)
        {
            GameObject go = new GameObject(name);
            _createdObjects.Add(go);
            return go;
        }
    }
}
