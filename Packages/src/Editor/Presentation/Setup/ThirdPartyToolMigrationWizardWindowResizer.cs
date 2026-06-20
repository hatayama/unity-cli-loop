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

        private readonly EditorWindow _window;
        private readonly VisualElement _root;
        private readonly ScrollView _mainScrollView;
        private bool _isApplyingContentSize;
        private IVisualElementScheduledItem _resizeScheduledItem;

        internal ThirdPartyToolMigrationWizardWindowResizer(EditorWindow window, ScrollView mainScrollView)
        {
            Debug.Assert(window != null, "window must not be null");
            Debug.Assert(mainScrollView != null, "mainScrollView must not be null");

            _window = window;
            _root = window.rootVisualElement;
            _mainScrollView = mainScrollView;
        }

        internal static Rect CreateCenteredRect(Rect bounds, Vector2 size)
        {
            Vector2 centeredPosition = bounds.center - (size * 0.5f);
            return new Rect(centeredPosition, size);
        }

        internal static Rect WithContentHeight(Rect currentRect, float contentHeight, Vector2 frameSize)
        {
            Debug.Assert(contentHeight >= 0f, "contentHeight must not be negative");

            float measuredHeight = contentHeight + frameSize.y;
            Vector2 targetSize = new Vector2(
                MinimumWindowSize.x,
                Mathf.Max(measuredHeight, MinimumWindowSize.y));
            return CreateCenteredRect(currentRect, targetSize);
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
            if (_isApplyingContentSize)
            {
                return;
            }

            ScheduleResizeToContent();
        }

        private void ResizeToContent()
        {
            if (_mainScrollView == null)
            {
                return;
            }

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

            Rect targetRect = WithContentHeight(_window.position, contentHeight, frameSize);
            if (!HasFiniteSize(targetRect.size))
            {
                return;
            }

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
