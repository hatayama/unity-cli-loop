#nullable enable
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Writes structured activity logs for mouse UI simulations.
    /// </summary>
    internal static class MouseUiSimulationActivityLogger
    {
        internal static void LogSimulationStart(MouseUiSimulationCommand parameters, string correlationId)
        {
            VibeLogger.LogInfo(
                "simulate_mouse_start",
                "Mouse simulation started",
                new
                {
                    Action = parameters.Action.ToString(),
                    X = parameters.X,
                    Y = parameters.Y,
                    BypassRaycast = parameters.BypassRaycast,
                    TargetPath = parameters.TargetPath,
                    DropTargetPath = parameters.DropTargetPath
                },
                correlationId: correlationId
            );
        }

        internal static void LogSimulationComplete(
            MouseUiSimulationCommand parameters,
            SimulateMouseUiResponse response,
            string correlationId)
        {
            VibeLogger.LogInfo(
                "simulate_mouse_complete",
                $"Mouse simulation completed: {response.Message}",
                new { Action = parameters.Action.ToString(), Success = response.Success },
                correlationId: correlationId
            );
        }
    }
}
