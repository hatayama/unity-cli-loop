using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

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

        /// <summary>
        /// Verifies that reading an sRGB Play Mode view RenderTexture in Linear color
        /// space preserves sRGB brightness in the output bytes and flips vertically.
        /// </summary>
        [Test]
        public void ReadPlayModeViewTexture_WithSrgbSourceInLinearColorSpace_PreservesBrightnessAndFlips()
        {
            IgnoreWhenGpuBlitIsUnavailable();
            Assert.That(QualitySettings.activeColorSpace, Is.EqualTo(ColorSpace.Linear));

            const int SIZE = 4;
            Texture2D source = new(SIZE, SIZE, TextureFormat.RGB24, false);
            Color32 bottomColor = new(180, 60, 60, 255);
            Color32 topColor = new(60, 60, 180, 255);
            for (int y = 0; y < SIZE; y++)
            {
                Color32 rowColor = y < SIZE / 2 ? bottomColor : topColor;
                for (int x = 0; x < SIZE; x++)
                {
                    source.SetPixel(x, y, rowColor);
                }
            }

            source.Apply();

            RenderTextureDescriptor descriptor = new(SIZE, SIZE, RenderTextureFormat.ARGB32, 0);
            descriptor.sRGB = true;
            RenderTexture sourceRt = RenderTexture.GetTemporary(descriptor);
            Texture2D result = null;
            try
            {
                Graphics.Blit(source, sourceRt);
                result = EditorWindowCaptureUtility.ReadPlayModeViewTexture(sourceRt, 1.0f);
                Color32 actualBottom = result.GetPixel(0, 0);
                Color32 actualTop = result.GetPixel(0, SIZE - 1);
                AssertColor32Near(actualBottom, topColor, 5);
                AssertColor32Near(actualTop, bottomColor, 5);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(sourceRt);
                UnityEngine.Object.DestroyImmediate(source);
                if (result != null)
                {
                    UnityEngine.Object.DestroyImmediate(result);
                }
            }
        }

        /// <summary>
        /// Verifies that resolution scaling keeps sRGB brightness in Linear color space.
        /// </summary>
        [Test]
        public void ReadPlayModeViewTexture_WithResolutionScaleInLinearColorSpace_PreservesBrightness()
        {
            IgnoreWhenGpuBlitIsUnavailable();
            Assert.That(QualitySettings.activeColorSpace, Is.EqualTo(ColorSpace.Linear));

            const int SOURCE_SIZE = 8;
            Color32 gray = new(128, 128, 128, 255);
            Texture2D source = new(SOURCE_SIZE, SOURCE_SIZE, TextureFormat.RGB24, false);
            for (int y = 0; y < SOURCE_SIZE; y++)
            {
                for (int x = 0; x < SOURCE_SIZE; x++)
                {
                    source.SetPixel(x, y, gray);
                }
            }

            source.Apply();

            RenderTextureDescriptor descriptor = new(SOURCE_SIZE, SOURCE_SIZE, RenderTextureFormat.ARGB32, 0);
            descriptor.sRGB = true;
            RenderTexture sourceRt = RenderTexture.GetTemporary(descriptor);
            Texture2D result = null;
            try
            {
                Graphics.Blit(source, sourceRt);
                result = EditorWindowCaptureUtility.ReadPlayModeViewTexture(sourceRt, 0.5f);
                Assert.That(result.width, Is.EqualTo(4));
                Assert.That(result.height, Is.EqualTo(4));
                Color32 actual = result.GetPixel(1, 1);
                AssertColor32Near(actual, gray, 5);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(sourceRt);
                UnityEngine.Object.DestroyImmediate(source);
                if (result != null)
                {
                    UnityEngine.Object.DestroyImmediate(result);
                }
            }
        }

        // Null (-nographics) does not run Blit, and CI llvmpipe (OpenGLCore) does not
        // match real-GPU sRGB write encoding, so pixel-value checks cannot hold there.
        private static void IgnoreWhenGpuBlitIsUnavailable()
        {
            GraphicsDeviceType deviceType = SystemInfo.graphicsDeviceType;
            if (deviceType == GraphicsDeviceType.Null || deviceType == GraphicsDeviceType.OpenGLCore)
            {
                Assert.Ignore($"sRGB RenderTexture blits need a real GPU; skipped on {deviceType}.");
            }
        }

        private static void AssertColor32Near(Color32 actual, Color32 expected, int tolerance)
        {
            Assert.That(actual.r, Is.InRange(expected.r - tolerance, expected.r + tolerance));
            Assert.That(actual.g, Is.InRange(expected.g - tolerance, expected.g + tolerance));
            Assert.That(actual.b, Is.InRange(expected.b - tolerance, expected.b + tolerance));
            Assert.That(actual.a, Is.InRange(expected.a - tolerance, expected.a + tolerance));
        }
    }
}
