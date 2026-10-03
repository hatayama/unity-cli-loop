using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies which screenshot parameter combinations the validator rejects before capture starts.
    /// </summary>
    public sealed class ScreenshotParameterValidatorEdgeTests
    {
        /// <summary>
        /// Verifies a window capture without a window name is rejected.
        /// </summary>
        [Test]
        public void Validate_WithAWindowCaptureAndNoWindowName_Throws()
        {
            ScreenshotSchema request = new ScreenshotSchema { CaptureMode = CaptureMode.window, WindowName = "" };

            AssertRejected(request, "WindowName cannot be null or empty");
        }

        /// <summary>
        /// Verifies a rendering capture needs no window name.
        /// </summary>
        [Test]
        public void Validate_WithARenderingCaptureAndNoWindowName_Accepts()
        {
            ScreenshotSchema request = new ScreenshotSchema { CaptureMode = CaptureMode.rendering, WindowName = "" };

            Assert.DoesNotThrow(() => ScreenshotParameterValidator.Validate(request));
        }

        /// <summary>
        /// Verifies resolution scales outside 0.1 to 1.0 are rejected and the bounds themselves are accepted.
        /// </summary>
        [TestCase(0.09f, false, TestName = "Validate_WithAScaleBelowTheMinimum_Throws")]
        [TestCase(0.1f, true, TestName = "Validate_WithTheMinimumScale_Accepts")]
        [TestCase(1.0f, true, TestName = "Validate_WithTheMaximumScale_Accepts")]
        [TestCase(1.01f, false, TestName = "Validate_WithAScaleAboveTheMaximum_Throws")]
        public void Validate_ChecksTheResolutionScaleRange(float scale, bool accepted)
        {
            ScreenshotSchema request = new ScreenshotSchema { ResolutionScale = scale };

            if (accepted)
            {
                Assert.DoesNotThrow(() => ScreenshotParameterValidator.Validate(request));
                return;
            }

            AssertRejected(request, "ResolutionScale must be between 0.1 and 1.0");
        }

        /// <summary>
        /// Verifies ElementsOnly is rejected outside rendering mode.
        /// </summary>
        [Test]
        public void Validate_WithElementsOnlyOutsideRendering_Throws()
        {
            ScreenshotSchema request = new ScreenshotSchema { CaptureMode = CaptureMode.window, ElementsOnly = true };

            AssertRejected(request, "ElementsOnly is only supported when CaptureMode=rendering");
        }

        /// <summary>
        /// Verifies AnnotateRaycastGrid is rejected outside rendering mode.
        /// </summary>
        [Test]
        public void Validate_WithARaycastGridOutsideRendering_Throws()
        {
            ScreenshotSchema request = new ScreenshotSchema { CaptureMode = CaptureMode.window, AnnotateRaycastGrid = true };

            AssertRejected(request, "AnnotateRaycastGrid is only supported when CaptureMode=rendering");
        }

        private static void AssertRejected(ScreenshotSchema request, string expectedMessage)
        {
            UnityCliLoopToolParameterValidationException exception =
                Assert.Throws<UnityCliLoopToolParameterValidationException>(() => ScreenshotParameterValidator.Validate(request));
            Assert.That(exception.Message, Does.StartWith(expectedMessage));
        }
    }
}
