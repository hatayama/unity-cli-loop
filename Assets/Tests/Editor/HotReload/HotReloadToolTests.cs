using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
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
        /// What: BuildApplyResponse lists retargeted pause-point marker ids with resolved
        /// line text from the registry (no "keep firing at the edited lines" wording).
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithRetargetedPausePointIds_AddsAggregatedWarning()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakePausePointPauseController(), () => DateTime.UtcNow);
            try
            {
                const string id = "Assets/Scripts/A.cs:10";
                UloopPausePointRegistry.Enable(id, 30);
                UloopPausePointRegistry.SetResolvedLine(id, 12, "return value;");

                HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                    },
                    new List<string>(),
                    patchedTotal: 1,
                    activePatchTotal: 1,
                    retargetedPausePointIds: new List<string> { id });

                HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

                Assert.That(
                    response.Warnings,
                    Does.Contain(
                        "Armed pause points were re-targeted onto the hot-reload patched bodies: "
                        + "Assets/Scripts/A.cs:10 (now line 12: return value;)"));
            }
            finally
            {
                UloopPausePointRegistry.ResetForTests();
            }
        }

        /// <summary>
        /// What: BuildApplyResponse drains retarget line-drift warnings into Warnings.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithRetargetLineDrift_AddsDriftWarning()
        {
            Func<IReadOnlyList<(string Id, string OldText, string NewText)>> previous =
                HotReloadPausePointCoordination.ConsumeRetargetLineDriftWarnings;
            HotReloadPausePointCoordination.ConsumeRetargetLineDriftWarnings = () =>
                new List<(string, string, string)>
                {
                    ("Assets/Scripts/A.cs:10", "return a;", "return a + 1;")
                };
            try
            {
                HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                    },
                    new List<string>(),
                    patchedTotal: 1,
                    activePatchTotal: 1);

                HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

                Assert.That(
                    response.Warnings,
                    Does.Contain(
                        "Pause point Assets/Scripts/A.cs:10 now targets a different statement "
                        + "(was: \"return a;\", now: \"return a + 1;\"). "
                        + "Re-enable it at the intended line if this is not what you want."));
            }
            finally
            {
                HotReloadPausePointCoordination.ConsumeRetargetLineDriftWarnings = previous;
            }
        }

        /// <summary>
        /// What: BuildApplyResponse drains expired-not-retargeted ids into Warnings.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithExpiredNotRetargetedIds_AddsAggregatedWarning()
        {
            Func<IReadOnlyList<string>> previous =
                HotReloadPausePointCoordination.ConsumeExpiredNotRetargetedMarkerIds;
            HotReloadPausePointCoordination.ConsumeExpiredNotRetargetedMarkerIds = () =>
                new List<string> { "Assets/Scripts/A.cs:10" };
            try
            {
                HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                    },
                    new List<string>(),
                    patchedTotal: 1,
                    activePatchTotal: 1);

                HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

                Assert.That(
                    response.Warnings,
                    Does.Contain(
                        "Expired pause points were not re-targeted and will not fire: Assets/Scripts/A.cs:10"));
            }
            finally
            {
                HotReloadPausePointCoordination.ConsumeExpiredNotRetargetedMarkerIds = previous;
            }
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
        /// What: BuildApplyResponse copies lifecycleNote onto Methods and appends it to Message.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithLifecycleNote_ExposesNoteOnMethodsAndMessage()
        {
            const string note =
                "BuildPlayer is only called from Awake (one-shot lifecycle methods); "
                + "the patched body may not run again for objects that are already initialized.";
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.BuildPlayer", "Assets/A.cs", note)
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.Methods.Count, Is.EqualTo(1));
            Assert.That(response.Methods[0].LifecycleNote, Is.EqualTo(note));
            Assert.That(response.Methods[0].Kind, Is.EqualTo("Patched"));
            Assert.That(response.Message, Does.Contain(note));
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

        private sealed class FakePausePointPauseController : IUloopPausePointPauseController
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
    }
}
