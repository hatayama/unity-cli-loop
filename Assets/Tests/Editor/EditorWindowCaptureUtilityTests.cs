using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Editor Window Capture Utility coordinate calculations.
    /// </summary>
    public class EditorWindowCaptureUtilityTests
    {
        [Test]
        public void CalculateImageToInputOffsetY_WhenRenderTextureIsShorterThanGameView_ShouldReturnTopOffset()
        {
            // Tests that a RenderTexture shorter than the Game View yields a positive top offset.
            Vector2 gameViewSize = new(1768f, 1383f);

            int offsetY = EditorWindowCaptureUtility.CalculateImageToInputOffsetY(gameViewSize, 1080);

            Assert.That(offsetY, Is.EqualTo(303));
        }

        [Test]
        public void CalculateImageToInputOffsetY_WhenRenderTextureMatchesGameView_ShouldReturnZero()
        {
            // Tests that a RenderTexture matching the Game View height yields a zero offset.
            Vector2 gameViewSize = new(1920f, 1080f);

            int offsetY = EditorWindowCaptureUtility.CalculateImageToInputOffsetY(gameViewSize, 1080);

            Assert.That(offsetY, Is.EqualTo(0));
        }

        [Test]
        public void CreateUnavailableGameRenderingImageInfo_WhenRenderTextureIsMissing_ShouldUseGameViewSizeAndZeroOffset()
        {
            // Tests that the fallback info reuses the Game View size for both sizes with a zero offset.
            Vector2 gameViewSize = new(1768f, 1383f);

            GameRenderingImageInfo renderingImageInfo =
                EditorWindowCaptureUtility.CreateUnavailableGameRenderingImageInfo(gameViewSize);

            Assert.That(renderingImageInfo.GameViewSize, Is.EqualTo(gameViewSize));
            Assert.That(renderingImageInfo.RenderingImageSize, Is.EqualTo(gameViewSize));
            Assert.That(renderingImageInfo.ImageToInputOffsetY, Is.EqualTo(0));
        }
    }
}
