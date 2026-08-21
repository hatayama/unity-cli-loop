using System;
using System.Collections.Generic;
using System.Reflection;
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

        /// <summary>
        /// What: calling hot-reload with neither --files, --revert-all, nor --status names
        /// --files and shows a project-relative .cs example.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WithoutFilesOrRevertAll_ReturnsValidationFailure()
        {
            HotReloadTool tool = new HotReloadTool();
            JObject parameters = new JObject();

            UnityCliLoopToolResponse baseResponse =
                await tool.ExecuteAsync(parameters, CancellationToken.None);
            HotReloadResponse response = baseResponse as HotReloadResponse;

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Success, Is.False);
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Files is required unless --revert-all or --status is set. Pass project-relative .cs paths with --files, e.g. 'uloop hot-reload --files Assets/Scripts/Player.cs'."));
        }

        /// <summary>
        /// What: --status Added rows explain InvocationCount 0 with the not-instrumented Reason
        /// only, not the AlreadyActive source-unchanged sentence.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Status_AddedRow_SetsNotInstrumentedReason()
        {
            const string filePath = "Assets/Tests/Editor/HotReload/StatusAddedReason.cs";
            const string methodKey = "Host.NewHelper(System.Int32)";
            MethodInfo shim = typeof(HotReloadAddedMemberHost).GetMethod(
                nameof(HotReloadAddedMemberHost.ExistingCaller),
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(shim, Is.Not.Null);

            HotReloadAddedMemberRegistry.BeginFileGeneration(filePath);
            HotReloadAddedMemberRegistry.Register(filePath, methodKey, shim, filePath);
            try
            {
                HotReloadTool tool = new HotReloadTool();
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(
                    new JObject { ["Status"] = true },
                    CancellationToken.None);
                HotReloadResponse response = baseResponse as HotReloadResponse;

                Assert.That(response, Is.Not.Null);
                Assert.That(response.Success, Is.True);
                HotReloadMethodResult addedRow = null;
                for (int index = 0; index < response.Methods.Count; index++)
                {
                    HotReloadMethodResult row = response.Methods[index];
                    if (row.Kind == HotReloadConstants.AddedMemberStatusKind && row.Method == methodKey)
                    {
                        addedRow = row;
                        break;
                    }
                }

                Assert.That(addedRow, Is.Not.Null);
                Assert.That(
                    addedRow.Reason,
                    Is.EqualTo(
                        "Added-member calls are not instrumented, so InvocationCount is always 0 for this row."));
                Assert.That(addedRow.InvocationCount, Is.EqualTo(0L));
            }
            finally
            {
                HotReloadAddedMemberRegistry.Clear();
            }
        }

        /// <summary>
        /// What: --status Active rows with InvocationCount 0 explain that finished calls do
        /// not re-run, and Message aggregates that count.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Status_NeverInvokedActiveRow_SetsNeverInvokedReason()
        {
            HotReloadPatcher.RevertAll();
            MethodInfo original = typeof(HotReloadCoreFixture).GetMethod(
                nameof(HotReloadCoreFixture.ReplaceableCompute),
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo shim = typeof(HotReloadHandwrittenShims).GetMethod(
                nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0),
                BindingFlags.Static | BindingFlags.Public);
            Assert.That(original, Is.Not.Null);
            Assert.That(shim, Is.Not.Null);

            try
            {
                HotReloadPatchResult applyResult = HotReloadPatcher.Apply(
                    original,
                    shim,
                    HotReloadPatchShape.Transplant,
                    "Assets/Tests/Fixture.cs");
                Assert.That(applyResult.Success, Is.True, applyResult.ErrorMessage);

                HotReloadTool tool = new HotReloadTool();
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(
                    new JObject { ["Status"] = true },
                    CancellationToken.None);
                HotReloadResponse response = baseResponse as HotReloadResponse;

                Assert.That(response, Is.Not.Null);
                Assert.That(response.Success, Is.True);
                HotReloadMethodResult activeRow = null;
                for (int index = 0; index < response.Methods.Count; index++)
                {
                    HotReloadMethodResult row = response.Methods[index];
                    if (row.Kind == "Active"
                        && row.Method.Contains(nameof(HotReloadCoreFixture.ReplaceableCompute)))
                    {
                        activeRow = row;
                        break;
                    }
                }

                Assert.That(activeRow, Is.Not.Null);
                Assert.That(activeRow.Kind, Is.EqualTo("Active"));
                Assert.That(activeRow.InvocationCount, Is.EqualTo(0L));
                Assert.That(
                    activeRow.Reason,
                    Is.EqualTo(
                        "Not invoked since this patch was applied. Calls that already finished before the patch (for example one-time initialization) do not re-run automatically; the patched body takes effect the next time this method is called."));
                Assert.That(
                    activeRow.Reason,
                    Is.EqualTo(HotReloadConstants.ActivePatchNeverInvokedReason));
                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        "1 change(s) currently active. 1 change(s) have not been invoked since their patch was applied; see Methods[].Reason."));
            }
            finally
            {
                HotReloadPatcher.RevertAll();
            }
        }

        /// <summary>
        /// What: --status Active rows that have run since the patch leave Reason empty and
        /// keep Message as the active-count sentence only.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Status_InvokedActiveRow_LeavesReasonEmpty()
        {
            HotReloadPatcher.RevertAll();
            MethodInfo original = typeof(HotReloadCoreFixture).GetMethod(
                nameof(HotReloadCoreFixture.ReplaceableCompute),
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo shim = typeof(HotReloadHandwrittenShims).GetMethod(
                nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0),
                BindingFlags.Static | BindingFlags.Public);
            Assert.That(original, Is.Not.Null);
            Assert.That(shim, Is.Not.Null);

            try
            {
                HotReloadPatchResult applyResult = HotReloadPatcher.Apply(
                    original,
                    shim,
                    HotReloadPatchShape.Transplant,
                    "Assets/Tests/Fixture.cs");
                Assert.That(applyResult.Success, Is.True, applyResult.ErrorMessage);

                HotReloadCoreFixture fixture = new HotReloadCoreFixture();
                Assert.That(fixture.ReplaceableCompute(5), Is.EqualTo(47));

                HotReloadTool tool = new HotReloadTool();
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(
                    new JObject { ["Status"] = true },
                    CancellationToken.None);
                HotReloadResponse response = baseResponse as HotReloadResponse;

                Assert.That(response, Is.Not.Null);
                Assert.That(response.Success, Is.True);
                HotReloadMethodResult activeRow = null;
                for (int index = 0; index < response.Methods.Count; index++)
                {
                    HotReloadMethodResult row = response.Methods[index];
                    if (row.Kind == "Active"
                        && row.Method.Contains(nameof(HotReloadCoreFixture.ReplaceableCompute)))
                    {
                        activeRow = row;
                        break;
                    }
                }

                Assert.That(activeRow, Is.Not.Null);
                Assert.That(activeRow.InvocationCount, Is.GreaterThanOrEqualTo(1L));
                Assert.That(activeRow.Reason, Is.EqualTo(string.Empty));
                Assert.That(
                    response.Message,
                    Is.EqualTo("1 change(s) currently active."));
            }
            finally
            {
                HotReloadPatcher.RevertAll();
            }
        }

        /// <summary>
        /// What: composing AlreadyActiveAddedMemberReason from the not-instrumented constant
        /// keeps the historical AlreadyActive added-member sentence byte-identical.
        /// </summary>
        [Test]
        public void AlreadyActiveAddedMemberReason_KeepsHistoricalWording()
        {
            Assert.That(
                HotReloadConstants.AlreadyActiveAddedMemberReason,
                Is.EqualTo(
                    "Source is unchanged since the last applied hot reload; the existing added member stays available. "
                    + "Added-member calls are not instrumented, so InvocationCount is always 0 for this row."));
        }

        /// <summary>
        /// What: an empty Files list names --files and shows a project-relative .cs example.
        /// </summary>
        [Test]
        public void ValidateApplyParameters_MissingFiles_ReturnsErrorNamingFilesOption()
        {
            HotReloadSchema schema = new HotReloadSchema
            {
                Files = Array.Empty<string>()
            };

            string error = HotReloadTool.ValidateApplyParameters(schema);

            Assert.That(
                error,
                Is.EqualTo(
                    "Files is required unless --revert-all or --status is set. Pass project-relative .cs paths with --files, e.g. 'uloop hot-reload --files Assets/Scripts/Player.cs'."));
        }

        /// <summary>
        /// What: blank path entries are rejected before the orchestrator runs.
        /// </summary>
        [Test]
        public void ValidateApplyParameters_WhitespaceOnlyPath_ReturnsError()
        {
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
        /// What: BuildApplyResponse sets the partial-apply RecommendedNextAction when a Failed
        /// outcome is mixed with patched methods.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WhenFailureWithPatchedMethods_SetsPartialApplyRecommendedNextAction()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Ok", "Assets/A.cs"),
                    HotReloadMethodOutcome.Failed("Type.Bad", "shim compile failed", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(
                    "Partially applied. Fix the failed methods and rerun, run 'uloop compile' to apply every edit, or run 'uloop hot-reload --revert-all' to discard the applied patches."));
            Assert.That(response.ShouldSerializeRecommendedNextAction(), Is.True);
        }

        /// <summary>
        /// What: BuildApplyResponse treats a Failed run that applied only added members as a
        /// partial apply, so CountAddedOutcomes cannot be dropped without this test failing.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WhenFailureWithOnlyAddedMembers_SetsPartialApplyRecommendedNextAction()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Added("Type.NewMember", "Assets/A.cs"),
                    HotReloadMethodOutcome.Failed("Type.Bad", "shim compile failed", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 1);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(
                    "Partially applied. Fix the failed methods and rerun, run 'uloop compile' to apply every edit, or run 'uloop hot-reload --revert-all' to discard the applied patches."));
            Assert.That(response.ShouldSerializeRecommendedNextAction(), Is.True);
        }

        /// <summary>
        /// What: BuildApplyResponse sets the fix-or-compile RecommendedNextAction when every
        /// outcome failed and nothing was applied.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WhenFailureWithNothingApplied_SetsFixOrCompileRecommendedNextAction()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Failed("Type.Method", "shim compile failed", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo("Fix the failed methods and rerun, or run 'uloop compile'."));
            Assert.That(response.ShouldSerializeRecommendedNextAction(), Is.True);
        }

        /// <summary>
        /// What: BuildApplyResponse leaves RecommendedNextAction empty on a successful apply
        /// so the field is omitted from JSON.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WhenSuccess_LeavesRecommendedNextActionEmpty()
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

            Assert.That(response.RecommendedNextAction, Is.EqualTo(string.Empty));
            Assert.That(response.ShouldSerializeRecommendedNextAction(), Is.False);
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
            Assert.That(response.Message, Is.EqualTo("No active hot-reload changes to revert."));
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
        /// What: an Added outcome appends Added: N to the apply message.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithAddedOutcome_AppendsAddedCount()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Caller", "Assets/A.cs"),
                    HotReloadMethodOutcome.Added("Type.AddedPing", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 2);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.Methods[1].Kind, Is.EqualTo(HotReloadConstants.AddedMemberStatusKind));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Hot reload applied. PatchedTotal=1, ActivePatchTotal=2. Added: 1."));
        }

        /// <summary>
        /// What: BuildApplyResponse keeps LifecycleNote on Methods and aggregates a count into
        /// Message instead of concatenating every note (two notes must not paste both paragraphs).
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithLifecycleNotes_ExposesPerMethodAndAggregatesMessage()
        {
            const string noteA =
                "Awake is a one-shot lifecycle method; objects that already ran it will not run the "
                + "patched body. It takes effect only for newly created objects.";
            const string noteB =
                "Start is a one-shot lifecycle method; objects that already ran it will not run the "
                + "patched body. It takes effect only for newly created objects.";
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Awake", "Assets/A.cs", noteA),
                    HotReloadMethodOutcome.Patched("Type.Start", "Assets/A.cs", noteB)
                },
                new List<string>(),
                patchedTotal: 2,
                activePatchTotal: 2);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.Methods.Count, Is.EqualTo(2));
            Assert.That(response.Methods[0].LifecycleNote, Is.EqualTo(noteA));
            Assert.That(response.Methods[1].LifecycleNote, Is.EqualTo(noteB));
            Assert.That(
                response.Message,
                Does.Contain("2 patched method(s) have one-shot lifecycle notes"));
            Assert.That(response.Message, Does.Contain("Methods[].LifecycleNote"));
            Assert.That(response.Message, Does.Not.Contain(noteA));
            Assert.That(response.Message, Does.Not.Contain(noteB));
        }

        /// <summary>
        /// What: a skipped-only run uses the no-patch message that also names AlreadyActive.
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
                Is.EqualTo(HotReloadConstants.NoMethodsPatchedSeeSkippedOrAlreadyActiveMessage));
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

        /// <summary>
        /// What: every apply-message branch appends the warning-count suffix when Warnings is
        /// non-empty, and the applied branch mentions Skipped before that suffix.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithWarnings_AppendsWarningCountOnEveryBranch()
        {
            const string warning = "const drift";
            List<string> oneWarning = new List<string> { warning };

            HotReloadResponse failed = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Failed("T.M", "reason", "file.cs")
                    },
                    oneWarning,
                    patchedTotal: 0,
                    activePatchTotal: 0));
            Assert.That(
                failed.Message,
                Is.EqualTo(
                    "Hot reload finished with one or more Failed method outcomes. See Methods. "
                    + "1 warning(s). See Warnings."));

            HotReloadResponse empty = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>(),
                    oneWarning,
                    patchedTotal: 0,
                    activePatchTotal: 0));
            Assert.That(
                empty.Message,
                Is.EqualTo(
                    "Hot reload found no patchable method bodies in the given files; nothing was changed. "
                    + "Hot reload only replaces existing ordinary method bodies; use uloop compile for other edits. "
                    + "1 warning(s). See Warnings."));

            HotReloadResponse allUnchanged = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>(),
                    oneWarning,
                    patchedTotal: 0,
                    activePatchTotal: 0,
                    unchangedTotal: 8));
            Assert.That(
                allUnchanged.Message,
                Is.EqualTo(
                    "All 8 methods are unchanged since the last compile; nothing to patch. "
                    + "1 warning(s). See Warnings."));

            HotReloadResponse skippedOnly = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Skipped("T.M", "reason", "file.cs")
                    },
                    oneWarning,
                    patchedTotal: 0,
                    activePatchTotal: 0));
            Assert.That(
                skippedOnly.Message,
                Is.EqualTo(
                    HotReloadConstants.NoMethodsPatchedSeeSkippedOrAlreadyActiveMessage
                    + " 1 warning(s). See Warnings."));

            HotReloadResponse applied = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                    },
                    new List<string> { "a", "b" },
                    patchedTotal: 1,
                    activePatchTotal: 1));
            Assert.That(
                applied.Message,
                Is.EqualTo(
                    "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. "
                    + "2 warning(s). See Warnings. "
                    + HotReloadConstants.MultiWarningSingleCompileResolutionMessage));

            HotReloadResponse appliedWithSkipped = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs"),
                        HotReloadMethodOutcome.Skipped("T.Skip", "reason", "file.cs")
                    },
                    new List<string> { "a", "b" },
                    patchedTotal: 1,
                    activePatchTotal: 1));
            Assert.That(
                appliedWithSkipped.Message,
                Is.EqualTo(
                    "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. "
                    + "See Methods for Skipped reasons. 2 warning(s). See Warnings. "
                    + HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
        }

        /// <summary>
        /// What: two or more orchestrator-only warnings append the single-compile resolution
        /// sentence after the warning-count suffix.
        /// </summary>
        [Test]
        public void BuildApplyResponse_TwoOrchestratorWarnings_AppendsSingleCompileResolution()
        {
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                    },
                    new List<string> { "warn-a", "warn-b" },
                    patchedTotal: 1,
                    activePatchTotal: 1));

            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. "
                    + "2 warning(s). See Warnings. "
                    + HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
        }

        /// <summary>
        /// What: a single orchestrator warning keeps the count suffix and does not add the
        /// single-compile resolution sentence.
        /// </summary>
        [Test]
        public void BuildApplyResponse_OneOrchestratorWarning_OmitsSingleCompileResolution()
        {
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                    },
                    new List<string> { "warn-a" },
                    patchedTotal: 1,
                    activePatchTotal: 1));

            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. "
                    + "1 warning(s). See Warnings."));
            Assert.That(
                response.Message,
                Does.Not.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
        }

        /// <summary>
        /// What: a pause-point warning merged onto two orchestrator warnings suppresses the
        /// single-compile resolution sentence, because compile alone cannot clear pause-point
        /// recovery steps.
        /// </summary>
        [Test]
        public void BuildApplyResponse_TwoOrchestratorWarningsPlusPausePoint_OmitsSingleCompileResolution()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                },
                new List<string> { "warn-a", "warn-b" },
                patchedTotal: 1,
                activePatchTotal: 1,
                suppressedPausePointIds: new List<string> { "Assets/Scripts/A.cs:10" });

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. "
                    + "3 warning(s). See Warnings."));
            Assert.That(
                response.Message,
                Does.Not.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
        }

        /// <summary>
        /// What: BuildApplyResponse copies orchestrator AddedFields onto the public response
        /// and uses an empty array when the result has none.
        /// </summary>
        [Test]
        public void BuildApplyResponse_CopiesAddedFieldsOrEmptyArray()
        {
            string[] addedFields =
            {
                "Ns.Host.AddedCount",
                "Ns.Host.AddedSerialized"
            };
            HotReloadResponse withFields = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                    },
                    new List<string>(),
                    patchedTotal: 1,
                    activePatchTotal: 1,
                    addedFields: addedFields));
            Assert.That(withFields.AddedFields, Is.EqualTo(addedFields));

            HotReloadResponse withoutFields = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                    },
                    new List<string>(),
                    patchedTotal: 1,
                    activePatchTotal: 1));
            Assert.That(withoutFields.AddedFields, Is.Not.Null);
            Assert.That(withoutFields.AddedFields, Is.Empty);
        }

        /// <summary>
        /// What: an applied run with Skipped outcomes and no warnings still points at Methods.
        /// </summary>
        [Test]
        public void BuildApplyResponse_AppliedWithSkipped_AppendsSkippedNoteWithoutWarningSuffix()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs"),
                    HotReloadMethodOutcome.Skipped("T.Skip", "reason", "file.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. "
                    + "See Methods for Skipped reasons."));
        }

        /// <summary>
        /// What: an all-AlreadyActive run serializes Kind as AlreadyActive and uses the dedicated
        /// no-change message, without counting those rows in PatchedTotal.
        /// </summary>
        [Test]
        public void BuildApplyResponse_AllAlreadyActive_SetsKindAndDedicatedMessage()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.AlreadyActive("Type.MethodA", "Assets/A.cs"),
                    HotReloadMethodOutcome.AlreadyActive("Type.MethodB", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 2);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.Success, Is.True);
            Assert.That(response.PatchedTotal, Is.EqualTo(0));
            Assert.That(response.Methods.Count, Is.EqualTo(2));
            Assert.That(response.Methods[0].Kind, Is.EqualTo(nameof(HotReloadMethodOutcomeKind.AlreadyActive)));
            Assert.That(response.Methods[1].Kind, Is.EqualTo(nameof(HotReloadMethodOutcomeKind.AlreadyActive)));
            Assert.That(
                response.Methods[0].Reason,
                Is.EqualTo(HotReloadConstants.AlreadyActiveReason));
            Assert.That(
                response.Message,
                Is.EqualTo(string.Format(HotReloadConstants.AlreadyActiveApplyMessageFormat, 2)));
        }

        /// <summary>
        /// What: an AlreadyActive apply row copies the live InvocationCount for a registered
        /// MethodKey so the response does not force testers to run --status.
        /// </summary>
        [Test]
        public void BuildApplyResponse_AlreadyActiveRegisteredKey_CopiesLiveInvocationCount()
        {
            const string methodKey = "HotReloadToolTests.AlreadyActiveCounted.Method()";
            HotReloadInvocationRegistry.Increment(methodKey);
            HotReloadInvocationRegistry.Increment(methodKey);
            HotReloadInvocationRegistry.Increment(methodKey);
            try
            {
                HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.AlreadyActive(methodKey, "Assets/A.cs")
                    },
                    new List<string>(),
                    patchedTotal: 0,
                    activePatchTotal: 1);

                HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

                Assert.That(response.Methods.Count, Is.EqualTo(1));
                Assert.That(response.Methods[0].InvocationCount, Is.EqualTo(3L));
            }
            finally
            {
                HotReloadInvocationRegistry.Remove(methodKey);
            }
        }

        /// <summary>
        /// What: an AlreadyActive apply row for an unregistered MethodKey stays at 0, matching
        /// GetCount's unknown-key behavior.
        /// </summary>
        [Test]
        public void BuildApplyResponse_AlreadyActiveUnregisteredKey_ReportsZero()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.AlreadyActive(
                        "HotReloadToolTests.AlreadyActiveUnknown.Method()",
                        "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 1);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.Methods.Count, Is.EqualTo(1));
            Assert.That(response.Methods[0].InvocationCount, Is.EqualTo(0L));
        }

        /// <summary>
        /// What: a Patched apply row stays at InvocationCount 0 even when the same MethodKey
        /// has a live registry count.
        /// </summary>
        [Test]
        public void BuildApplyResponse_Patched_KeepsInvocationCountZero()
        {
            const string methodKey = "HotReloadToolTests.PatchedCounted.Method()";
            HotReloadInvocationRegistry.Increment(methodKey);
            try
            {
                HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched(methodKey, "Assets/A.cs")
                    },
                    new List<string>(),
                    patchedTotal: 1,
                    activePatchTotal: 1);

                HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

                Assert.That(response.Methods.Count, Is.EqualTo(1));
                Assert.That(response.Methods[0].Kind, Is.EqualTo(nameof(HotReloadMethodOutcomeKind.Patched)));
                Assert.That(response.Methods[0].InvocationCount, Is.EqualTo(0L));
            }
            finally
            {
                HotReloadInvocationRegistry.Remove(methodKey);
            }
        }

        /// <summary>
        /// What: a mixed Skipped+AlreadyActive run uses the no-patch message that names both kinds.
        /// </summary>
        [Test]
        public void BuildApplyResponse_SkippedAndAlreadyActive_UsesSharedNoPatchMessage()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Skipped("T.Skip", "reason", "file.cs"),
                    HotReloadMethodOutcome.AlreadyActive("Type.Method", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 1);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Message,
                Is.EqualTo(HotReloadConstants.NoMethodsPatchedSeeSkippedOrAlreadyActiveMessage));
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
