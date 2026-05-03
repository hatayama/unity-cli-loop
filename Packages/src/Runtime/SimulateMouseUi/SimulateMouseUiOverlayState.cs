#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    public static class SimulateMouseUiOverlayState
    {
        public static bool IsActive { get; private set; }
        public static MouseAction Action { get; private set; }
        public static Vector2 CurrentPosition { get; private set; }
        public static Vector2? DragStartPosition { get; private set; }
        public static string? HitGameObjectName { get; private set; }

        // Screen.width/height at the time positions were recorded (Editor context may differ from Game context)
        public static Vector2 SourceScreenSize { get; private set; }

        public static float LongPressElapsed { get; private set; }

        // Animation request flags survive Clear() — they are consumed by the overlay in LateUpdate
        private static bool _pendingExpandAnimation;
        private static bool _pendingDissipateAnimation;

        private const int MAX_DRAG_WAYPOINTS = 4;

        private static readonly List<Vector2> _dragWaypoints = new List<Vector2>();

        // Intermediate positions where DragMove stopped, forming a polyline path
        public static IReadOnlyList<Vector2> DragWaypoints => _dragWaypoints;

        public static void Update(
            MouseAction action,
            Vector2 currentPosition,
            Vector2? dragStartPosition,
            string? hitGameObjectName,
            Vector2 sourceScreenSize)
        {
            // PlayDissipateAnimation calls Clear() on normal completion, but a cancelled or stuck drag
            // may leave stale waypoints — defensive clear ensures a fresh start
            if (action == MouseAction.DragStart || action == MouseAction.Drag)
            {
                _dragWaypoints.Clear();
            }

            IsActive = true;
            Action = action;
            CurrentPosition = currentPosition;
            DragStartPosition = dragStartPosition;
            HitGameObjectName = hitGameObjectName;
            SourceScreenSize = sourceScreenSize;
        }

        public static void UpdateLongPressElapsed(float elapsed)
        {
            LongPressElapsed = elapsed;
        }

        public static void UpdatePosition(Vector2 position)
        {
            CurrentPosition = position;
        }

        public static void AddWaypoint(Vector2 position)
        {
            // Keep only the most recent waypoints to bound overlay draw cost during long drags
            if (_dragWaypoints.Count >= MAX_DRAG_WAYPOINTS)
            {
                _dragWaypoints.RemoveAt(0);
            }

            _dragWaypoints.Add(position);
        }

        public static void RequestExpandAnimation()
        {
            _pendingExpandAnimation = true;
        }

        public static void RequestDissipateAnimation()
        {
            _pendingDissipateAnimation = true;
        }

        public static bool ConsumePendingExpandAnimation()
        {
            if (!_pendingExpandAnimation)
            {
                return false;
            }

            _pendingExpandAnimation = false;
            return true;
        }

        public static bool ConsumePendingDissipateAnimation()
        {
            if (!_pendingDissipateAnimation)
            {
                return false;
            }

            _pendingDissipateAnimation = false;
            return true;
        }

        public static void Clear()
        {
            IsActive = false;
            Action = default;
            CurrentPosition = Vector2.zero;
            DragStartPosition = null;
            HitGameObjectName = null;
            SourceScreenSize = Vector2.zero;
            LongPressElapsed = 0f;
            _dragWaypoints.Clear();
        }
    }
}
