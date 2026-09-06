using UnityEngine;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads the Play Mode view RenderTexture into a reusable RGBA32 destination.
    /// </summary>
    internal sealed class PlayModeViewFrameSource : IGameViewFrameSource
    {
        private readonly float _resolutionScale;

        internal PlayModeViewFrameSource(float resolutionScale)
        {
            _resolutionScale = resolutionScale;
        }

        public bool TryReadFrame(Texture2D destination)
        {
            RenderTexture renderTexture = GameViewBridge.GetRenderTexture();
            if (renderTexture == null)
            {
                return false;
            }

            if (!VideoFrameSizePolicy.MatchesEncoderSize(
                    renderTexture.width,
                    renderTexture.height,
                    _resolutionScale,
                    destination.width,
                    destination.height))
            {
                return false;
            }

            ReadPlayModeViewTexture(renderTexture, destination);
            return true;
        }

        private static void ReadPlayModeViewTexture(RenderTexture renderTexture, Texture2D destination)
        {
            RenderTextureDescriptor flipDescriptor = new RenderTextureDescriptor(
                destination.width,
                destination.height,
                renderTexture.format,
                0);
            // Why sRGB: Blit samples the source through sRGB decode in Linear color space, so the
            // destination must re-encode on write; ReadPixels copies raw destination bytes.
            flipDescriptor.sRGB = true;

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture flipped = RenderTexture.GetTemporary(flipDescriptor);
            try
            {
                // Match screenshot rendering capture. Identity Blit produced an inverted
                // paused-frame video against that PNG (yellow top / red bottom).
                Graphics.Blit(renderTexture, flipped, new Vector2(1f, -1f), new Vector2(0f, 1f));
                RenderTexture.active = flipped;
                destination.ReadPixels(new Rect(0, 0, destination.width, destination.height), 0, 0);
                destination.Apply();
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(flipped);
            }
        }
    }
}
