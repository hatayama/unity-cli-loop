using System.Collections.Generic;
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
    /// Verifies the Unity watch tool response stays aligned with the shared Go CLI contract.
    /// </summary>
    public sealed class WatchResponseContractTests
    {
        private const string SharedContractPath = "tests/contracts/watch_response_contract.json";

        [Test]
        public void WatchResponse_WhenSerialized_MatchesSharedContractFieldShape()
        {
            // Verifies C# does not add, remove, or rename watch response fields without updating
            // the shared CLI contract.
            JObject expected = ReadSharedContractFieldShape();
            WatchResponse response = new()
            {
                Success = true,
                Id = "speed",
                Expression = "1 + 2",
                MaxHistory = 20,
                HistoryDroppedCount = 0,
                ClearedCount = 0,
                Message = "Watch values retrieved.",
                Watches = new List<WatchEntryResponse>
                {
                    new()
                    {
                        Id = "speed",
                        Expression = "1 + 2",
                        MaxHistory = 20,
                        HistoryDroppedCount = 0,
                        ValueFrozenHint = "",
                        History = new List<WatchHistoryResponse>
                        {
                            new()
                            {
                                FrameCount = 42,
                                EvaluatedAtUtc = "2026-06-03T00:00:01.0000000Z",
                                Success = true,
                                Value = "3",
                                Truncated = false,
                                ErrorTypeName = "",
                                ErrorMessage = ""
                            }
                        }
                    }
                },
                CompilationErrors = new List<WatchCompilationErrorResponse>
                {
                    new()
                    {
                        Line = 3,
                        Column = 25,
                        Message = "; expected",
                        ErrorCode = "CS1002"
                    }
                }
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
