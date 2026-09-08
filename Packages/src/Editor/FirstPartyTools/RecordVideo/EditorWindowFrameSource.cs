using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads an EditorWindow's last painted pixels into a reusable RGBA32 destination and requests the next repaint.
    /// </summary>
    internal sealed class EditorWindowFrameSource : IGameViewFrameSource
    {
        private readonly EditorWindow _window;
        private readonly float _resolutionScale;

        internal EditorWindowFrameSource(EditorWindow window, float resolutionScale)
        {
            _window = window;
            _resolutionScale = resolutionScale;
        }

        // Unity's null comparison also reports a destroyed window, which is how a closed tab is detected.
        public bool IsSourceClosed => _window == null;

        public bool TryReadFrame(Texture2D destination)
        {
            if (_window == null)
            {
                return false;
            }

            float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
            int sourceWidth = Mathf.RoundToInt(_window.position.width * pixelsPerPoint);
            int sourceHeight = Mathf.RoundToInt(_window.position.height * pixelsPerPoint);
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                return false;
            }

            // A resized window no longer matches the fixed encoder size, so that tick is skipped
            // the same way the Play Mode view source handles a resized Game View.
            if (!VideoFrameSizePolicy.MatchesEncoderSize(
                    sourceWidth,
                    sourceHeight,
                    _resolutionScale,
                    destination.width,
                    destination.height))
            {
                return false;
            }

            ReadWindowPixels(destination, sourceWidth, sourceHeight);

            // Repaint after reading: it only queues a redraw, so this tick reads the previous
            // repaint's result and the next tick reads the one requested here.
            _window.Repaint();
            return true;
        }

        private void ReadWindowPixels(Texture2D destination, int sourceWidth, int sourceHeight)
        {
            RenderTextureDescriptor fullDescriptor = new RenderTextureDescriptor(
                sourceWidth,
                sourceHeight,
                RenderTextureFormat.ARGB32,
                24);
            // Why sRGB: in Linear color space the window grab must not be gamma-converted twice.
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                fullDescriptor.sRGB = false;
            }

            // Captured before the grab call, which reassigns RenderTexture.active internally;
            // saving afterwards would release a still-active render texture.
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture full = RenderTexture.GetTemporary(fullDescriptor);
            RenderTexture scaled = null;
            try
            {
                InternalEditorUtilityBridge.CaptureEditorWindow(_window, full);

                RenderTexture readSource = full;
                // ReadPixels cannot read beyond the active target, so a scaled recording needs
                // the full grab downscaled into a destination-sized target first.
                if (destination.width != sourceWidth || destination.height != sourceHeight)
                {
                    RenderTextureDescriptor scaledDescriptor = fullDescriptor;
                    scaledDescriptor.width = destination.width;
                    scaledDescriptor.height = destination.height;
                    scaled = RenderTexture.GetTemporary(scaledDescriptor);
                    Graphics.Blit(full, scaled);
                    readSource = scaled;
                }

                RenderTexture.active = readSource;
                destination.ReadPixels(new Rect(0, 0, destination.width, destination.height), 0, 0);
                destination.Apply();
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (scaled != null)
                {
                    RenderTexture.ReleaseTemporary(scaled);
                }

                RenderTexture.ReleaseTemporary(full);
            }
        }
    }
}
