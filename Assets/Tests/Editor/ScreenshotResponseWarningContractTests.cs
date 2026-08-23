using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Pins ScreenshotResponse.Warning wire presence so deleting ShouldSerializeWarning cannot go unnoticed.
    /// </summary>
    public sealed class ScreenshotResponseWarningContractTests
    {
        /// <summary>
        /// What: an empty Warning is omitted from production JSON so `"Warning":""` cannot reappear unnoticed.
        /// </summary>
        [Test]
        public void ScreenshotResponse_WhenWarningIsEmpty_OmitsWarningPropertyFromJson()
        {
            ScreenshotResponse response = new ScreenshotResponse();

            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                JsonRpcResponseSerializer.Settings);
            JObject parsed = JObject.Parse(json);

            Assert.That(json, Does.Not.Contain("\"Warning\""));
            Assert.That(parsed.Property("Warning"), Is.Null);
        }

        /// <summary>
        /// What: a set Warning serializes under Warning with the exact chrome-warning sentence.
        /// </summary>
        [Test]
        public void ScreenshotResponse_WhenWarningIsSet_SerializesExactChromeWarningSentence()
        {
            ScreenshotResponse response = new ScreenshotResponse
            {
                Warning =
                    "This window capture includes Unity Editor chrome. If you wanted the Game View image (typical during Play Mode), re-run with --capture-mode rendering."
            };

            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                JsonRpcResponseSerializer.Settings);
            JObject parsed = JObject.Parse(json);

            Assert.That(
                json,
                Does.Contain(
                    "\"Warning\":\"This window capture includes Unity Editor chrome. If you wanted the Game View image (typical during Play Mode), re-run with --capture-mode rendering.\""));
            Assert.That(
                parsed.Value<string>("Warning"),
                Is.EqualTo(
                    "This window capture includes Unity Editor chrome. If you wanted the Game View image (typical during Play Mode), re-run with --capture-mode rendering."));
        }

        /// <summary>
        /// What: ResolvedCaptureMode is always present on the wire as window or rendering.
        /// </summary>
        [Test]
        public void ScreenshotResponse_WhenResolvedCaptureModeIsSet_SerializesExactWireName()
        {
            ScreenshotResponse response = new ScreenshotResponse
            {
                ResolvedCaptureMode = "rendering"
            };

            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                JsonRpcResponseSerializer.Settings);
            JObject parsed = JObject.Parse(json);

            Assert.That(json, Does.Contain("\"ResolvedCaptureMode\":\"rendering\""));
            Assert.That(parsed.Value<string>("ResolvedCaptureMode"), Is.EqualTo("rendering"));
        }
    }
}
