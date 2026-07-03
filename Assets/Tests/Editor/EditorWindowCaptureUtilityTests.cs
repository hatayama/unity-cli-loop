using NUnit.Framework;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
{
    public class EditorWindowCaptureUtilityTests
    {
        [Test]
        public void CalculateImageToInputOffsetY_WhenRenderTextureIsShorterThanGameView_ShouldReturnTopOffset()
        {
            Vector2 gameViewSize = new Vector2(1768f, 1383f);

            int offsetY = EditorWindowCaptureUtility.CalculateImageToInputOffsetY(gameViewSize, 1080);

            Assert.That(offsetY, Is.EqualTo(303));
        }

        [Test]
        public void CalculateImageToInputOffsetY_WhenRenderTextureMatchesGameView_ShouldReturnZero()
        {
            Vector2 gameViewSize = new Vector2(1920f, 1080f);

            int offsetY = EditorWindowCaptureUtility.CalculateImageToInputOffsetY(gameViewSize, 1080);

            Assert.That(offsetY, Is.EqualTo(0));
        }

        [Test]
        public void CreateUnavailableGameRenderingImageInfo_WhenRenderTextureIsMissing_ShouldUseGameViewSizeAndZeroOffset()
        {
            Vector2 gameViewSize = new Vector2(1768f, 1383f);

            GameRenderingImageInfo renderingImageInfo =
                EditorWindowCaptureUtility.CreateUnavailableGameRenderingImageInfo(gameViewSize);

            Assert.That(renderingImageInfo.GameViewSize, Is.EqualTo(gameViewSize));
            Assert.That(renderingImageInfo.RenderingImageSize, Is.EqualTo(gameViewSize));
            Assert.That(renderingImageInfo.ImageToInputOffsetY, Is.EqualTo(0));
        }
    }
}
