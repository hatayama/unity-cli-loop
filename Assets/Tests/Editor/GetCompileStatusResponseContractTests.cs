using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

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
            // Verifies C# does not add, remove, or rename get-compile-status fields (including Result.Success)
            // without updating the shared CLI contract that Go unmarshals during domain-reload waits.
            JObject expected = ReadSharedContractFieldShape();
            GetCompileStatusResponse response = new()
            {
                Ready = true,
                HasResult = true,
                IsCompiling = true,
                IsUpdating = true,
                IsDomainReloadInProgress = true,
                Result = new JObject
                {
                    ["Success"] = true
                },
                Message = "Compile result is available."
            };
            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                UnityCliLoopJsonResponseSerializerSettings.Settings);
            JObject actual = NormalizeFieldShape(JObject.Parse(json));

            Assert.That(JToken.DeepEquals(actual, expected), Is.True, $"Expected {expected} but got {actual}");
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
