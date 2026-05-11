using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the JSON-RPC CLI compatibility gate before Unity tool execution.
    /// </summary>
    public class JsonRpcProcessorCliVersionGateTests
    {
        [Test]
        public async Task ProcessRequest_WhenCliVersionSatisfiesMinimum_AllowsRequest()
        {
            // Verifies compatible CLI clients can execute bridge commands.
            string response = await JsonRpcProcessor.ProcessRequest(BuildGetVersionRequest(CliConstants.MINIMUM_REQUIRED_CLI_VERSION));
            JObject parsed = JObject.Parse(response);

            Assert.That(parsed["error"], Is.Null);
            Assert.That(parsed["result"], Is.Not.Null);
        }

        [Test]
        public async Task ProcessRequest_WhenCliVersionIsTooOld_ReturnsCliUpdateRequiredError()
        {
            // Verifies old CLI clients receive an exact update command before any tool runs.
            string response = await JsonRpcProcessor.ProcessRequest(BuildGetVersionRequest("3.0.0-beta.5"));
            JObject data = ParseErrorData(response);

            Assert.That(data["type"]?.ToString(), Is.EqualTo("cli_update_required"));
            Assert.That(data["currentCliVersion"]?.ToString(), Is.EqualTo("3.0.0-beta.5"));
            Assert.That(data["requiredCliVersion"]?.ToString(), Is.EqualTo(CliConstants.MINIMUM_REQUIRED_CLI_VERSION));
            Assert.That(
                data["updateCommand"]?.ToString(),
                Is.EqualTo($"uloop update --to-version {CliConstants.MINIMUM_REQUIRED_CLI_VERSION}"));
            Assert.That(data["fallbackUpdateCommand"]?.ToString(), Is.EqualTo("uloop update"));
            Assert.That(data["retryableAfterUpdate"]?.ToObject<bool>(), Is.True);
        }

        [Test]
        public async Task ProcessRequest_WhenCliMetadataIsMissing_ReturnsCliUpdateRequiredError()
        {
            // Verifies legacy clients without metadata are stopped with upgrade instructions.
            string response = await JsonRpcProcessor.ProcessRequest(
                "{\"jsonrpc\":\"2.0\",\"method\":\"get-version\",\"params\":{},\"id\":1}");
            JObject data = ParseErrorData(response);

            Assert.That(data["type"]?.ToString(), Is.EqualTo("cli_update_required"));
            Assert.That(data["currentCliVersion"]?.Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public async Task ProcessRequest_WhenCliVersionIsInvalid_ReturnsCliUpdateRequiredError()
        {
            // Verifies malformed CLI versions cannot bypass the compatibility gate.
            string response = await JsonRpcProcessor.ProcessRequest(BuildGetVersionRequest("not-a-version"));
            JObject data = ParseErrorData(response);

            Assert.That(data["type"]?.ToString(), Is.EqualTo("cli_update_required"));
            Assert.That(data["currentCliVersion"]?.ToString(), Is.EqualTo("not-a-version"));
        }

        private static string BuildGetVersionRequest(string cliVersion)
        {
            return
                "{\"jsonrpc\":\"2.0\",\"method\":\"get-version\",\"params\":{},\"id\":1,\"uloop\":{\"cliVersion\":\"" +
                cliVersion +
                "\"}}";
        }

        private static JObject ParseErrorData(string response)
        {
            JObject parsed = JObject.Parse(response);
            JObject error = parsed["error"] as JObject;
            Assert.That(error, Is.Not.Null);
            JObject data = error["data"] as JObject;
            Assert.That(data, Is.Not.Null);
            return data;
        }
    }
}
