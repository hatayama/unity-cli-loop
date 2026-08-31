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

        /// <summary>
        /// What: omitted CaptureMode deserializes to auto so Play Mode can resolve it to rendering.
        /// </summary>
        [Test]
        public void ConvertToSchema_WhenCaptureModeIsOmitted_DefaultsToAuto()
        {
            JObject parameters = new JObject();

            ScreenshotSchema schema = DeserializeScreenshotSchema(parameters);

            Assert.That(schema.CaptureMode, Is.EqualTo(CaptureMode.auto));
        }

        /// <summary>
        /// What: Edit Mode auto + annotate-elements still fails validation because auto resolves to window.
        /// </summary>
        [Test]
        public void ExecuteAsync_WhenCaptureModeOmittedWithAnnotateElementsInEditMode_ShouldThrowValidationException()
        {
            JObject parameters = new JObject
            {
                ["AnnotateElements"] = true
            };

            UnityCliLoopToolParameterValidationException? exception =
                Assert.ThrowsAsync<UnityCliLoopToolParameterValidationException>(
                    async () => await ExecuteScreenshot(parameters));

            Assert.That(
                exception!.Message,
                Is.EqualTo("AnnotateElements is only supported when CaptureMode=rendering"));
        }

        /// <summary>
        /// What: explicit window + annotate-elements is still rejected while Play Mode is injected.
        /// </summary>
        [Test]
        public void CaptureAsync_WhenWindowSpecifiedWithAnnotateElementsWhilePlaying_ShouldThrowValidationException()
        {
            JObject parameters = new JObject
            {
                ["CaptureMode"] = "window",
                ["AnnotateElements"] = true
            };
            ScreenshotSchema schema = DeserializeScreenshotSchema(parameters);
            ScreenshotUseCase useCase = new ScreenshotUseCase(new FakeScreenshotEditorStateReader(true));

            UnityCliLoopToolParameterValidationException? exception =
                Assert.ThrowsAsync<UnityCliLoopToolParameterValidationException>(
                    async () => await useCase.CaptureAsync(schema, CancellationToken.None));

            Assert.That(
                exception!.Message,
                Is.EqualTo("AnnotateElements is only supported when CaptureMode=rendering"));
        }

        /// <summary>
        /// What: omitted CaptureMode + annotate-elements in injected Play Mode passes validation and resolves to rendering.
        /// </summary>
        [Test]
        public async Task CaptureAsync_WhenCaptureModeOmittedWithAnnotateElementsWhilePlaying_ResolvesToRendering()
        {
            JObject parameters = new JObject
            {
                ["annotateElements"] = true
            };
            ScreenshotSchema schema = DeserializeScreenshotSchema(parameters);
            ScreenshotUseCase useCase = new ScreenshotUseCase(new FakeScreenshotEditorStateReader(true));

            ScreenshotResponse response = await useCase.CaptureAsync(schema, CancellationToken.None);

            Assert.That(response.ResolvedCaptureMode, Is.EqualTo("rendering"));
            Assert.That(
                response.Message,
                Is.EqualTo("Rendering screenshots require PlayMode, but Unity is currently in EditMode."));
        }

        private static ScreenshotSchema DeserializeScreenshotSchema(JObject parameters)
        {
            ScreenshotSchema? schema = parameters.ToObject<ScreenshotSchema>(
                UnityCliLoopToolParameterSerializer.CamelCaseSerializer);
            Assert.That(schema, Is.Not.Null);
            return schema!;
        }

        private static async Task<UnityCliLoopToolResponse> ExecuteScreenshot(JObject parameters)
        {
            ScreenshotTool tool = new();
            return await tool.ExecuteAsync(parameters, CancellationToken.None);
        }

        private sealed class FakeScreenshotEditorStateReader : IScreenshotEditorStateReader
        {
            public FakeScreenshotEditorStateReader(bool isPlaying)
            {
                IsPlaying = isPlaying;
            }

            public bool IsPlaying { get; }
        }
    }
}
