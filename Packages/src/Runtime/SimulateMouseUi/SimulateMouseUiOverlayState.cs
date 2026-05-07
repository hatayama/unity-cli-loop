#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Provides Simulate Mouse UI Overlay State operations for its owning module.
    /// </summary>
    public sealed class SimulateMouseUiOverlayStateService
    {
        public bool IsActive { get; private set; }
        public MouseAction Action { get; private set; }
        public Vector2 CurrentPosition { get; private set; }
        public Vector2? DragStartPosition { get; private set; }
        public string? HitGameObjectName { get; private set; }

        // Screen.width/height at the time positions were recorded (Editor context may differ from Game context)
        public Vector2 SourceScreenSize { get; private set; }

        public float LongPressElapsed { get; private set; }

        // Animation request flags survive Clear() — they are consumed by the overlay in LateUpdate
        private bool _pendingExpandAnimation;
        private bool _pendingDissipateAnimation;

        private const int MAX_DRAG_WAYPOINTS = 4;

        private readonly List<Vector2> _dragWaypoints = new List<Vector2>();

        // Intermediate positions where DragMove stopped, forming a polyline path
        public IReadOnlyList<Vector2> DragWaypoints => _dragWaypoints;

        public void Update(
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

        public void UpdateLongPressElapsed(float elapsed)
        {
            LongPressElapsed = elapsed;
        }

        public void UpdatePosition(Vector2 position)
        {
            CurrentPosition = position;
        }

        public void AddWaypoint(Vector2 position)
        {
            // Keep only the most recent waypoints to bound overlay draw cost during long drags
            if (_dragWaypoints.Count >= MAX_DRAG_WAYPOINTS)
            {
                _dragWaypoints.RemoveAt(0);
            }

            _dragWaypoints.Add(position);
        }

        public void RequestExpandAnimation()
        {
            _pendingExpandAnimation = true;
        }

        public void RequestDissipateAnimation()
        {
            _pendingDissipateAnimation = true;
        }

        public bool ConsumePendingExpandAnimation()
        {
            if (!_pendingExpandAnimation)
            {
                return false;
            }

            _pendingExpandAnimation = false;
            return true;
        }

        public bool ConsumePendingDissipateAnimation()
        {
            if (!_pendingDissipateAnimation)
            {
                return false;
            }

            _pendingDissipateAnimation = false;
            return true;
        }

        public void Clear()
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

    /// <summary>
    /// Stores Simulate Mouse UI Overlay state shared by the owning workflow.
    /// </summary>
    public static class SimulateMouseUiOverlayState
    {
        private static readonly SimulateMouseUiOverlayStateService ServiceValue =
            new SimulateMouseUiOverlayStateService();

        public static bool IsActive => ServiceValue.IsActive;
        public static MouseAction Action => ServiceValue.Action;
        public static Vector2 CurrentPosition => ServiceValue.CurrentPosition;
        public static Vector2? DragStartPosition => ServiceValue.DragStartPosition;
        public static string? HitGameObjectName => ServiceValue.HitGameObjectName;
        public static Vector2 SourceScreenSize => ServiceValue.SourceScreenSize;
        public static float LongPressElapsed => ServiceValue.LongPressElapsed;
        public static IReadOnlyList<Vector2> DragWaypoints => ServiceValue.DragWaypoints;

        public static void Update(
            MouseAction action,
            Vector2 currentPosition,
            Vector2? dragStartPosition,
            string? hitGameObjectName,
            Vector2 sourceScreenSize)
        {
            ServiceValue.Update(action, currentPosition, dragStartPosition, hitGameObjectName, sourceScreenSize);
        }

        public static void UpdateLongPressElapsed(float elapsed)
        {
            ServiceValue.UpdateLongPressElapsed(elapsed);
        }

        public static void UpdatePosition(Vector2 position)
        {
            ServiceValue.UpdatePosition(position);
        }

        public static void AddWaypoint(Vector2 position)
        {
            ServiceValue.AddWaypoint(position);
        }

        public static void RequestExpandAnimation()
        {
            ServiceValue.RequestExpandAnimation();
        }

        public static void RequestDissipateAnimation()
        {
            ServiceValue.RequestDissipateAnimation();
        }

        public static bool ConsumePendingExpandAnimation()
        {
            return ServiceValue.ConsumePendingExpandAnimation();
        }

        public static bool ConsumePendingDissipateAnimation()
        {
            return ServiceValue.ConsumePendingDissipateAnimation();
        }

        public static void Clear()
        {
            ServiceValue.Clear();
        }
    }
}
