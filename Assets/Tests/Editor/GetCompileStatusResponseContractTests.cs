using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Unity get-compile-status responses stay aligned with the shared Go CLI contract.
    /// </summary>
    public sealed class GetCompileStatusResponseContractTests
    {
        private const string SharedContractPath = "tests/contracts/compile_status_response_contract.json";

        [Test]
        public void GetCompileStatusResponse_WhenSerialized_MatchesSharedContractFieldShape()
        {
            // Verifies C# does not add, remove, or rename get-compile-status fields without updating the
            // shared CLI contract. Result must come from a real CompileResponse serialization because that
            // is what CompileStatusBridgeCommand restores from ResultJson for Go compileResultStatus.Success.
            JObject expected = ReadSharedContractFieldShape();
            JObject compileResultJson = SerializeCompileResponseResult();
            GetCompileStatusResponse response = new()
            {
                Ready = true,
                HasResult = true,
                IsCompiling = true,
                IsUpdating = true,
                IsDomainReloadInProgress = true,
                Result = compileResultJson,
                Message = "Compile result is available."
            };
            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                UnityCliLoopJsonResponseSerializerSettings.Settings);
            JObject actual = NormalizeFieldShape(JObject.Parse(json));

            Assert.That(JToken.DeepEquals(actual, expected), Is.True, $"Expected {expected} but got {actual}");
        }

        [Test]
        public void CompileResponse_WhenSerializedForCompileStatusResult_IncludesSuccessProperty()
        {
            // Verifies the wire Result payload still exposes Success under the name Go unmarshals into
            // compileResultStatus — a rename on CompileResponse must fail this test.
            JObject compileResultJson = SerializeCompileResponseResult();
            Assert.That(compileResultJson.Property("Success"), Is.Not.Null);
            Assert.That(compileResultJson["Success"]!.Type, Is.EqualTo(JTokenType.Boolean));
        }

        private static JObject SerializeCompileResponseResult()
        {
            CompileResponse compileResult = new(
                success: true,
                errorCount: 0,
                warningCount: 0,
                errors: Array.Empty<CompileIssue>(),
                warnings: Array.Empty<CompileIssue>(),
                message: "Compile result is available.");
            string resultJson = JsonConvert.SerializeObject(
                compileResult,
                Formatting.None,
                UnityCliLoopJsonResponseSerializerSettings.Settings);
            return JObject.Parse(resultJson);
        }

        private static JObject ReadSharedContractFieldShape()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string json = File.ReadAllText(Path.Combine(projectRoot, SharedContractPath));
            return NormalizeFieldShape(JObject.Parse(json));
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
