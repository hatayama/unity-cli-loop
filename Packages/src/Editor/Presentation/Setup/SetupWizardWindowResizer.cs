using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Measures UI Toolkit content and resizes the setup wizard window around it.
    /// </summary>
    internal sealed class SetupWizardWindowResizer
    {
        internal static readonly Vector2 MinimumWindowSize = new Vector2(360f, 380f);

        private const int PreferredWrappedTextLineCount = 2;

        private readonly EditorWindow _window;
        private readonly VisualElement _root;
        private readonly ScrollView _mainScrollView;
        private bool _isApplyingContentSize;
        private IVisualElementScheduledItem _resizeScheduledItem;

        internal SetupWizardWindowResizer(EditorWindow window, ScrollView mainScrollView)
        {
            Debug.Assert(window != null, "window must not be null");
            Debug.Assert(mainScrollView != null, "mainScrollView must not be null");

            _window = window ?? throw new System.ArgumentNullException(nameof(window));
            _root = window.rootVisualElement;
            _mainScrollView = mainScrollView ?? throw new System.ArgumentNullException(nameof(mainScrollView));
        }

        internal static Rect WithContentSize(Rect currentRect, Vector2 contentSize, Vector2 frameSize)
        {
            Vector2 measuredSize = contentSize + frameSize;
            Vector2 targetSize = new(
                Mathf.Max(measuredSize.x, MinimumWindowSize.x),
                Mathf.Max(measuredSize.y, MinimumWindowSize.y));
            return CreateCenteredRect(currentRect, targetSize);
        }

        internal static Rect CreateCenteredRect(Rect bounds, Vector2 size)
        {
            Vector2 centeredPosition = bounds.center - (size * 0.5f);
            return new Rect(centeredPosition, size);
        }

        internal static int EstimateWrappedLineCount(float laidOutTextHeight, float singleLineTextHeight)
        {
            if (singleLineTextHeight <= 0f) return 1;

            return Mathf.Max(1, Mathf.RoundToInt(laidOutTextHeight / singleLineTextHeight));
        }

        internal static float SelectPreferredTextWidth(
            float laidOutWidth,
            float measuredWidth,
            int lineCount,
            WhiteSpace whiteSpace)
        {
            if (whiteSpace != WhiteSpace.Normal) return measuredWidth;
            if (lineCount <= PreferredWrappedTextLineCount) return Mathf.Min(laidOutWidth, measuredWidth);

            return Mathf.Max(laidOutWidth, measuredWidth / PreferredWrappedTextLineCount);
        }

        internal static bool HasFiniteSize(Vector2 size)
        {
            return IsFinite(size.x) && IsFinite(size.y);
        }

        internal void BindSizeUpdates()
        {
            _root.RegisterCallback<GeometryChangedEvent>(HandleGeometryChanged);
        }

        internal void ScheduleResizeToContent()
        {
            _resizeScheduledItem?.Pause();
            _resizeScheduledItem = _root.schedule.Execute(ResizeToContent).StartingIn(0);
        }

        internal void Pause()
        {
            _resizeScheduledItem?.Pause();
        }

        private static Vector2 MeasureContentSize(ScrollView mainContainer)
        {
            VisualElement contentContainer = mainContainer.contentContainer;
            float width = MeasurePreferredContentWidth(mainContainer, contentContainer);
            float height = MeasurePreferredContentHeight(mainContainer, contentContainer);
            return new Vector2(width, height);
        }

        private static float MeasurePreferredContentWidth(VisualElement mainContainer, VisualElement contentContainer)
        {
            float maxRight = 0f;
            foreach (TextElement textElement in contentContainer.Query<TextElement>().Build())
            {
                if (!textElement.visible) continue;
                if (string.IsNullOrEmpty(textElement.text)) continue;
                if (!HasFiniteRect(textElement.worldBound)) continue;

                float left = textElement.worldBound.xMin - contentContainer.worldBound.xMin;
                float horizontalChrome =
                    textElement.resolvedStyle.paddingLeft
                    + textElement.resolvedStyle.paddingRight
                    + textElement.resolvedStyle.borderLeftWidth
                    + textElement.resolvedStyle.borderRightWidth;
                float verticalChrome =
                    textElement.resolvedStyle.paddingTop
                    + textElement.resolvedStyle.paddingBottom
                    + textElement.resolvedStyle.borderTopWidth
                    + textElement.resolvedStyle.borderBottomWidth;
                float laidOutWidth = textElement.worldBound.width;
                Vector2 measuredTextSize = textElement.MeasureTextSize(
                    textElement.text,
                    0f,
                    VisualElement.MeasureMode.Undefined,
                    0f,
                    VisualElement.MeasureMode.Undefined);
                if (!IsFinite(left)) continue;
                if (!IsFinite(horizontalChrome) || !IsFinite(verticalChrome)) continue;
                if (!HasFiniteSize(measuredTextSize)) continue;
                if (!IsFinite(laidOutWidth)) continue;
                float measuredWidth = measuredTextSize.x + horizontalChrome;
                int lineCount = EstimateWrappedLineCount(
                    textElement.worldBound.height - verticalChrome,
                    measuredTextSize.y);
                float preferredWidth = SelectPreferredTextWidth(
                    laidOutWidth,
                    measuredWidth,
                    lineCount,
                    textElement.resolvedStyle.whiteSpace);
                if (!IsFinite(preferredWidth)) continue;
                float right = left + preferredWidth;
                maxRight = Mathf.Max(maxRight, right);
            }

            float width =
                mainContainer.resolvedStyle.paddingLeft
                + maxRight
                + mainContainer.resolvedStyle.paddingRight;
            return IsFinite(width) ? Mathf.Ceil(width) : 0f;
        }

        private static float MeasurePreferredContentHeight(VisualElement mainContainer, VisualElement contentContainer)
        {
            float maxBottom = 0f;
            foreach (VisualElement child in contentContainer.Children())
            {
                if (!child.visible) continue;
                if (!HasFiniteRect(child.worldBound)) continue;
                float bottom = child.worldBound.yMax - contentContainer.worldBound.yMin;
                if (!IsFinite(bottom)) continue;
                maxBottom = Mathf.Max(maxBottom, bottom);
            }

            float height =
                mainContainer.resolvedStyle.paddingTop
                + maxBottom
                + mainContainer.resolvedStyle.paddingBottom;
            return IsFinite(height) ? Mathf.Ceil(height) : 0f;
        }

        private static bool Approximately(Vector2 left, Vector2 right)
        {
            const float Tolerance = 0.5f;
            return Mathf.Abs(left.x - right.x) < Tolerance && Mathf.Abs(left.y - right.y) < Tolerance;
        }

        private static bool HasFiniteRect(Rect rect)
        {
            return IsFinite(rect.xMin)
                && IsFinite(rect.xMax)
                && IsFinite(rect.yMin)
                && IsFinite(rect.yMax);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void HandleGeometryChanged(GeometryChangedEvent evt)
        {
            if (_isApplyingContentSize) return;

            ScheduleResizeToContent();
        }

        private void ResizeToContent()
        {
            if (_root.layout.width <= 0f || _root.layout.height <= 0f) return;

            Vector2 contentSize = MeasureContentSize(_mainScrollView);
            if (!HasFiniteSize(contentSize)) return;
            if (contentSize.x <= 0f || contentSize.y <= 0f) return;

            Vector2 frameSize = _window.position.size - _root.layout.size;
            if (!HasFiniteSize(frameSize)) return;
            Rect targetRect = WithContentSize(_window.position, contentSize, frameSize);
            if (!HasFiniteSize(targetRect.size)) return;
            if (Approximately(_window.position.size, targetRect.size))
            {
                _window.minSize = targetRect.size;
                _window.maxSize = targetRect.size;
                return;
            }

            _isApplyingContentSize = true;
            _window.minSize = targetRect.size;
            _window.maxSize = targetRect.size;
            _window.position = targetRect;
            _isApplyingContentSize = false;
        }
    }
}
