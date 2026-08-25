using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies the wire contract for hot-reload validation responses.
    /// </summary>
    [TestFixture]
    public sealed class HotReloadValidationResponseTests
    {
        /// <summary>
        /// What: a validation failure serializes its structured error code and recovery actions.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilesAreMissing_SerializesStructuredValidationFields()
        {
            HotReloadTool tool = new HotReloadTool();
            UnityCliLoopToolResponse baseResponse =
                await tool.ExecuteAsync(new JObject(), CancellationToken.None);
            HotReloadResponse response = baseResponse as HotReloadResponse;
            JObject json = SerializeResponse(response);
            JArray nextActions = json["NextActions"] as JArray;

            Assert.That(response, Is.Not.Null);
            Assert.That(json.Value<string>("ErrorCode"), Is.EqualTo("HOT_RELOAD_FILES_REQUIRED"));
            Assert.That(nextActions, Is.Not.Null);
            Assert.That(
                nextActions.ToObject<string[]>(),
                Is.EqualTo(
                    new[]
                    {
                        "Pass project-relative .cs paths with --files.",
                        "Run 'uloop hot-reload --status' to inspect active patches."
                    }));
        }

        /// <summary>
        /// What: successful apply, status, and revert responses omit empty validation fields.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenNoValidationFailure_OmitsStructuredValidationFields()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/Scripts/Player.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1);
            HotReloadResponse applyResponse = HotReloadTool.BuildApplyResponse(result);
            HotReloadResponse statusResponse = await ExecuteAsync(new JObject { ["Status"] = true });
            HotReloadResponse revertResponse = await ExecuteAsync(new JObject { ["RevertAll"] = true });

            AssertValidationFieldsAreAbsent(SerializeResponse(applyResponse));
            AssertValidationFieldsAreAbsent(SerializeResponse(statusResponse));
            AssertValidationFieldsAreAbsent(SerializeResponse(revertResponse));
        }

        private static async Task<HotReloadResponse> ExecuteAsync(JObject parameters)
        {
            HotReloadTool tool = new HotReloadTool();
            UnityCliLoopToolResponse baseResponse =
                await tool.ExecuteAsync(parameters, CancellationToken.None);
            HotReloadResponse response = baseResponse as HotReloadResponse;
            Assert.That(response, Is.Not.Null);
            return response;
        }

        private static JObject SerializeResponse(HotReloadResponse response)
        {
            return JObject.Parse(
                JsonConvert.SerializeObject(
                    response,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));
        }

        private static void AssertValidationFieldsAreAbsent(JObject json)
        {
            Assert.That(json.Property("ErrorCode"), Is.Null);
            Assert.That(json.Property("NextActions"), Is.Null);
        }
    }
}
