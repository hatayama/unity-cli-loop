using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Freezes the cli_update_required JSON-RPC error.data frame that old CLIs must keep parsing forever.
    /// </summary>
    public sealed class CliUpdateRequiredErrorContractTests
    {
        private const string SharedContractPath = "tests/contracts/cli_update_required_error_contract.json";

        [Test]
        public void CreateCliProtocolMismatchResponse_WhenOlderProtocol_MatchesSharedErrorDataFieldShape()
        {
            // This frame must remain backward compatible forever — old CLIs parse it to learn they must update.
            // Verifies JsonRpcResponseFactory still emits the frozen error.data field shape for older CLIs.
            JObject expected = ReadSharedErrorDataFieldShape();
            string responseJson = JsonRpcResponseFactory.CreateCliProtocolMismatchResponse(
                id: "contract-test",
                currentCliVersion: "3.0.0-beta.5",
                currentProtocolVersion: 1);
            JObject actualData = (JObject)JObject.Parse(responseJson)["error"]!["data"]!;
            JObject actual = NormalizeFieldShape(actualData);

            Assert.That(JToken.DeepEquals(actual, expected), Is.True, $"Expected {expected} but got {actual}");
            Assert.That(actualData.Value<string>("type"), Is.EqualTo("cli_update_required"));
            Assert.That(
                actualData.Value<int>("requiredProtocolVersion"),
                Is.EqualTo(CliConstants.REQUIRED_CLI_PROTOCOL_VERSION));
            Assert.That(actualData.Value<string>("updateCommand"), Is.EqualTo("uloop update"));
        }

        private static JObject ReadSharedErrorDataFieldShape()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string json = File.ReadAllText(Path.Combine(projectRoot, SharedContractPath));
            JObject contract = JObject.Parse(json);
            return NormalizeFieldShape((JObject)contract["errorData"]!);
        }

        private static JObject NormalizeFieldShape(JObject value)
        {
            JObject shape = new();
            foreach (JProperty property in value.Properties())
            {
                shape[property.Name] = NormalizeTokenFieldShape(property.Value);
            }
            return shape;
        }

        private static JToken NormalizeTokenFieldShape(JToken value)
        {
            if (value is JObject objectValue)
            {
                return NormalizeFieldShape(objectValue);
            }

            if (value is JArray arrayValue)
            {
                JArray arrayShape = new();
                if (arrayValue.Count > 0)
                {
                    arrayShape.Add(NormalizeTokenFieldShape(arrayValue[0]));
                }
                return arrayShape;
            }

            return new JValue(NormalizeScalarType(value.Type));
        }

        private static string NormalizeScalarType(JTokenType type)
        {
            return type switch
            {
                JTokenType.Boolean => "boolean",
                JTokenType.Float => "number",
                JTokenType.Integer => "number",
                JTokenType.Null => "null",
                JTokenType.String => "string",
                _ => "unknown"
            };
        }
    }
}
