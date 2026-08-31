using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Measures UI Toolkit content and resizes the migration wizard window around it.
    /// </summary>
    internal sealed class ThirdPartyToolMigrationWizardWindowResizer
    {
        internal static readonly Vector2 InitialWindowSize = new Vector2(360f, 220f);
        internal static readonly Vector2 MinimumWindowSize = new Vector2(360f, 120f);
        private const float MaxHeightRatioOfMainWindow = 0.9f;

        private readonly EditorWindow _window;
        private readonly VisualElement _root;
        private readonly ScrollView _mainScrollView;
        private bool _hasFittedToContentOnce;
        private IVisualElementScheduledItem _resizeScheduledItem;

        internal ThirdPartyToolMigrationWizardWindowResizer(EditorWindow window, ScrollView mainScrollView)
        {
            Debug.Assert(window != null, "window must not be null");
            Debug.Assert(mainScrollView != null, "mainScrollView must not be null");

            _window = window ?? throw new System.ArgumentNullException(nameof(window));
            _root = window.rootVisualElement;
            _mainScrollView = mainScrollView ?? throw new System.ArgumentNullException(nameof(mainScrollView));
        }

        internal static Rect CreateCenteredRect(Rect bounds, Vector2 size)
        {
            Vector2 centeredPosition = bounds.center - (size * 0.5f);
            return new Rect(centeredPosition, size);
        }

        internal static Rect WithContentHeight(
            Rect currentRect,
            float contentHeight,
            Vector2 frameSize,
            float maxHeight)
        {
            Debug.Assert(contentHeight >= 0f, "contentHeight must not be negative");

            float measuredHeight = contentHeight + frameSize.y;
            float clampedMaxHeight = Mathf.Max(maxHeight, MinimumWindowSize.y);
            Vector2 targetSize = new Vector2(
                MinimumWindowSize.x,
                Mathf.Clamp(measuredHeight, MinimumWindowSize.y, clampedMaxHeight));
            return CreateCenteredRect(currentRect, targetSize);
        }

        internal static bool HasFiniteSize(Vector2 size)
        {
            return IsFinite(size.x) && IsFinite(size.y);
        }

        internal void ScheduleResizeToContent()
        {
            if (_hasFittedToContentOnce)
            {
                return;
            }

            _resizeScheduledItem?.Pause();
            _resizeScheduledItem = _root.schedule.Execute(ResizeToContent).StartingIn(0);
        }

        internal void Pause()
        {
            _resizeScheduledItem?.Pause();
        }

        private static float MeasurePreferredContentHeight(
            VisualElement mainContainer,
            VisualElement contentContainer)
        {
            float maxBottom = 0f;
            foreach (VisualElement child in contentContainer.Children())
            {
                if (!child.visible)
                {
                    continue;
                }

                if (!HasFiniteRect(child.worldBound))
                {
                    continue;
                }

                float bottom = child.worldBound.yMax - contentContainer.worldBound.yMin;
                if (!IsFinite(bottom))
                {
                    continue;
                }

                maxBottom = Mathf.Max(maxBottom, bottom);
            }

            float height =
                mainContainer.resolvedStyle.paddingTop
                + maxBottom
                + mainContainer.resolvedStyle.paddingBottom;
            return IsFinite(height) ? Mathf.Ceil(height) : 0f;
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

        private void ResizeToContent()
        {
            if (_root.layout.width <= 0f || _root.layout.height <= 0f)
            {
                return;
            }

            float contentHeight =
                MeasurePreferredContentHeight(_mainScrollView, _mainScrollView.contentContainer);
            if (!IsFinite(contentHeight) || contentHeight <= 0f)
            {
                return;
            }

            Vector2 frameSize = _window.position.size - _root.layout.size;
            if (!HasFiniteSize(frameSize))
            {
                return;
            }

            float maxHeight = EditorGUIUtility.GetMainWindowPosition().height * MaxHeightRatioOfMainWindow;
            Rect targetRect = WithContentHeight(_window.position, contentHeight, frameSize, maxHeight);
            if (!HasFiniteSize(targetRect.size))
            {
                return;
            }

            _window.minSize = MinimumWindowSize;
            _window.position = targetRect;
            _hasFittedToContentOnce = true;
        }
    }
}
