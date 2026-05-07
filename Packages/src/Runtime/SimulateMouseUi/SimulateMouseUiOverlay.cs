#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    // Canvas-based overlay that visualizes SimulateMouseUi cursor position and drag state on Game View.
    // Animation is driven externally via SetCursorScale/SetAlpha from async functions in SimulateMouseUiTool.
    /// <summary>
    /// Drives the runtime overlay used by Simulate Mouse UI behavior.
    /// </summary>
    public class SimulateMouseUiOverlay : MonoBehaviour
    {
        private const float WAYPOINT_MARKER_DIAMETER = 12f;
        private const float LINE_THICKNESS = 2f;

        private static readonly Color CLICK_COLOR = new Color(0f, 1f, 0.4f, 0.9f);
        private static readonly Color DRAG_COLOR = new Color(1f, 0.6f, 0f, 0.9f);

        [SerializeField] private CanvasGroup _canvasGroup = null!;
        [SerializeField] private RectTransform _cursorGroup = null!;
        [SerializeField] private Image _circleImage = null!;
        [SerializeField] private Image _crosshairH = null!;
        [SerializeField] private Image _crosshairV = null!;
        [SerializeField] private Text _longPressText = null!;
        [SerializeField] private Image _dragStartMarker = null!;
        [SerializeField] private Sprite _circleSprite = null!;

        private readonly List<Image> _pathSegments = new List<Image>();
        private readonly List<Image> _waypointMarkers = new List<Image>();

        private const float SELF_EXPAND_DURATION = SimulateMouseUiAnimationConstants.EXPAND_DURATION;
        private const float SELF_EXPAND_START_SCALE = SimulateMouseUiAnimationConstants.EXPAND_START_SCALE;
        private const float SELF_DISSIPATE_DURATION = SimulateMouseUiAnimationConstants.DISSIPATE_DURATION;

        private enum SelfAnimState { None, Expanding, Dissipating }
        private SelfAnimState _selfAnimState = SelfAnimState.None;
        private float _selfAnimStartTime;

        private void Awake()
        {
            Debug.Assert(_canvasGroup != null, "_canvasGroup must be assigned in prefab");
            Debug.Assert(_cursorGroup != null, "_cursorGroup must be assigned in prefab");
            Debug.Assert(_circleImage != null, "_circleImage must be assigned in prefab");
            Debug.Assert(_crosshairH != null, "_crosshairH must be assigned in prefab");
            Debug.Assert(_crosshairV != null, "_crosshairV must be assigned in prefab");
            Debug.Assert(_longPressText != null, "_longPressText must be assigned in prefab");
            Debug.Assert(_dragStartMarker != null, "_dragStartMarker must be assigned in prefab");
            Debug.Assert(_circleSprite != null, "_circleSprite must be assigned in prefab");

            _canvasGroup!.alpha = 0;
        }

        private void LateUpdate()
        {
            ConsumePendingAnimationRequests();

            // Dissipate runs independently of overlay state (state is already cleared on release)
            if (_selfAnimState == SelfAnimState.Dissipating)
            {
                float elapsed = Time.realtimeSinceStartup - _selfAnimStartTime;
                float t = Mathf.Clamp01(elapsed / SELF_DISSIPATE_DURATION);
                _cursorGroup.localScale = Vector3.one * Mathf.Lerp(1f, 0f, t);
                _canvasGroup.alpha = Mathf.Lerp(1f, 0f, t);
                if (t >= 1f)
                {
                    _selfAnimState = SelfAnimState.None;
                    _canvasGroup.alpha = 0f;
                }
                return;
            }

            if (!SimulateMouseUiOverlayState.IsActive)
            {
                _canvasGroup.alpha = 0;
                return;
            }

            // SimulateMouseUiTool controls alpha via SetAlpha during animations.
            // For non-animated callers (e.g. InputReplayer), auto-show on first active frame.
            if (_canvasGroup.alpha == 0f)
            {
                _canvasGroup.alpha = 1f;
                _cursorGroup.localScale = Vector3.one;
            }

            if (_selfAnimState == SelfAnimState.Expanding)
            {
                float elapsed = Time.realtimeSinceStartup - _selfAnimStartTime;
                float t = Mathf.Clamp01(elapsed / SELF_EXPAND_DURATION);
                _cursorGroup.localScale = Vector3.one * Mathf.Lerp(SELF_EXPAND_START_SCALE, 1f, t);
                if (t >= 1f)
                {
                    _selfAnimState = SelfAnimState.None;
                }
            }

            UpdateCursorPosition();
            UpdateCursorMode();
            UpdateDragPath();
        }

        private void ConsumePendingAnimationRequests()
        {
            if (SimulateMouseUiOverlayState.ConsumePendingExpandAnimation())
            {
                _canvasGroup.alpha = 1f;
                _cursorGroup.localScale = Vector3.one * SELF_EXPAND_START_SCALE;
                _selfAnimState = SelfAnimState.Expanding;
                _selfAnimStartTime = Time.realtimeSinceStartup;
            }

            if (SimulateMouseUiOverlayState.ConsumePendingDissipateAnimation())
            {
                _selfAnimState = SelfAnimState.Dissipating;
                _selfAnimStartTime = Time.realtimeSinceStartup;
            }
        }

        public void SetCursorScale(float scale)
        {
            _cursorGroup.localScale = Vector3.one * scale;
            // SimulateMouseUiTool drives animation externally; cancel any self-driven animation
            _selfAnimState = SelfAnimState.None;
        }

        public void SetAlpha(float alpha)
        {
            _canvasGroup.alpha = alpha;
            _selfAnimState = SelfAnimState.None;
        }

        // sim coordinate: simX = screen pixel X, simY = EditorScreen.height - canvasY (top-left origin)
        // Canvas Screen Space Overlay position: bottom-left origin
        private static Vector2 SimToScreen(Vector2 simPos)
        {
            Vector2 srcSize = SimulateMouseUiOverlayState.SourceScreenSize;
            Debug.Assert(srcSize.x > 0f && srcSize.y > 0f, "SourceScreenSize must be set before SimToScreen is called");
            return new Vector2(simPos.x, srcSize.y - simPos.y);
        }

        private void UpdateCursorPosition()
        {
            Vector2 screenPos = SimToScreen(SimulateMouseUiOverlayState.CurrentPosition);
            _cursorGroup.position = new Vector3(screenPos.x, screenPos.y, 0f);
        }

        private void UpdateCursorMode()
        {
            bool isLongPress = SimulateMouseUiOverlayState.Action == MouseAction.LongPress;
            _crosshairH.enabled = !isLongPress;
            _crosshairV.enabled = !isLongPress;
            _longPressText.enabled = isLongPress;

            if (isLongPress)
            {
                _longPressText.text = SimulateMouseUiOverlayState.LongPressElapsed.ToString("F1") + "s";

                // Pulse the circle between 1.0x and 1.2x scale over a 2-second cycle
                float t = Mathf.PingPong(SimulateMouseUiOverlayState.LongPressElapsed, 2f) / 2f;
                float scale = Mathf.Lerp(1.0f, 1.2f, t);
                _circleImage.rectTransform.localScale = Vector3.one * scale;
            }
            else
            {
                _circleImage.rectTransform.localScale = Vector3.one;
            }
        }

        private void UpdateDragPath()
        {
            if (!SimulateMouseUiOverlayState.DragStartPosition.HasValue)
            {
                _dragStartMarker.enabled = false;
                HidePool(_pathSegments);
                HidePool(_waypointMarkers);
                // Keep a reasonable number of pooled objects for reuse across drags
                TrimPool(_pathSegments, 5);
                TrimPool(_waypointMarkers, 4);
                return;
            }

            Color activeColor = GetActiveColor();

            // Start marker
            Vector2 startScreen = SimToScreen(SimulateMouseUiOverlayState.DragStartPosition.Value);
            _dragStartMarker.enabled = true;
            _dragStartMarker.color = activeColor;
            _dragStartMarker.rectTransform.position = new Vector3(startScreen.x, startScreen.y, 0f);

            // Polyline: start → waypoints → current
            IReadOnlyList<Vector2> waypoints = SimulateMouseUiOverlayState.DragWaypoints;
            Vector2 currentScreen = SimToScreen(SimulateMouseUiOverlayState.CurrentPosition);

            int segmentCount = waypoints.Count + 1;
            EnsurePoolCount(_pathSegments, segmentCount, CreatePooledSegment);
            EnsurePoolCount(_waypointMarkers, waypoints.Count, CreatePooledWaypointMarker);

            // All segments fade: oldest = transparent, newest = most visible (but not fully opaque)
            float canvasScale = _pathSegments.Count > 0 ? _pathSegments[0].rectTransform.lossyScale.x : 1f;
            Vector2 prev = startScreen;
            for (int i = 0; i < segmentCount; i++)
            {
                Vector2 to = (i < waypoints.Count) ? SimToScreen(waypoints[i]) : currentScreen;
                float alpha = (float)(i + 1) / (segmentCount + 1);
                Color fadedColor = activeColor;
                fadedColor.a = activeColor.a * alpha;
                DrawSegment(_pathSegments[i], prev, to, fadedColor, canvasScale);

                if (i < waypoints.Count)
                {
                    _waypointMarkers[i].enabled = true;
                    _waypointMarkers[i].color = fadedColor;
                    _waypointMarkers[i].rectTransform.position = new Vector3(to.x, to.y, 0f);
                }

                prev = to;
            }
            HidePoolFrom(_pathSegments, segmentCount);
            HidePoolFrom(_waypointMarkers, waypoints.Count);
        }

        private void DrawSegment(Image line, Vector2 from, Vector2 to, Color color, float canvasScale)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;

            if (length < 1f)
            {
                line.enabled = false;
                return;
            }

            line.enabled = true;
            line.color = color;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

            RectTransform rect = line.rectTransform;
            rect.position = new Vector3(from.x, from.y, 0f);
            // sizeDelta is in local space; convert screen-pixel length via lossyScale
            rect.sizeDelta = new Vector2(length / canvasScale, LINE_THICKNESS / canvasScale);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private Image CreatePooledSegment()
        {
            Image line = CreateImage("PathSegment", gameObject.transform);
            line.rectTransform.pivot = new Vector2(0f, 0.5f);
            line.rectTransform.sizeDelta = Vector2.zero;
            line.enabled = false;
            return line;
        }

        private Image CreatePooledWaypointMarker()
        {
            Image marker = CreateImage("WaypointMarker", gameObject.transform);
            marker.rectTransform.sizeDelta = new Vector2(WAYPOINT_MARKER_DIAMETER, WAYPOINT_MARKER_DIAMETER);
            marker.sprite = _circleSprite;
            marker.enabled = false;
            return marker;
        }

        private void EnsurePoolCount(List<Image> pool, int count, System.Func<Image> factory)
        {
            while (pool.Count < count)
            {
                pool.Add(factory());
            }
        }

        private static void HidePool(List<Image> pool)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                pool[i].enabled = false;
            }
        }

        // Pools only grow via EnsurePoolCount; trim excess to prevent unbounded accumulation
        private static void TrimPool(List<Image> pool, int maxRetain)
        {
            while (pool.Count > maxRetain)
            {
                Image excess = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
                Destroy(excess.gameObject);
            }
        }

        private static void HidePoolFrom(List<Image> pool, int startIndex)
        {
            for (int i = startIndex; i < pool.Count; i++)
            {
                pool[i].enabled = false;
            }
        }

        private Color GetActiveColor()
        {
            return SimulateMouseUiOverlayState.Action == MouseAction.Click
                ? CLICK_COLOR
                : DRAG_COLOR;
        }

        private static Image CreateImage(string name, Transform parent)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            Image image = go.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }
    }
}
