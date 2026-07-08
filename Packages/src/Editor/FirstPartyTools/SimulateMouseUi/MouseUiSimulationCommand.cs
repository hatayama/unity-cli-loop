#nullable enable
using System;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Mouse UI Simulation Command behavior for Unity CLI Loop.
    /// </summary>
    internal sealed class MouseUiSimulationCommand
    {
        private MouseUiSimulationCommand(SimulateMouseUiSchema request, MouseAction action)
        {
            Action = action;
            X = request.X;
            Y = request.Y;
            FromX = request.FromX;
            FromY = request.FromY;
            DragSpeed = request.DragSpeed;
            Duration = request.Duration;
            Button = ToRuntimeMouseButton(request.Button);
            BypassRaycast = request.BypassRaycast;
            TargetPath = request.TargetPath ?? "";
            DropTargetPath = request.DropTargetPath ?? "";
        }

        public MouseAction Action { get; }
        public float X { get; }
        public float Y { get; }
        public float FromX { get; }
        public float FromY { get; }
        public float DragSpeed { get; }
        public float Duration { get; }
        public MouseButton Button { get; }
        public bool BypassRaycast { get; }
        public string TargetPath { get; }
        public string DropTargetPath { get; }

        // Returns (command, error). Error is non-null iff the caller supplied an out-of-range enum value.
        public static (MouseUiSimulationCommand? command, string? errorMessage) TryFromSchema(SimulateMouseUiSchema request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            MouseAction? action = TryConvertMouseAction(request.Action);
            if (action == null)
            {
                return (null, $"Unknown mouse UI action: {request.Action}");
            }

            return (new MouseUiSimulationCommand(request, action.Value), null);
        }

        private static MouseAction? TryConvertMouseAction(UnityCliLoopMouseUiAction action)
        {
            switch (action)
            {
                case UnityCliLoopMouseUiAction.Click:
                    return MouseAction.Click;
                case UnityCliLoopMouseUiAction.Drag:
                    return MouseAction.Drag;
                case UnityCliLoopMouseUiAction.DragStart:
                    return MouseAction.DragStart;
                case UnityCliLoopMouseUiAction.DragMove:
                    return MouseAction.DragMove;
                case UnityCliLoopMouseUiAction.DragEnd:
                    return MouseAction.DragEnd;
                case UnityCliLoopMouseUiAction.LongPress:
                    return MouseAction.LongPress;
                default:
                    return null;
            }
        }

        private static MouseButton ToRuntimeMouseButton(UnityCliLoopMouseButton button)
        {
            switch (button)
            {
                case UnityCliLoopMouseButton.Right:
                    return MouseButton.Right;
                case UnityCliLoopMouseButton.Middle:
                    return MouseButton.Middle;
                default:
                    return MouseButton.Left;
            }
        }
    }
}
