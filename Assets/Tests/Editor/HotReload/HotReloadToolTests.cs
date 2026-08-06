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

        /// <summary>
        /// What: BuildApplyResponse does not emit pause-point warnings when no markers
        /// were retargeted or suppressed, even if PatchedTotal &gt; 0.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithoutPausePointTransitions_AddsNoPausePointWarning()
        {
            HotReloadOrchestratorResult patched = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1);

            HotReloadResponse patchedResponse = HotReloadTool.BuildApplyResponse(patched);

            Assert.That(
                string.Join(" | ", patchedResponse.Warnings),
                Does.Not.Contain("pause points").IgnoreCase);
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
                    "Armed pause points could not be re-targeted and will not fire until the patch "
                    + "is reverted or compiled for real: Assets/Scripts/A.cs:10, Assets/Scripts/B.cs:20"));
        }

        /// <summary>
        /// What: BuildApplyResponse lists retargeted pause-point marker ids in Warnings
        /// when auto-retarget kept markers firing on patched bodies.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithRetargetedPausePointIds_AddsAggregatedWarning()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1,
                retargetedPausePointIds: new List<string>
                {
                    "Assets/Scripts/A.cs:10"
                });

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Warnings,
                Does.Contain(
                    "Armed pause points were re-targeted onto the hot-reload patched bodies and keep "
                    + "firing at the edited lines: Assets/Scripts/A.cs:10"));
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

        /// <summary>
        /// What: an empty Methods list yields the "no patchable method bodies" message (never "See Methods").
        /// </summary>
        [Test]
        public void BuildApplyResponse_EmptyMethods_YieldsNoPatchableBodiesMessage()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>(),
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Message,
                Does.Contain("Hot reload found no patchable method bodies in the given files"));
            Assert.That(response.Message, Does.Not.Contain("See Methods"));
        }

        /// <summary>
        /// What: an empty Methods list with UnchangedTotal reports the all-unchanged message
        /// instead of the generic "no patchable bodies" wording.
        /// </summary>
        [Test]
        public void BuildApplyResponse_EmptyMethodsWithUnchangedTotal_YieldsAllUnchangedMessage()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>(),
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0,
                unchangedTotal: 8);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Message,
                Is.EqualTo("All 8 methods are unchanged since the last compile; nothing to patch."));
            Assert.That(response.UnchangedTotal, Is.EqualTo(8));
        }

        /// <summary>
        /// What: a patched run with UnchangedTotal appends the untouched-methods suffix.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithUnchangedTotal_AppendsUntouchedSuffix()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1,
                unchangedTotal: 7);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. "
                    + "7 unchanged methods were left untouched."));
            Assert.That(response.UnchangedTotal, Is.EqualTo(7));
        }

        /// <summary>
        /// What: a skipped-only run keeps the existing "See Methods for Skipped reasons." message.
        /// </summary>
        [Test]
        public void BuildApplyResponse_SkippedOnly_KeepsExistingSkippedMessage()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Skipped("T.M", "reason", "file.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Message,
                Is.EqualTo("Hot reload finished with no methods patched. See Methods for Skipped reasons."));
        }

        /// <summary>
        /// What: a failed run keeps the existing failure message.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithFailedOutcome_KeepsExistingFailureMessage()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Failed("T.M", "reason", "file.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Message,
                Is.EqualTo("Hot reload finished with one or more Failed method outcomes. See Methods."));
        }
    }
}
