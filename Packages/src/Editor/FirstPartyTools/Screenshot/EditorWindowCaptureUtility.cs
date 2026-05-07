#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Utility class for capturing any EditorWindow to a Texture2D.
    /// Uses InternalEditorUtilityBridge to access Unity internal GrabPixels method.
    /// </summary>
    public static class EditorWindowCaptureUtility
    {
        /// <summary>
        /// Find all EditorWindows matching the given name (title bar text).
        /// </summary>
        /// <param name="windowName">Window name displayed in the title bar (e.g., "Console", "Inspector")</param>
        /// <param name="matchMode">Matching mode: exact, prefix, or contains (all case-insensitive)</param>
        /// <returns>Array of matching EditorWindows (empty if none found)</returns>
        public static EditorWindow[] FindWindowsByName(string windowName, WindowMatchMode matchMode = WindowMatchMode.exact)
        {
            if (string.IsNullOrEmpty(windowName))
            {
                return Array.Empty<EditorWindow>();
            }

            List<EditorWindow> matchingWindows = new();
            EditorWindow[] allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (EditorWindow window in allWindows)
            {
                if (window.titleContent == null)
                {
                    continue;
                }

                string title = window.titleContent.text;
                bool isMatch = matchMode switch
                {
                    WindowMatchMode.exact => title.Equals(windowName, StringComparison.OrdinalIgnoreCase),
                    WindowMatchMode.prefix => title.StartsWith(windowName, StringComparison.OrdinalIgnoreCase),
                    WindowMatchMode.contains => title.Contains(windowName, StringComparison.OrdinalIgnoreCase),
                    _ => title.Equals(windowName, StringComparison.OrdinalIgnoreCase)
                };

                if (isMatch)
                {
                    matchingWindows.Add(window);
                }
            }

            return matchingWindows.ToArray();
        }

        /// <summary>
        /// Capture an EditorWindow to a Texture2D asynchronously.
        /// Waits for 2 frames after showing the window to ensure it is fully rendered.
        /// </summary>
        /// <param name="window">The EditorWindow to capture</param>
        /// <param name="resolutionScale">Resolution scale (0.1 to 1.0)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Captured Texture2D, or null if capture failed</returns>
        public static async Task<Texture2D?> CaptureWindowAsync(EditorWindow window, float resolutionScale, CancellationToken ct)
        {
            if (window == null)
            {
                return null;
            }

            window.ShowTab();
            await EditorDelay.DelayFrame(2, ct);
            return CaptureWindowInternal(window, resolutionScale);
        }

        /// <summary>
        /// Internal capture logic shared by sync and async methods.
        /// </summary>
        private static Texture2D? CaptureWindowInternal(EditorWindow window, float resolutionScale)
        {
            float scale = EditorGUIUtility.pixelsPerPoint;
            int width = Mathf.RoundToInt(window.position.width * scale);
            int height = Mathf.RoundToInt(window.position.height * scale);

            if (width <= 0 || height <= 0)
            {
                return null;
            }

            // For Linear color space, disable the sRGB flag to prevent double gamma conversion
            RenderTextureDescriptor descriptor = new(width, height, RenderTextureFormat.ARGB32, 24);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                descriptor.sRGB = false;
            }
            RenderTexture rt = RenderTexture.GetTemporary(descriptor);

            InternalEditorUtilityBridge.CaptureEditorWindow(window, rt);

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D texture = new(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();

            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);

            if (!Mathf.Approximately(resolutionScale, 1.0f))
            {
                texture = ApplyResolutionScaling(texture, resolutionScale);
            }

            return texture;
        }

        /// <summary>
        /// Get a list of all open EditorWindow names.
        /// </summary>
        /// <returns>Array of window names</returns>
        public static string[] GetOpenWindowNames()
        {
            EditorWindow[] allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            List<string> names = new();

            foreach (EditorWindow window in allWindows)
            {
                if (window.titleContent != null && !string.IsNullOrEmpty(window.titleContent.text))
                {
                    names.Add(window.titleContent.text);
                }
            }

            return names.ToArray();
        }

        // Captures game rendering by reading GameView's composited RenderTexture (PlayMode only).
        // Contains all cameras + Screen Space Overlay Canvas, without tab bar or borders.
        // Coordinate mapping to simulate-mouse:
        //   sim_x = image_x / resolutionScale, sim_y = image_y / resolutionScale + YOffset
        // where YOffset = Screen.height - RenderTexture.height (returned in the tuple).
        public static async Task<(Texture2D? texture, int yOffset)> CaptureGameRenderingAsync(float resolutionScale, CancellationToken ct)
        {
            Debug.Assert(UnityEditor.EditorApplication.isPlaying, "CaptureGameRenderingAsync requires PlayMode");

            // Wait for the game camera to complete at least one full render cycle after any state change
            await EditorDelay.DelayFrame(2, ct);

            RenderTexture rt = GameViewBridge.GetRenderTexture();
            if (rt == null)
            {
                Debug.LogWarning("[EditorWindowCaptureUtility] GameView RenderTexture is not available");
                return (null, 0);
            }

            int yOffset = (int)Handles.GetMainGameViewSize().y - rt.height;

            // RenderTexture uses bottom-left origin; flip vertically for standard top-left image format
            RenderTextureDescriptor flipDescriptor = new(rt.width, rt.height, rt.format, 0);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                flipDescriptor.sRGB = false;
            }
            RenderTexture flipped = RenderTexture.GetTemporary(flipDescriptor);
            Graphics.Blit(rt, flipped, new Vector2(1f, -1f), new Vector2(0f, 1f));

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = flipped;

            Texture2D texture = new(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();

            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(flipped);

            if (!Mathf.Approximately(resolutionScale, 1.0f))
            {
                texture = ApplyResolutionScaling(texture, resolutionScale);
            }

            return (texture, yOffset);
        }

        private static Texture2D ApplyResolutionScaling(Texture2D originalTexture, float scale)
        {
            int newWidth = Mathf.RoundToInt(originalTexture.width * scale);
            int newHeight = Mathf.RoundToInt(originalTexture.height * scale);

            Texture2D scaledTexture = new(newWidth, newHeight, originalTexture.format, false);

            RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight);
            Graphics.Blit(originalTexture, rt);

            RenderTexture.active = rt;
            scaledTexture.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
            scaledTexture.Apply();
            RenderTexture.active = null;

            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(originalTexture);

            return scaledTexture;
        }
    }
}

