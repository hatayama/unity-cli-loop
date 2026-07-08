#nullable enable
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Converts between top-left mouse input coordinates and Unity screen coordinates.
    /// </summary>
    internal static class MouseUiCoordinateConverter
    {
        // Input coordinates use top-left origin; Unity Screen space uses bottom-left origin.
        // Handles.GetMainGameViewSize() returns the Game view's target resolution (e.g. 1920x1080),
        // which matches the Canvas layout space — unlike Screen.height which returns the window pixel size.
        internal static Vector2 InputToScreen(Vector2 inputPos)
        {
            float targetHeight = Handles.GetMainGameViewSize().y;
            return new Vector2(inputPos.x, targetHeight - inputPos.y);
        }

        internal static Vector2 ScreenToInput(Vector2 screenPos)
        {
            float targetHeight = Handles.GetMainGameViewSize().y;
            return new Vector2(screenPos.x, targetHeight - screenPos.y);
        }
    }
}
