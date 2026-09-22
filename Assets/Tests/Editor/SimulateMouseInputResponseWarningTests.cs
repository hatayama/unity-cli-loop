using Newtonsoft.Json;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Pins how simulate-mouse-input serializes its optional Warning field.
    /// </summary>
    public sealed class SimulateMouseInputResponseWarningTests
    {
        /// <summary>
        /// Verifies an empty Warning is omitted from the production JSON.
        /// </summary>
        [Test]
        public void SimulateMouseInputResponse_WhenWarningIsEmpty_OmitsWarningKey()
        {
            SimulateMouseInputResponse response = new SimulateMouseInputResponse();

            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                JsonRpcResponseSerializer.Settings);

            Assert.That(json, Does.Not.Contain("\"Warning\""));
        }

        /// <summary>
        /// Verifies a non-empty Warning is serialized under its production key.
        /// </summary>
        [Test]
        public void SimulateMouseInputResponse_WhenWarningIsSet_IncludesWarningKey()
        {
            SimulateMouseInputResponse response = new SimulateMouseInputResponse
            {
                Warning = "monitor removed"
            };

            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                JsonRpcResponseSerializer.Settings);

            Assert.That(json, Does.Contain("\"Warning\":\"monitor removed\""));
        }
    }
}
