using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
{
    public class ScreenshotToolTests
    {
        [Test]
        public void ExecuteAsync_WhenRaycastLayerMaskIsSetWithoutRaycastGrid_ShouldThrowValidationException()
        {
            ScreenshotTool tool = new ScreenshotTool();
            JObject parameters = new JObject
            {
                ["RaycastLayerMask"] = "Default"
            };

            ParameterValidationException exception = Assert.ThrowsAsync<ParameterValidationException>(
                async () => await tool.ExecuteAsync(parameters));

            Assert.That(exception.Message, Does.Contain("RaycastLayerMask requires AnnotateRaycastGrid=true"));
        }

        [Test]
        public void ExecuteAsync_WhenElementsOnlyHasNoAnnotationMode_ShouldThrowValidationException()
        {
            ScreenshotTool tool = new ScreenshotTool();
            JObject parameters = new JObject
            {
                ["CaptureMode"] = "rendering",
                ["ElementsOnly"] = true
            };

            ParameterValidationException exception = Assert.ThrowsAsync<ParameterValidationException>(
                async () => await tool.ExecuteAsync(parameters));

            Assert.That(
                exception.Message,
                Does.Contain("ElementsOnly requires AnnotateElements=true or AnnotateRaycastGrid=true"));
        }

        [Test]
        public async Task ExecuteAsync_WhenElementsOnlyUsesRaycastGrid_ShouldPassValidation()
        {
            ScreenshotTool tool = new ScreenshotTool();
            JObject parameters = new JObject
            {
                ["CaptureMode"] = "rendering",
                ["AnnotateRaycastGrid"] = true,
                ["ElementsOnly"] = true
            };

            BaseToolResponse response = await tool.ExecuteAsync(parameters);

            Assert.That(response, Is.InstanceOf<ScreenshotResponse>());
        }

        [Test]
        public void ExecuteAsync_WhenRaycastLayerMaskContainsUnknownLayer_ShouldThrowValidationException()
        {
            ScreenshotTool tool = new ScreenshotTool();
            JObject parameters = new JObject
            {
                ["CaptureMode"] = "rendering",
                ["AnnotateRaycastGrid"] = true,
                ["RaycastLayerMask"] = "MissingLayerForTest"
            };

            ParameterValidationException exception = Assert.ThrowsAsync<ParameterValidationException>(
                async () => await tool.ExecuteAsync(parameters));

            Assert.That(exception.Message, Does.Contain("unknown layer name"));
            Assert.That(exception.Message, Does.Contain("MissingLayerForTest"));
        }
    }
}
