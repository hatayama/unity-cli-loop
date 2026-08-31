using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace io.github.hatayama.UnityCliLoop.InternalAPIBridge
{
    /// <summary>
    /// Bridge class for accessing Unity internal APIs.
    /// Uses Unity.InternalAPIEditorBridge assembly name to access internal members via InternalsVisibleTo.
    /// </summary>
    public static class InternalEditorUtilityBridge
    {
        private static Material _blitSceneViewCaptureMat;

        /// <summary>
        /// Capture an EditorWindow to a RenderTexture using Unity internal GrabPixels method.
        /// Considers pixelsPerPoint for Retina/HiDPI displays.
        /// </summary>
        /// <param name="window">The EditorWindow to capture</param>
        /// <param name="rt">The RenderTexture to capture into</param>
        public static void CaptureEditorWindow(EditorWindow window, RenderTexture rt)
        {
            if (window == null || rt == null)
            {
                return;
            }

            if (_blitSceneViewCaptureMat == null)
            {
                FieldInfo blitMatField = typeof(InternalEditorUtility).GetField(
                    "blitSceneViewCaptureMat",
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (blitMatField != null)
                {
                    _blitSceneViewCaptureMat = blitMatField.GetValue(null) as Material;
                }

                if (_blitSceneViewCaptureMat == null)
                {
                    _blitSceneViewCaptureMat = EditorGUIUtility.LoadRequired("SceneView/BlitSceneViewCapture.mat") as Material;

                    if (blitMatField != null)
                    {
                        blitMatField.SetValue(null, _blitSceneViewCaptureMat);
                    }
                }
            }

            // When in Linear color space, use a RenderTexture with sRGB write disabled
            // to prevent double gamma correction from BlitSceneViewCapture.mat
            RenderTextureDescriptor descriptor = rt.descriptor;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                descriptor.sRGB = false;
            }
            RenderTexture temporary = RenderTexture.GetTemporary(descriptor);

            float scale = EditorGUIUtility.pixelsPerPoint;
            Rect rect = new(0.0f, 0.0f, window.position.width * scale, window.position.height * scale);

            // m_Parent is the HostView/DockArea that contains the EditorWindow
            // GrabPixels is an internal method that captures the view contents
            window.m_Parent.GrabPixels(temporary, rect);

            Graphics.Blit(temporary, rt, _blitSceneViewCaptureMat);
            RenderTexture.ReleaseTemporary(temporary);
        }
    }
}

