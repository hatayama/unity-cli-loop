#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines the Unity CLI Loop Keyboard Simulation operations required by the owning workflow.
    /// </summary>
    public interface IUnityCliLoopKeyboardSimulationService
    {
        Task<UnityCliLoopKeyboardSimulationResult> SimulateKeyboardAsync(
            UnityCliLoopKeyboardSimulationRequest request,
            CancellationToken ct);
    }

    /// <summary>
    /// Defines the Unity CLI Loop Mouse Input Simulation operations required by the owning workflow.
    /// </summary>
    public interface IUnityCliLoopMouseInputSimulationService
    {
        Task<UnityCliLoopMouseInputSimulationResult> SimulateMouseInputAsync(
            UnityCliLoopMouseInputSimulationRequest request,
            CancellationToken ct);
    }

    /// <summary>
    /// Defines the Unity CLI Loop Mouse UI Simulation operations required by the owning workflow.
    /// </summary>
    public interface IUnityCliLoopMouseUiSimulationService
    {
        Task<UnityCliLoopMouseUiSimulationResult> SimulateMouseUiAsync(
            UnityCliLoopMouseUiSimulationRequest request,
            CancellationToken ct);
    }

    public enum UnityCliLoopKeyboardAction
    {
        Press = 0,
        KeyDown = 1,
        KeyUp = 2
    }

    public enum UnityCliLoopMouseInputAction
    {
        Click = 0,
        LongPress = 1,
        MoveDelta = 2,
        Scroll = 3,
        SmoothDelta = 4
    }

    public enum UnityCliLoopMouseUiAction
    {
        Click = 0,
        Drag = 1,
        DragStart = 2,
        DragMove = 3,
        DragEnd = 4,
        LongPress = 5
    }

    public enum UnityCliLoopMouseButton
    {
        Left = 0,
        Right = 1,
        Middle = 2
    }

    /// <summary>
    /// Provides Unity CLI Loop Input Simulation Defaults behavior for Unity CLI Loop.
    /// </summary>
    public static class UnityCliLoopInputSimulationDefaults
    {
        public const float MouseUiDragSpeed = 2000f;
        public const float MouseUiDuration = 0.5f;
    }

    /// <summary>
    /// Carries the request data needed for Unity CLI Loop Keyboard Simulation behavior.
    /// </summary>
    public sealed class UnityCliLoopKeyboardSimulationRequest
    {
        public UnityCliLoopKeyboardAction Action { get; set; } = UnityCliLoopKeyboardAction.Press;
        public string Key { get; set; } = "";
        public float Duration { get; set; }
    }

    /// <summary>
    /// Carries the result data produced by Unity CLI Loop Keyboard Simulation behavior.
    /// </summary>
    public sealed class UnityCliLoopKeyboardSimulationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? KeyName { get; set; }
    }

    /// <summary>
    /// Carries the request data needed for Unity CLI Loop Mouse Input Simulation behavior.
    /// </summary>
    public sealed class UnityCliLoopMouseInputSimulationRequest
    {
        public UnityCliLoopMouseInputAction Action { get; set; } = UnityCliLoopMouseInputAction.Click;
        public float X { get; set; }
        public float Y { get; set; }
        public UnityCliLoopMouseButton Button { get; set; } = UnityCliLoopMouseButton.Left;
        public float Duration { get; set; }
        public float DeltaX { get; set; }
        public float DeltaY { get; set; }
        public float ScrollX { get; set; }
        public float ScrollY { get; set; }
    }

    /// <summary>
    /// Carries the result data produced by Unity CLI Loop Mouse Input Simulation behavior.
    /// </summary>
    public sealed class UnityCliLoopMouseInputSimulationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? Button { get; set; }
        public float? PositionX { get; set; }
        public float? PositionY { get; set; }
    }

    /// <summary>
    /// Carries the request data needed for Unity CLI Loop Mouse UI Simulation behavior.
    /// </summary>
    public sealed class UnityCliLoopMouseUiSimulationRequest
    {
        public UnityCliLoopMouseUiAction Action { get; set; } = UnityCliLoopMouseUiAction.Click;
        public float X { get; set; }
        public float Y { get; set; }
        public float FromX { get; set; }
        public float FromY { get; set; }
        public float DragSpeed { get; set; } = UnityCliLoopInputSimulationDefaults.MouseUiDragSpeed;
        public float Duration { get; set; } = UnityCliLoopInputSimulationDefaults.MouseUiDuration;
        public UnityCliLoopMouseButton Button { get; set; } = UnityCliLoopMouseButton.Left;
        public bool BypassRaycast { get; set; }
        public string TargetPath { get; set; } = "";
        public string DropTargetPath { get; set; } = "";
    }

    /// <summary>
    /// Carries the result data produced by Unity CLI Loop Mouse UI Simulation behavior.
    /// </summary>
    public sealed class UnityCliLoopMouseUiSimulationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? HitGameObjectName { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float? EndPositionX { get; set; }
        public float? EndPositionY { get; set; }
    }
}
