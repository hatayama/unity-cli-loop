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
        private const int UNIFORM_COLOR_CAPTURE_ATTEMPTS = 3;
        private const int WINDOW_UNIFORM_RETRY_WAIT_FRAMES = 2;

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
        public static async Task<(Texture2D? texture, bool timedOut)> CaptureWindowAsync(
            EditorWindow window,
            float resolutionScale,
            int frameWaitTimeoutMilliseconds,
            CancellationToken ct)
        {
            if (window == null)
            {
                return (null, false);
            }

            SynchronizationContext editorContext =
                CapturedEditorSynchronizationContext.RequireCurrent("window screenshot capture");
            window.ShowTab();
            bool framesReady = await EditorFrameWaiter.WaitFramesOrTimeoutAsync(
                2,
                frameWaitTimeoutMilliseconds,
                ct).ConfigureAwait(false);
            if (!framesReady)
            {
                return (null, true);
            }

            await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);

            Texture2D? texture = null;
            Color32? uniformColor = null;
            for (int attempt = 1; attempt <= UNIFORM_COLOR_CAPTURE_ATTEMPTS; attempt++)
            {
                texture = CaptureWindowInternal(window, resolutionScale);
                if (texture == null)
                {
                    return (null, false);
                }

                uniformColor = UniformColorDetector.DetectUniformColor(texture.GetPixels32());
                if (!uniformColor.HasValue)
                {
                    return (texture, false);
                }

                if (attempt == UNIFORM_COLOR_CAPTURE_ATTEMPTS)
                {
                    break;
                }

                // Why destroy before retry: keep only the final texture if retries exhaust.
                UnityEngine.Object.DestroyImmediate(texture);
                texture = null;
                window.Repaint();
                bool retryReady = await EditorFrameWaiter.WaitFramesOrTimeoutAsync(
                    WINDOW_UNIFORM_RETRY_WAIT_FRAMES,
                    frameWaitTimeoutMilliseconds,
                    ct).ConfigureAwait(false);
                if (!retryReady)
                {
                    return (null, true);
                }

                await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
            }

            Debug.Assert(texture != null, "Window capture retry loop must leave a texture when exhausted.");
            Debug.Assert(uniformColor.HasValue, "Exhausted window retries must have observed a uniform color.");
            Color32 color = uniformColor.GetValueOrDefault();
            VibeLogger.LogWarning(
                "screenshot_uniform_color_after_retries",
                $"Window capture remained uniform color RGBA({color.r},{color.g},{color.b},{color.a}) after {UNIFORM_COLOR_CAPTURE_ATTEMPTS} attempts.");
            return (texture, false);
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
            // Capture the caller's active target before the capture call, which may
            // reassign RenderTexture.active to the temporary internally; saving afterwards
            // would release a still-active render texture and emit a Console warning.
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(descriptor);
            Texture2D texture;
            try
            {
                InternalEditorUtilityBridge.CaptureEditorWindow(window, rt);

                RenderTexture.active = rt;

                texture = new(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(rt);
            }

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

        // Captures game rendering by reading the Play Mode view RenderTexture (PlayMode only).
        // Works for both GameView and Device Simulator via PlayModeView.m_TargetTexture.
        // Contains all cameras + Screen Space Overlay Canvas, without tab bar or borders.
        internal static async Task<(Texture2D? texture, GameRenderingImageInfo renderingImageInfo, bool timedOut)> CaptureGameRenderingAsync(
            float resolutionScale,
            GameRenderingImageInfo? renderingImageInfo,
            int frameWaitTimeoutMilliseconds,
            CancellationToken ct)
        {
            Debug.Assert(UnityEditor.EditorApplication.isPlaying, "CaptureGameRenderingAsync requires PlayMode");

            SynchronizationContext editorContext =
                CapturedEditorSynchronizationContext.RequireCurrent("rendering screenshot capture");
            // Why render-wait: editor update ticks can advance without redrawing the Play Mode RT.
            PlayModeViewRenderWaitResult renderWaitResult =
                await PlayModeViewRenderWaiter.WaitForRenderedFrameAsync(
                    frameWaitTimeoutMilliseconds,
                    ct).ConfigureAwait(false);
            if (renderWaitResult == PlayModeViewRenderWaitResult.TicksStalled)
            {
                return (null, default, true);
            }

            if (renderWaitResult == PlayModeViewRenderWaitResult.NotRendered)
            {
                VibeLogger.LogWarning(
                    "screenshot_render_wait_not_confirmed",
                    "Timed out waiting for a Game camera render before reading the Play Mode view RenderTexture; continuing capture.");
            }

            await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);

            Texture2D? texture = null;
            Color32? uniformColor = null;
            GameRenderingImageInfo captureInfo = default;
            for (int attempt = 1; attempt <= UNIFORM_COLOR_CAPTURE_ATTEMPTS; attempt++)
            {
                RenderTexture rt = GameViewBridge.GetRenderTexture();
                if (rt == null)
                {
                    Debug.LogWarning("[EditorWindowCaptureUtility] Play Mode view RenderTexture is not available");
                    GameRenderingImageInfo unavailableInfo = renderingImageInfo ??
                        CreateUnavailableGameRenderingImageInfo(Handles.GetMainGameViewSize());
                    return (null, unavailableInfo, false);
                }

                // Play Mode view RenderTexture can be shorter than the full input area, so raw image Y needs this offset.
                captureInfo = renderingImageInfo ??
                    CreateGameRenderingImageInfo(Handles.GetMainGameViewSize(), rt.width, rt.height);

                texture = ReadPlayModeViewTexture(rt, resolutionScale);
                uniformColor = UniformColorDetector.DetectUniformColor(texture.GetPixels32());
                if (!uniformColor.HasValue)
                {
                    return (texture, captureInfo, false);
                }

                if (attempt == UNIFORM_COLOR_CAPTURE_ATTEMPTS)
                {
                    break;
                }

                // Why destroy before retry: keep only the final texture if retries exhaust.
                UnityEngine.Object.DestroyImmediate(texture);
                texture = null;

                PlayModeViewRenderWaitResult retryWaitResult =
                    await PlayModeViewRenderWaiter.WaitForRenderedFrameAsync(
                        frameWaitTimeoutMilliseconds,
                        ct).ConfigureAwait(false);
                if (retryWaitResult == PlayModeViewRenderWaitResult.TicksStalled)
                {
                    return (null, default, true);
                }

                if (retryWaitResult == PlayModeViewRenderWaitResult.NotRendered)
                {
                    VibeLogger.LogWarning(
                        "screenshot_render_wait_not_confirmed",
                        "Timed out waiting for a Game camera render before uniform-color capture retry; continuing capture.");
                }

                await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
            }

            Debug.Assert(texture != null, "Rendering capture retry loop must leave a texture when exhausted.");
            Debug.Assert(uniformColor.HasValue, "Exhausted rendering retries must have observed a uniform color.");
            Color32 color = uniformColor.GetValueOrDefault();
            VibeLogger.LogWarning(
                "screenshot_uniform_color_after_retries",
                $"Rendering capture remained uniform color RGBA({color.r},{color.g},{color.b},{color.a}) after {UNIFORM_COLOR_CAPTURE_ATTEMPTS} attempts.");
            return (texture, captureInfo, false);
        }

        // Reads and vertically flips the Play Mode view RT into a Texture2D (top-left origin).
        private static Texture2D ReadPlayModeViewTexture(RenderTexture rt, float resolutionScale)
        {
            RenderTextureDescriptor flipDescriptor = new(rt.width, rt.height, rt.format, 0);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                flipDescriptor.sRGB = false;
            }

            // Capture the caller's active target before Blit: Blit leaves the destination
            // assigned to RenderTexture.active, so saving afterwards would "restore" the
            // temporary itself and releasing it would warn about an active render texture.
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture flipped = RenderTexture.GetTemporary(flipDescriptor);
            Texture2D texture;
            try
            {
                Graphics.Blit(rt, flipped, new Vector2(1f, -1f), new Vector2(0f, 1f));

                RenderTexture.active = flipped;

                texture = new(rt.width, rt.height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                texture.Apply();
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(flipped);
            }

            if (!Mathf.Approximately(resolutionScale, 1.0f))
            {
                texture = ApplyResolutionScaling(texture, resolutionScale);
            }

            return texture;
        }

        // Raycast-grid annotations must use the same settled RenderTexture geometry as the PNG capture path.
        internal static async Task<(GameRenderingImageInfo renderingImageInfo, bool timedOut)> GetGameRenderingImageInfoAsync(
            int frameWaitTimeoutMilliseconds,
            CancellationToken ct)
        {
            Debug.Assert(UnityEditor.EditorApplication.isPlaying, "GetGameRenderingImageInfoAsync requires PlayMode");

            SynchronizationContext editorContext =
                CapturedEditorSynchronizationContext.RequireCurrent("raycast grid rendering info capture");
            bool framesReady = await EditorFrameWaiter.WaitFramesOrTimeoutAsync(
                2,
                frameWaitTimeoutMilliseconds,
                ct).ConfigureAwait(false);
            if (!framesReady)
            {
                return (default, true);
            }

            await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);

            Vector2 gameViewSize = Handles.GetMainGameViewSize();
            RenderTexture rt = GameViewBridge.GetRenderTexture();
            if (rt == null)
            {
                return (CreateUnavailableGameRenderingImageInfo(gameViewSize), false);
            }

            return (CreateGameRenderingImageInfo(gameViewSize, rt.width, rt.height), false);
        }

        internal static GameRenderingImageInfo CreateUnavailableGameRenderingImageInfo(Vector2 gameViewSize)
        {
            return new GameRenderingImageInfo(gameViewSize, gameViewSize, 0);
        }

        private static GameRenderingImageInfo CreateGameRenderingImageInfo(
            Vector2 gameViewSize,
            int renderTextureWidth,
            int renderTextureHeight)
        {
            Vector2 renderingImageSize = new(renderTextureWidth, renderTextureHeight);
            int imageToInputOffsetY = CalculateImageToInputOffsetY(gameViewSize, renderTextureHeight);
            return new GameRenderingImageInfo(gameViewSize, renderingImageSize, imageToInputOffsetY);
        }

        /// <summary>
        /// Calculates the Y offset from rendering screenshot image space to Game View input space.
        /// </summary>
        internal static int CalculateImageToInputOffsetY(Vector2 gameViewSize, int renderTextureHeight)
        {
            Debug.Assert(gameViewSize.y >= 0f, "Game View height must not be negative.");
            Debug.Assert(renderTextureHeight >= 0, "RenderTexture height must not be negative.");

            return Mathf.RoundToInt(gameViewSize.y) - renderTextureHeight;
        }

        private static Texture2D ApplyResolutionScaling(Texture2D originalTexture, float scale)
        {
            int newWidth = Mathf.RoundToInt(originalTexture.width * scale);
            int newHeight = Mathf.RoundToInt(originalTexture.height * scale);

            Texture2D scaledTexture = new(newWidth, newHeight, originalTexture.format, false);

            // Same active-target discipline as the capture paths: save before Blit,
            // restore before release, so the caller's target survives and the temporary
            // is never released while active.
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight);
            try
            {
                Graphics.Blit(originalTexture, rt);

                RenderTexture.active = rt;
                scaledTexture.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
                scaledTexture.Apply();
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(rt);
            }
            UnityEngine.Object.DestroyImmediate(originalTexture);

            return scaledTexture;
        }
    }

    /// <summary>
    /// Switches screenshot continuations back to the Editor synchronization context captured before a timeout race.
    /// </summary>
    internal static class CapturedEditorSynchronizationContext
    {
        public static SynchronizationContext RequireCurrent(string operationName)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(operationName), "operationName must not be null or whitespace");

            SynchronizationContext context = SynchronizationContext.Current;
            Debug.Assert(context != null, $"{operationName} must start on Unity's editor synchronization context.");
            if (context == null)
            {
                throw new InvalidOperationException(
                    $"{operationName} must start on Unity's editor synchronization context.");
            }

            return context;
        }

        public static SwitchToMainThreadAwaitable SwitchTo(
            SynchronizationContext context,
            CancellationToken ct)
        {
            Debug.Assert(context != null, "context must not be null");

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return MainThreadSwitcher.SwitchToMainThread(ct);
        }
    }

    /// <summary>
    /// Describes the Game View geometry used to map rendering screenshots back to input coordinates.
    /// </summary>
    internal readonly struct GameRenderingImageInfo
    {
        public readonly Vector2 GameViewSize;
        public readonly Vector2 RenderingImageSize;
        public readonly int ImageToInputOffsetY;

        public GameRenderingImageInfo(
            Vector2 gameViewSize,
            Vector2 renderingImageSize,
            int imageToInputOffsetY)
        {
            GameViewSize = gameViewSize;
            RenderingImageSize = renderingImageSize;
            ImageToInputOffsetY = imageToInputOffsetY;
        }
    }
}
