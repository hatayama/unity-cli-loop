using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the Unity pause-point-status response (including captured
    /// variables) stays aligned with the shared Go CLI contract.
    /// </summary>
    public sealed class PausePointStatusResponseContractTests
    {
        private const string SharedContractPath = "tests/contracts/pause_point_status_response_contract.json";

        [Test]
        public void PausePointStatusResponse_WhenSerialized_MatchesSharedContractFieldShape()
        {
            // Verifies C# does not add, remove, or rename fields (including CapturedVariables) without
            // updating the shared CLI contract.
            JObject expected = ReadSharedContractFieldShape();
            PausePointStatusResponse response = new()
            {
                Id = "Assets/Scripts/Enemy.cs:42",
                Status = "Hit",
                IsEnabled = true,
                IsHit = true,
                HitCount = 1,
                TimeoutSeconds = 30,
                Expired = false,
                EnabledAtUtc = "2026-06-03T00:00:00.0000000Z",
                ElapsedSinceEnabledMilliseconds = 1200,
                RemainingMilliseconds = 28800,
                Generation = 3,
                EditorState = new PausePointStatusEditorState
                {
                    IsPlaying = true,
                    IsPaused = true,
                    CapturedAt = "PausePointHit"
                },
                FirstHitAtUtc = "2026-06-03T00:00:01.0000000Z",
                LastHitAtUtc = "2026-06-03T00:00:01.0000000Z",
                FirstHitSequence = 1,
                LastHitSequence = 1,
                Message = "Pause point hit.",
                RecommendedNextAction = "Clear this marker, then re-enable it with the same Id and TimeoutSeconds values.",
                CapturedVariables = new List<PausePointStatusCapturedVariable>
                {
                    new()
                    {
                        Name = "target",
                        Scope = "InstanceField",
                        TypeName = "UnityEngine.GameObject",
                        Value = "Enemy",
                        UnityObjectKind = "SceneObject",
                        UnityObjectPath = "MainScene:/Root/Enemy",
                        UnityObjectInstanceId = -1234
                    }
                },
                CapturedVariablesTruncated = true
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
