#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Screenshot Use Case parameter validation.
    /// </summary>
    public class ScreenshotUseCaseTests
    {
        [Test]
        public void ExecuteAsync_WhenRaycastLayerMaskIsSetWithoutRaycastGrid_ShouldThrowValidationException()
        {
            // Tests that setting RaycastLayerMask without AnnotateRaycastGrid fails validation.
            JObject parameters = new JObject
            {
                ["RaycastLayerMask"] = "Default"
            };

            UnityCliLoopToolParameterValidationException? exception =
                Assert.ThrowsAsync<UnityCliLoopToolParameterValidationException>(
                    async () => await ExecuteScreenshot(parameters));

            Assert.That(exception!.Message, Does.Contain("RaycastLayerMask requires AnnotateRaycastGrid=true"));
        }

        [Test]
        public void ExecuteAsync_WhenElementsOnlyHasNoAnnotationMode_ShouldThrowValidationException()
        {
            // Tests that ElementsOnly without AnnotateElements or AnnotateRaycastGrid fails validation.
            JObject parameters = new JObject
            {
                ["CaptureMode"] = "rendering",
                ["ElementsOnly"] = true
            };

            UnityCliLoopToolParameterValidationException? exception =
                Assert.ThrowsAsync<UnityCliLoopToolParameterValidationException>(
                    async () => await ExecuteScreenshot(parameters));

            Assert.That(
                exception!.Message,
                Does.Contain("ElementsOnly requires AnnotateElements=true or AnnotateRaycastGrid=true"));
        }

        [Test]
        public async Task ExecuteAsync_WhenElementsOnlyUsesRaycastGrid_ShouldPassValidation()
        {
            // Tests that ElementsOnly combined with AnnotateRaycastGrid passes validation
            // (PlayMode is unavailable in EditMode, so capture itself no-ops after validation).
            JObject parameters = new JObject
            {
                ["CaptureMode"] = "rendering",
                ["AnnotateRaycastGrid"] = true,
                ["ElementsOnly"] = true
            };

            UnityCliLoopToolResponse response = await ExecuteScreenshot(parameters);

            Assert.That(response, Is.InstanceOf<ScreenshotResponse>());
        }

        [Test]
        public void ExecuteAsync_WhenRaycastLayerMaskContainsUnknownLayer_ShouldThrowValidationException()
        {
            // Tests that an unrecognized layer name in RaycastLayerMask fails validation with the layer name in the message.
            JObject parameters = new JObject
            {
                ["CaptureMode"] = "rendering",
                ["AnnotateRaycastGrid"] = true,
                ["RaycastLayerMask"] = "MissingLayerForTest"
            };

            UnityCliLoopToolParameterValidationException? exception =
                Assert.ThrowsAsync<UnityCliLoopToolParameterValidationException>(
                    async () => await ExecuteScreenshot(parameters));

            Assert.That(exception!.Message, Does.Contain("unknown layer name"));
            Assert.That(exception!.Message, Does.Contain("MissingLayerForTest"));
        }

        private static async Task<UnityCliLoopToolResponse> ExecuteScreenshot(JObject parameters)
        {
            ScreenshotTool tool = new();
            return await tool.ExecuteAsync(parameters, CancellationToken.None);
        }
    }
}
