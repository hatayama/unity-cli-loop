using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Lightweight coverage for HotReloadTool validation and response aggregation
    /// (no orchestrator / worker invocation).
    /// </summary>
    public class HotReloadToolTests
    {
        [Test]
        public void ToolName_ReturnsHotReload()
        {
            // Verifies the registered CLI command name is hot-reload.
            HotReloadTool tool = new HotReloadTool();
            Assert.That(tool.ToolName, Is.EqualTo("hot-reload"));
        }

        [Test]
        public async Task ExecuteAsync_WithoutFilesOrRevertAll_ReturnsValidationFailure()
        {
            // Verifies --files is required when --revert-all is not set.
            HotReloadTool tool = new HotReloadTool();
            JObject parameters = new JObject();

            UnityCliLoopToolResponse baseResponse =
                await tool.ExecuteAsync(parameters, CancellationToken.None);
            HotReloadResponse response = baseResponse as HotReloadResponse;

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Does.Contain("Files is required"));
        }

        [Test]
        public void ValidateApplyParameters_WhitespaceOnlyPath_ReturnsError()
        {
            // Verifies blank path entries are rejected before the orchestrator runs.
            HotReloadSchema schema = new HotReloadSchema
            {
                Files = new[] { "   " }
            };

            string error = HotReloadTool.ValidateApplyParameters(schema);

            Assert.That(error, Is.EqualTo("Files must not contain null or empty paths."));
        }

        [Test]
        public void BuildApplyResponse_WithFailedOutcome_SetsSuccessFalse()
        {
            // Verifies any Failed method outcome flips Success to false.
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Failed("Type.Method", "shim compile failed", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Methods.Count, Is.EqualTo(1));
            Assert.That(response.Methods[0].Kind, Is.EqualTo("Failed"));
        }

        [Test]
        public void BuildApplyResponse_AddsPausePointWarningOnlyWhenPatched()
        {
            // Verifies the pause-point interaction warning appears iff PatchedTotal > 0.
            HotReloadOrchestratorResult patched = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1);
            HotReloadOrchestratorResult skippedOnly = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Skipped("Type.Other", "partial type", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0);

            HotReloadResponse patchedResponse = HotReloadTool.BuildApplyResponse(patched);
            HotReloadResponse skippedResponse = HotReloadTool.BuildApplyResponse(skippedOnly);

            Assert.That(
                patchedResponse.Warnings,
                Does.Contain(HotReloadConstants.PausePointInteractionWarning));
            Assert.That(
                skippedResponse.Warnings,
                Does.Not.Contain(HotReloadConstants.PausePointInteractionWarning));
        }

        /// <summary>
        /// What: BuildApplyResponse lists suppressed pause-point marker ids in Warnings
        /// when the orchestrator collected any during the apply.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithSuppressedPausePointIds_AddsAggregatedWarning()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1,
                suppressedPausePointIds: new List<string>
                {
                    "Assets/Scripts/A.cs:10",
                    "Assets/Scripts/B.cs:20"
                });

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Warnings,
                Does.Contain(
                    "Armed pause points on the patched methods will not fire until the patch is reverted or compiled for real: Assets/Scripts/A.cs:10, Assets/Scripts/B.cs:20"));
        }

        [Test]
        public async Task ExecuteAsync_RevertAllWithNoActivePatches_ReportsClearedCountZero()
        {
            // Verifies --revert-all succeeds with a clear message when the ledger is empty.
            HotReloadPatcher.RevertAll();
            HotReloadTool tool = new HotReloadTool();
            JObject parameters = new JObject
            {
                ["RevertAll"] = true
            };

            UnityCliLoopToolResponse baseResponse =
                await tool.ExecuteAsync(parameters, CancellationToken.None);
            HotReloadResponse response = baseResponse as HotReloadResponse;

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Success, Is.True);
            Assert.That(response.ClearedCount, Is.EqualTo(0));
            Assert.That(response.Message, Is.EqualTo("No active hot-reload patches to revert."));
        }
    }
}
