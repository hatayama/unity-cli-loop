using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests run-tests JSON-RPC metadata used to keep respect-path runs alive after disconnect.
    /// </summary>
    public sealed class JsonRpcRunTestsRequestMetadataReaderTests
    {
        /// <summary>
        /// Verifies PlayMode plus the respect flag is treated as a surviving run.
        /// </summary>
        [Test]
        public void ReadRespectsEnterPlayModeSettings_WhenPlayModeAndTrue_ReturnsTrue()
        {
            JObject paramsObject = JObject.Parse(
                "{\"RespectEnterPlayModeSettings\":true,\"TestMode\":\"PlayMode\"}");

            bool respects = JsonRpcRunTestsRequestMetadataReader.ReadRespectsEnterPlayModeSettings(paramsObject);

            Assert.That(respects, Is.True);
        }

        /// <summary>
        /// Verifies EditMode ignores the respect flag for disconnect cancellation.
        /// </summary>
        [Test]
        public void ReadRespectsEnterPlayModeSettings_WhenEditModeAndTrue_ReturnsFalse()
        {
            JObject paramsObject = JObject.Parse(
                "{\"RespectEnterPlayModeSettings\":true,\"TestMode\":\"EditMode\"}");

            bool respects = JsonRpcRunTestsRequestMetadataReader.ReadRespectsEnterPlayModeSettings(paramsObject);

            Assert.That(respects, Is.False);
        }

        /// <summary>
        /// Verifies a missing respect flag does not keep the run after disconnect.
        /// </summary>
        [Test]
        public void ReadRespectsEnterPlayModeSettings_WhenFlagIsMissing_ReturnsFalse()
        {
            JObject paramsObject = JObject.Parse("{\"TestMode\":\"PlayMode\"}");

            bool respects = JsonRpcRunTestsRequestMetadataReader.ReadRespectsEnterPlayModeSettings(paramsObject);

            Assert.That(respects, Is.False);
        }

        /// <summary>
        /// Verifies non-object params are treated as not respecting Enter Play Mode settings.
        /// </summary>
        [Test]
        public void ReadRespectsEnterPlayModeSettings_WhenParamsAreNotObject_ReturnsFalse()
        {
            bool respects = JsonRpcRunTestsRequestMetadataReader.ReadRespectsEnterPlayModeSettings(new JArray());

            Assert.That(respects, Is.False);
        }
    }
}
