using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Converts public top-left Game View coordinates into Unity's bottom-left input coordinates.
    /// </summary>
    internal static class GameViewCoordinateUtility
    {
        internal static GameViewCoordinateConversion ConvertInputToUnity(
            Vector2 inputPosition,
            Vector2 gameViewSize)
        {
            Debug.Assert(gameViewSize.x >= 0f, "Game View width must not be negative.");
            Debug.Assert(gameViewSize.y >= 0f, "Game View height must not be negative.");

            Vector2 injectedUnityPosition = new Vector2(
                inputPosition.x,
                gameViewSize.y - inputPosition.y);

            return new GameViewCoordinateConversion(
                inputPosition,
                injectedUnityPosition,
                gameViewSize);
        }

        internal static Vector2 GetMainGameViewSize()
        {
            return Handles.GetMainGameViewSize();
        }
    }

    /// <summary>
    /// Describes one conversion from screenshot-compatible Game View input to Unity input space.
    /// </summary>
    internal readonly struct GameViewCoordinateConversion
    {
        public readonly Vector2 InputPosition;
        public readonly Vector2 InjectedUnityPosition;
        public readonly Vector2 GameViewSize;

        public GameViewCoordinateConversion(
            Vector2 inputPosition,
            Vector2 injectedUnityPosition,
            Vector2 gameViewSize)
        {
            InputPosition = inputPosition;
            InjectedUnityPosition = injectedUnityPosition;
            GameViewSize = gameViewSize;
        }
    }
}
