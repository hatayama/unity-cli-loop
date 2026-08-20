using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Freezes the server_busy JSON-RPC error.data frame that the Go CLI decodes into a typed struct.
    /// </summary>
    public sealed class ServerBusyErrorContractTests
    {
        private const string SharedContractPath = "tests/contracts/server_busy_error_contract.json";

        /// <summary>
        /// Verifies ServerBusyErrorData still emits the frozen error.data field shape the Go CLI decodes.
        /// </summary>
        [Test]
        public void ServerBusyErrorData_WhenSerialized_MatchesSharedErrorDataFieldShape()
        {
            JObject expected = ReadSharedErrorDataFieldShape();
            ServerBusyErrorData errorData = new ServerBusyErrorData(
                runningToolName: "compile",
                requestedToolName: "get-logs",
                isPlaying: true,
                isPaused: false,
                message: "Unity is busy running 'compile'. Retry 'get-logs' after the running tool completes.",
                secondsSinceLastMainThreadTick: 1.5,
                isCompiling: true,
                isUpdating: false,
                runningToolElapsedSeconds: 12);
            string json = JsonConvert.SerializeObject(
                errorData,
                Formatting.None,
                UnityCliLoopJsonResponseSerializerSettings.Settings);
            JObject actualData = JObject.Parse(json);
            JObject actual = NormalizeFieldShape(actualData);

            Assert.That(JToken.DeepEquals(actual, expected), Is.True, $"Expected {expected} but got {actual}");
            Assert.That(actualData.Value<string>("type"), Is.EqualTo("server_busy"));
            Assert.That(actualData.Value<string>("runningToolName"), Is.EqualTo("compile"));
            Assert.That(actualData.Value<string>("requestedToolName"), Is.EqualTo("get-logs"));
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
