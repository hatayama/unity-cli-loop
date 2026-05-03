using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Bridge class for accessing Unity GameView internal APIs via reflection.
    /// GameView is an internal class; this bridge discovers members dynamically.
    /// </summary>
    public static class GameViewBridge
    {
        private static Type _gameViewType;
        private static FieldInfo _renderTextureField;
        private static bool _memberSearchDone;

        /// <summary>
        /// Get the GameView's composited RenderTexture containing all cameras + Screen Space Overlay Canvas.
        /// </summary>
        /// <returns>The RenderTexture, or null if GameView not found or field not accessible</returns>
        public static RenderTexture GetRenderTexture()
        {
            EnsureMembersResolved();

            EditorWindow gameView = FindMainGameView();
            if (gameView == null || _renderTextureField == null)
            {
                return null;
            }

            return _renderTextureField.GetValue(gameView) as RenderTexture;
        }

        private static EditorWindow FindMainGameView()
        {
            if (_gameViewType == null)
            {
                return null;
            }

            UnityEngine.Object[] gameViews = Resources.FindObjectsOfTypeAll(_gameViewType);
            if (gameViews.Length == 0)
            {
                return null;
            }

            // Prefer focused window to match editor's active view context
            foreach (UnityEngine.Object gv in gameViews)
            {
                EditorWindow window = gv as EditorWindow;
                if (window != null && window.hasFocus)
                {
                    return window;
                }
            }

            return gameViews[0] as EditorWindow;
        }

        private static void EnsureMembersResolved()
        {
            if (_memberSearchDone)
            {
                return;
            }
            _memberSearchDone = true;

            _gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            if (_gameViewType == null)
            {
                Debug.LogWarning("[GameViewBridge] GameView type not found");
                return;
            }

            _renderTextureField = _gameViewType.GetField("m_RenderTexture",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (_renderTextureField == null)
            {
                Debug.LogWarning("[GameViewBridge] m_RenderTexture field not found");
            }
        }
    }
}
