using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.Runtime;
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
                Success = true,
                Id = "Assets/Scripts/Enemy.cs:42",
                Status = "Hit",
                IsEnabled = true,
                IsHit = true,
                HitCount = 1,
                MethodEntryCount = 1,
                HitWhen = "speed > 5",
                HitWhenSkippedCount = 2,
                HitWhenErrorNote = "--hit-when expected variable 'speed' to be a numeric primitive.",
                TimeoutSeconds = 30,
                Mode = "continuous",
                MaxHistory = 20,
                MaxPreviewElements = 15,
                MaxCallerFrames = 4,
                CapturedVariableHistory = new List<PausePointStatusCapturedHistoryFrame>
                {
                    new()
                    {
                        HitSequence = 1,
                        FrameCount = 42,
                        HitAtUtc = "2026-06-03T00:00:01.0000000Z",
                        CapturedVariables = new List<PausePointStatusCapturedVariable>(),
                        Truncated = false,
                        CallerFrames = new List<PausePointStatusCallerFrame>
                        {
                            new()
                            {
                                Method = "Game.AI.Tick",
                                File = "Assets/Scripts/AI.cs",
                                Line = 44
                            }
                        }
                    },
                    new()
                    {
                        HitSequence = 2,
                        FrameCount = 43,
                        HitAtUtc = "2026-06-03T00:00:02.0000000Z",
                        CapturedVariables = new List<PausePointStatusCapturedVariable>(),
                        Truncated = false,
                        CallerFrames = new List<PausePointStatusCallerFrame>()
                    }
                },
                HistoryDroppedCount = 0,
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
                RecommendedNextAction = "Re-enable the marker with a longer --timeout-seconds and trigger the code path again; clearing the expired marker first is not required.",
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
                        UnityObjectInstanceId = -1234,
                        Truncated = false
                    }
                },
                CallerFrames = new List<PausePointStatusCallerFrame>
                {
                    new()
                    {
                        Method = "Game.AI.Tick",
                        File = "Assets/Scripts/AI.cs",
                        Line = 44
                    }
                },
                CapturedVariablesTruncated = true,
                TruncatedVariableNames = new[] { "extraField" },
                TruncatedVariableCount = 1,
                ClearedReason = "",
                StatusBeforeClear = "",
                LateHitDiscardedAfterClear = false,
                SuppressedByHotReload = false,
                RetargetedToHotReloadPatch = false
            };
            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                UnityCliLoopJsonResponseSerializerSettings.Settings);
            JObject actual = NormalizeFieldShape(JObject.Parse(json));

            Assert.That(JToken.DeepEquals(actual, expected), Is.True, $"Expected {expected} but got {actual}");
        }

        /// <summary>
        /// What: status Warning carries SuppressedByHotReloadReason when the marker is suppressed.
        /// </summary>
        [Test]
        public void FromSnapshot_WhenSuppressed_WarningEqualsReason()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakePauseController(), () => DateTime.UtcNow);
            try
            {
                const string id = "status-warning-reason";
                const string reason = "The marker's line no longer resolves inside the hot-reload patched body.";
                UloopPausePointRegistry.Enable(id, 30);
                UloopPausePointRegistry.SetSuppressedByHotReload(id, true, reason);
                UloopPausePointRegistry.SetRetargetedToHotReloadPatch(id, false);

                PausePointStatusResponse response =
                    PausePointStatusResponse.FromSnapshot(UloopPausePointRegistry.GetStatus(id));

                Assert.That(response.SuppressedByHotReload, Is.True);
                Assert.That(response.SuppressedByHotReloadReason, Is.EqualTo(reason));
                Assert.That(response.Warning, Is.EqualTo(reason));
                Assert.That(response.RetargetedToHotReloadPatch, Is.False);
            }
            finally
            {
                UloopPausePointRegistry.ResetForTests();
            }
        }

        private sealed class FakePauseController : IUloopPausePointPauseController
        {
            public bool IsPlaying => true;
            public bool IsPaused => false;
            public void Pause()
            {
            }

            public void Resume()
            {
            }
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
