#nullable enable
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies UniformColorDetector pixel uniformity checks used by screenshot capture retries.
    /// </summary>
    public class UniformColorDetectorTests
    {
        /// <summary>
        /// Verifies that an all-black pixel buffer returns that black color.
        /// </summary>
        [Test]
        public void DetectUniformColor_WhenAllPixelsBlack_ReturnsBlack()
        {
            Color32[] pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(0, 0, 0, 255);
            }

            Color32? result = UniformColorDetector.DetectUniformColor(pixels);

            Assert.That(result, Is.EqualTo(new Color32?(new Color32(0, 0, 0, 255))));
        }

        /// <summary>
        /// Verifies that a single non-matching trailing pixel makes the buffer non-uniform.
        /// </summary>
        [Test]
        public void DetectUniformColor_WhenLastPixelDiffers_ReturnsNull()
        {
            Color32[] pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(0, 0, 0, 255);
            }
            pixels[pixels.Length - 1] = new Color32(255, 255, 255, 255);

            Color32? result = UniformColorDetector.DetectUniformColor(pixels);

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies that an empty pixel buffer is treated as non-uniform.
        /// </summary>
        [Test]
        public void DetectUniformColor_WhenEmpty_ReturnsNull()
        {
            Color32[] pixels = System.Array.Empty<Color32>();

            Color32? result = UniformColorDetector.DetectUniformColor(pixels);

            Assert.That(result, Is.Null);
        }
    }
}
