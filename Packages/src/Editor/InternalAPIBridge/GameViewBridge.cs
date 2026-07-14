using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.InternalAPIBridge
{
    /// <summary>
    /// Bridge for the active Play Mode view RenderTexture via reflection.
    /// Uses PlayModeView so both GameView and Device Simulator windows work.
    /// </summary>
    public static class GameViewBridge
    {
        private const string PlayModeViewTypeName = "UnityEditor.PlayModeView";
        private const string GetMainPlayModeViewMethodName = "GetMainPlayModeView";
        private const string TargetTextureFieldName = "m_TargetTexture";

        private static Type _playModeViewType;
        private static MethodInfo _getMainPlayModeViewMethod;
        private static FieldInfo _targetTextureField;
        private static bool _memberSearchDone;

        /// <summary>
        /// Get the active Play Mode view's composited RenderTexture
        /// (cameras + Screen Space Overlay Canvas).
        /// </summary>
        /// <returns>The RenderTexture, or null if the view or field is unavailable</returns>
        public static RenderTexture GetRenderTexture()
        {
            EnsureMembersResolved();

            EditorWindow playModeView = FindMainPlayModeView();
            if (playModeView == null || _targetTextureField == null)
            {
                return null;
            }

            return _targetTextureField.GetValue(playModeView) as RenderTexture;
        }

        /// <summary>
        /// Resolve m_TargetTexture on the PlayModeView declaring type.
        /// Must not use a derived Type: GetField does not return private fields declared on base types.
        /// </summary>
        internal static FieldInfo ResolveTargetTextureField(Type playModeViewType)
        {
            Debug.Assert(playModeViewType != null, "playModeViewType must not be null");

            return playModeViewType.GetField(
                TargetTextureFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static EditorWindow FindMainPlayModeView()
        {
            if (_getMainPlayModeViewMethod == null)
            {
                return null;
            }

            return _getMainPlayModeViewMethod.Invoke(null, null) as EditorWindow;
        }

        private static void EnsureMembersResolved()
        {
            if (_memberSearchDone)
            {
                return;
            }
            _memberSearchDone = true;

            _playModeViewType = typeof(Editor).Assembly.GetType(PlayModeViewTypeName);
            if (_playModeViewType == null)
            {
                Debug.LogWarning("[GameViewBridge] PlayModeView type not found");
                return;
            }

            _getMainPlayModeViewMethod = _playModeViewType.GetMethod(
                GetMainPlayModeViewMethodName,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (_getMainPlayModeViewMethod == null)
            {
                Debug.LogWarning("[GameViewBridge] GetMainPlayModeView method not found");
                return;
            }

            // why: private base fields are invisible to GetField on derived types (GameView / SimulatorWindow)
            _targetTextureField = ResolveTargetTextureField(_playModeViewType);
            if (_targetTextureField == null)
            {
                Debug.LogWarning("[GameViewBridge] m_TargetTexture field not found on PlayModeView");
            }
        }
    }
}
