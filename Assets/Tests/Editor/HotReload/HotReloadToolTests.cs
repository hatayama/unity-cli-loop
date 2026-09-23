using System;
using System.Collections.Generic;
using System.Linq;
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
        // The superseded-signature map is per file generation, and the fixture patch these
        // tests supersede is applied from this path.
        private const string SupersededFixturePath = "Assets/Tests/Fixture.cs";

        private HotReloadPlayModeEntryDropLedgerSessionScope _ledgerSessionScope;

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _ledgerSessionScope = new HotReloadPlayModeEntryDropLedgerSessionScope();
            _scope = new HotReloadDomainTestScope();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            _ledgerSessionScope.Restore();
        }

        [Test]
        public void ToolName_ReturnsHotReload()
        {
            // Verifies the registered CLI command name is hot-reload.
            HotReloadTool tool = new HotReloadTool();
            Assert.That(tool.ToolName, Is.EqualTo("hot-reload"));
        }

        /// <summary>
        /// What: omitting --files without compile snapshots returns the required-files code and recovery actions.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WithoutFilesAndWithoutBaseline_ReturnsValidationFailure()
        {
            IDisposable detectorScope = HotReloadServicesTestScope.BeginWithChangeDetector(
                new HotReloadStubChangeDetector(() =>
                    new HotReloadChangedFileAggregationResult(
                        hasBaseline: false,
                        changedProjectRelativePaths: new List<string>(),
                        scanLimitWarnings: new List<string>())));
            try
            {

                HotReloadTool tool = new HotReloadTool();
                UnityCliLoopToolResponse baseResponse =
                    await tool.ExecuteAsync(new JObject(), CancellationToken.None);
                HotReloadResponse response = baseResponse as HotReloadResponse;

                Assert.That(response, Is.Not.Null);
                Assert.That(response.Success, Is.False);
                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        "No compile snapshots exist yet. Run 'uloop compile' first or pass project-relative "
                        + ".cs paths with --files. Files that have never been compiled are not selected "
                        + "automatically; pass them (and any other path) with --files."));
                Assert.That(response.ErrorCode, Is.EqualTo(HotReloadValidationErrorCodes.FilesRequired));
                Assert.That(
                    response.NextActions,
                    Is.EqualTo(
                        new[]
                        {
                            "Run 'uloop compile' to create source snapshots.",
                            "Pass project-relative .cs paths with --files (required for new files that have "
                            + "not been compiled yet)."
                        }));
            }
            finally
            {
                detectorScope.Dispose();
            }
        }

        /// <summary>
        /// What: a no-changed-files validation failure reports remaining active patches
        /// and points at --status / --revert-all when the ledger is not empty.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_NoChangedFiles_WhenActivePatchExists_ReportsActiveCountAndRecovery()
        {
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            IDisposable detectorScope = HotReloadServicesTestScope.BeginWithChangeDetector(
                new HotReloadStubChangeDetector(CreateNoChangedFilesDetector()));
            try
            {
                ApplyCoreFixtureTransplant(
                    nameof(HotReloadCoreFixture.ReplaceableCompute),
                    BindingFlags.Instance | BindingFlags.Public,
                    nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));

                HotReloadTool tool = new HotReloadTool();
                UnityCliLoopToolResponse baseResponse =
                    await tool.ExecuteAsync(new JObject(), CancellationToken.None);
                HotReloadResponse response = baseResponse as HotReloadResponse;

                Assert.That(response, Is.Not.Null);
                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo(HotReloadValidationErrorCodes.NoChangedFiles));
                Assert.That(response.ActivePatchTotal, Is.EqualTo(1));
                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        "No .cs files changed since the last compile were found. Files that have "
                        + "never been compiled are not selected automatically; pass them (and any "
                        + "other path) with --files. 1 hot-reload change(s) are still active."));
                Assert.That(
                    response.NextActions[response.NextActions.Length - 1],
                    Is.EqualTo(
                        "Run 'uloop hot-reload --status' to inspect the active changes, or 'uloop hot-reload --revert-all' to drop them."));
            }
            finally
            {
                detectorScope.Dispose();
                HotReloadCompositionRoot.Services.Patcher.RevertAll();
            }
        }

        /// <summary>
        /// What: a no-changed-files validation failure keeps the original Message and
        /// NextActions when no hot-reload changes are active.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_NoChangedFiles_WhenNoActivePatches_KeepsOriginalMessageAndNextActions()
        {
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            IDisposable detectorScope = HotReloadServicesTestScope.BeginWithChangeDetector(
                new HotReloadStubChangeDetector(CreateNoChangedFilesDetector()));
            try
            {

                HotReloadTool tool = new HotReloadTool();
                UnityCliLoopToolResponse baseResponse =
                    await tool.ExecuteAsync(new JObject(), CancellationToken.None);
                HotReloadResponse response = baseResponse as HotReloadResponse;

                Assert.That(response, Is.Not.Null);
                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo(HotReloadValidationErrorCodes.NoChangedFiles));
                Assert.That(response.ActivePatchTotal, Is.EqualTo(0));
                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        "No .cs files changed since the last compile were found. Files that have never been "
                        + "compiled are not selected automatically; pass them (and any other path) with "
                        + "--files."));
                Assert.That(
                    response.NextActions,
                    Is.EqualTo(
                        new[]
                        {
                            "Save the edited .cs files to disk, then run 'uloop hot-reload' again.",
                            "Pass project-relative .cs paths with --files (required for new files that have "
                            + "not been compiled yet)."
                        }));
            }
            finally
            {
                detectorScope.Dispose();
                HotReloadCompositionRoot.Services.Patcher.RevertAll();
            }
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
            RegisterAddedMemberForStatus(filePath, methodKey);
            HotReloadResponse response = await ExecuteStatusAsync(CancellationToken.None);
            HotReloadMethodResult addedRow = FindStatusRow(
                response,
                HotReloadConstants.AddedMemberStatusKind,
                methodKey);

            Assert.That(
                addedRow.Reason,
                Is.EqualTo(
                    "Added-member calls are not instrumented, so InvocationCount is always 0 for this row."));
            Assert.That(addedRow.InvocationCount, Is.EqualTo(0L));
        }

        /// <summary>
        /// What: --status Active rows with InvocationCount 0 explain that finished calls do
        /// not re-run, and Message aggregates that count.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Status_NeverInvokedActiveRow_SetsNeverInvokedReason()
        {
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            try
            {
                ApplyCoreFixtureTransplant(
                    nameof(HotReloadCoreFixture.ReplaceableCompute),
                    BindingFlags.Instance | BindingFlags.Public,
                    nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));

                HotReloadResponse response = await ExecuteStatusAsync(CancellationToken.None);
                HotReloadMethodResult activeRow = FindStatusRow(
                    response,
                    "Active",
                    nameof(HotReloadCoreFixture.ReplaceableCompute));

                Assert.That(activeRow.Kind, Is.EqualTo("Active"));
                Assert.That(activeRow.InvocationCount, Is.EqualTo(0L));
                Assert.That(
                    activeRow.Reason,
                    Is.EqualTo(
                        "Not invoked since this patch was applied. Calls that already finished before the patch (for example one-time initialization) do not re-run automatically; the patched body takes effect the next time this method is called. If this method only runs during initialization, trigger that path again — re-create the object that runs it, or run 'uloop compile' and enter Play Mode again."));
                Assert.That(
                    activeRow.Reason,
                    Is.EqualTo(HotReloadConstants.ActivePatchNeverInvokedReason));
                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        "1 change(s) currently active. 1 change(s) have not been invoked since their patch was applied; see Methods[].Reason."));
                Assert.That(response.AutoRefreshHeld, Is.True);
            }
            finally
            {
                HotReloadCompositionRoot.Services.Patcher.RevertAll();
            }
        }

        /// <summary>
        /// What: --status Active rows that have run since the patch leave Reason empty and
        /// keep Message as the active-count sentence only.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Status_InvokedActiveRow_LeavesReasonEmpty()
        {
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            try
            {
                ApplyCoreFixtureTransplant(
                    nameof(HotReloadCoreFixture.ReplaceableCompute),
                    BindingFlags.Instance | BindingFlags.Public,
                    nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));

                HotReloadCoreFixture fixture = new HotReloadCoreFixture();
                Assert.That(fixture.ReplaceableCompute(5), Is.EqualTo(47));

                HotReloadResponse response = await ExecuteStatusAsync(CancellationToken.None);
                HotReloadMethodResult activeRow = FindStatusRow(
                    response,
                    "Active",
                    nameof(HotReloadCoreFixture.ReplaceableCompute));

                Assert.That(activeRow.InvocationCount, Is.GreaterThanOrEqualTo(1L));
                Assert.That(activeRow.Reason, Is.EqualTo(string.Empty));
                Assert.That(
                    response.Message,
                    Is.EqualTo("1 change(s) currently active."));
            }
            finally
            {
                HotReloadCompositionRoot.Services.Patcher.RevertAll();
            }
        }

        /// <summary>
        /// What: --status Message counts never-invoked Active rows only, not added-member
        /// rows, when both kinds are present.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Status_MixedActiveAndAdded_CountsOnlyNeverInvokedActiveInAggregate()
        {
            const string filePath = "Assets/Tests/Editor/HotReload/StatusAddedReason.cs";
            const string methodKey = "Host.NewHelper(System.Int32)";
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            try
            {
                ApplyCoreFixtureTransplant(
                    nameof(HotReloadCoreFixture.ReplaceableCompute),
                    BindingFlags.Instance | BindingFlags.Public,
                    nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));
                ApplyCoreFixtureTransplant(
                    nameof(HotReloadCoreFixture.StaticPing),
                    BindingFlags.Static | BindingFlags.Public,
                    nameof(HotReloadHandwrittenShims.StaticPing__shim0));
                RegisterAddedMemberForStatus(filePath, methodKey);

                HotReloadResponse response = await ExecuteStatusAsync(CancellationToken.None);
                HotReloadMethodResult addedRow = FindStatusRow(
                    response,
                    HotReloadConstants.AddedMemberStatusKind,
                    methodKey);

                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        "3 change(s) currently active. 2 change(s) have not been invoked since their patch was applied; see Methods[].Reason."));
                Assert.That(
                    addedRow.Reason,
                    Is.EqualTo(
                        "Added-member calls are not instrumented, so InvocationCount is always 0 for this row."));
            }
            finally
            {
                HotReloadCompositionRoot.Services.Patcher.RevertAll();
            }
        }

        /// <summary>
        /// What: Revert of one method drops that method's superseded mapping so a later
        /// patch of the same key does not keep a stale Reason.
        /// </summary>
        [Test]
        public void Revert_RemovesSupersededMappingForThatMethod()
        {
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            try
            {
                ApplyCoreFixtureTransplant(
                    nameof(HotReloadCoreFixture.ReplaceableCompute),
                    BindingFlags.Instance | BindingFlags.Public,
                    nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));
                IReadOnlyList<HotReloadActivePatchInfo> patches =
                    HotReloadCompositionRoot.Services.Patcher.DescribeActivePatches();
                Assert.That(patches.Count, Is.EqualTo(1));
                string methodKey = patches[0].MethodKey;
                new HotReloadDomainTestAccess().RecordSupersededSignature(
                    SupersededFixturePath,
                    methodKey,
                    "Host.Replacement()");

                MethodInfo original = typeof(HotReloadCoreFixture).GetMethod(
                    nameof(HotReloadCoreFixture.ReplaceableCompute),
                    BindingFlags.Instance | BindingFlags.Public);
                Assert.That(original, Is.Not.Null);
                Assert.That(
                    HotReloadCompositionRoot.Services.Patcher.Revert(original, out string _),
                    Is.EqualTo(HotReloadRevertOutcome.Reverted));

                bool found = HotReloadCompositionRoot.Services.Domain.TryGetSupersededReplacement(
                    methodKey,
                    out string _);
                Assert.That(found, Is.False);
            }
            finally
            {
                HotReloadCompositionRoot.Services.Patcher.RevertAll();
            }
        }

        /// <summary>
        /// What: --status Active rows prefer the superseded Reason over the never-invoked
        /// Reason when the old compiled signature is recorded.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Status_SupersededActiveRow_SetsSupersededReason()
        {
            const string replacementDisplayName = "Host.Replacement(System.String)";
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            try
            {
                ApplyCoreFixtureTransplant(
                    nameof(HotReloadCoreFixture.ReplaceableCompute),
                    BindingFlags.Instance | BindingFlags.Public,
                    nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));
                IReadOnlyList<HotReloadActivePatchInfo> patches =
                    HotReloadCompositionRoot.Services.Patcher.DescribeActivePatches();
                Assert.That(patches.Count, Is.EqualTo(1));
                new HotReloadDomainTestAccess().RecordSupersededSignature(
                    SupersededFixturePath,
                    patches[0].MethodKey,
                    replacementDisplayName);

                HotReloadResponse response = await ExecuteStatusAsync(CancellationToken.None);
                HotReloadMethodResult activeRow = FindStatusRow(
                    response,
                    "Active",
                    nameof(HotReloadCoreFixture.ReplaceableCompute));

                Assert.That(activeRow.InvocationCount, Is.EqualTo(0L));
                Assert.That(
                    activeRow.Reason,
                    Is.EqualTo(
                        string.Format(
                            HotReloadConstants.ActivePatchSupersededReasonFormat,
                            replacementDisplayName)));
                Assert.That(
                    activeRow.Reason,
                    Is.Not.EqualTo(HotReloadConstants.ActivePatchNeverInvokedReason));
            }
            finally
            {
                HotReloadCompositionRoot.Services.Patcher.RevertAll();
            }
        }

        /// <summary>
        /// What: --status lists live added-field ledger rows as Kind AddedField and reports
        /// AddedFieldTotal without counting those rows in ActivePatchTotal.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Status_RegisteredAddedFields_ListsAddedFieldRowsOutsideActivePatchTotal()
        {
            const string filePath = "Assets/Tests/Editor/HotReload/StatusAddedField.cs";
            const string methodKey = "Host.NewHelper(System.Int32)";
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            try
            {
                ApplyCoreFixtureTransplant(
                    nameof(HotReloadCoreFixture.ReplaceableCompute),
                    BindingFlags.Instance | BindingFlags.Public,
                    nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));
                RegisterAddedMemberForStatus(filePath, methodKey);
                new HotReloadDomainTestAccess().ReplaceAddedFields(
                    filePath,
                    new[] { "Ns.Host.alpha", "Ns.Host.beta" });

                HotReloadResponse response = await ExecuteStatusAsync(CancellationToken.None);
                HotReloadMethodResult alphaRow = FindStatusRow(
                    response,
                    HotReloadConstants.AddedFieldKind,
                    "Ns.Host.alpha");
                HotReloadMethodResult betaRow = FindStatusRow(
                    response,
                    HotReloadConstants.AddedFieldKind,
                    "Ns.Host.beta");

                Assert.That(response.ActivePatchTotal, Is.EqualTo(2));
                Assert.That(response.AddedFieldTotal, Is.EqualTo(2));
                Assert.That(response.Methods.Count, Is.EqualTo(4));
                Assert.That(alphaRow.Method, Is.EqualTo("Ns.Host.alpha"));
                Assert.That(alphaRow.FilePath, Is.EqualTo(filePath));
                Assert.That(alphaRow.Reason, Is.EqualTo(string.Empty));
                Assert.That(betaRow.Method, Is.EqualTo("Ns.Host.beta"));
                Assert.That(betaRow.FilePath, Is.EqualTo(filePath));
            }
            finally
            {
                HotReloadCompositionRoot.Services.Patcher.RevertAll();
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
        /// What: an empty Files list leaves selection to the compile-snapshot default path.
        /// </summary>
        [Test]
        public void ValidateApplyParameters_EmptyFiles_LeavesSelectionToExecuteAsync()
        {
            HotReloadSchema schema = new HotReloadSchema
            {
                Files = Array.Empty<string>()
            };

            HotReloadValidationFailure failure = HotReloadTool.ValidateApplyParameters(schema);

            Assert.That(failure, Is.Null);
        }

        /// <summary>
        /// What: blank path entries return the invalid-files code and recovery actions.
        /// </summary>
        [Test]
        public void ValidateApplyParameters_WhitespaceOnlyPath_ReturnsError()
        {
            HotReloadSchema schema = new HotReloadSchema
            {
                Files = new[] { "   " }
            };

            HotReloadValidationFailure failure = HotReloadTool.ValidateApplyParameters(schema);

            Assert.That(failure.Message, Is.EqualTo("Files must not contain null or empty paths."));
            Assert.That(failure.ErrorCode, Is.EqualTo(HotReloadValidationErrorCodes.InvalidFiles));
            Assert.That(
                failure.NextActions,
                Is.EqualTo(
                    new[]
                    {
                        "Remove null or empty entries from --files.",
                        "Pass project-relative .cs paths with --files."
                    }));
        }

        /// <summary>
        /// What: --status combined with --files returns the status-conflict code and distinct exits.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_StatusWithFiles_ReturnsStructuredValidationFailure()
        {
            HotReloadTool tool = new HotReloadTool();
            JObject parameters = new JObject
            {
                ["Status"] = true,
                ["Files"] = new JArray("Assets/Scripts/Player.cs")
            };

            UnityCliLoopToolResponse baseResponse =
                await tool.ExecuteAsync(parameters, CancellationToken.None);
            HotReloadResponse response = baseResponse as HotReloadResponse;

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Success, Is.False);
            Assert.That(
                response.Message,
                Is.EqualTo("--status cannot be combined with --files or --revert-all."));
            Assert.That(response.ErrorCode, Is.EqualTo(HotReloadValidationErrorCodes.StatusConflict));
            Assert.That(
                response.NextActions,
                Is.EqualTo(
                    new[]
                    {
                        "Run 'uloop hot-reload --status' with no other flags to inspect active patches.",
                        "To apply or revert patches, drop --status and pass --files or --revert-all."
                    }));
        }

        /// <summary>
        /// What: a failed run still summarizes its stale patches, so a failure does not hide the
        /// patches that stayed active behind deleted methods.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithFailureAndStaleOutcome_KeepsStaleSummaryInMessage()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Failed("Type.Broken()", "shim compile failed", "Assets/A.cs"),
                    HotReloadMethodOutcome.Stale("Type.RemovedMethod()", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 1);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.Success, Is.False);
            Assert.That(
                response.Message,
                Is.EqualTo("Hot reload finished with one or more Failed method outcomes. See Methods. Stale=1."));
        }

        /// <summary>
        /// What: a Stale row reaches the public response with its ledger invocation count and is
        /// summarized in Message, so an agent can tell why ActivePatchTotal exceeds the run.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithStaleOutcome_ReportsInvocationCountAndMessageSummary()
        {
            const string staleMethodKey = "Type.RemovedMethod()";
            HotReloadInvocationRegistry.Clear();
            HotReloadInvocationRegistry.Increment(staleMethodKey);
            HotReloadInvocationRegistry.Increment(staleMethodKey);
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.EditedMethod()", "Assets/A.cs"),
                    HotReloadMethodOutcome.Stale(staleMethodKey, "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 2);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            HotReloadMethodResult staleRow = null;
            for (int index = 0; index < response.Methods.Count; index++)
            {
                if (response.Methods[index].Kind == "Stale")
                {
                    staleRow = response.Methods[index];
                }
            }

            Assert.That(staleRow, Is.Not.Null);
            Assert.That(staleRow.InvocationCount, Is.EqualTo(2L));
            Assert.That(staleRow.Reason, Is.EqualTo(HotReloadConstants.StalePatchRemovedFromSourceReason));
            Assert.That(response.Message, Does.Contain("Stale=1"));
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
                    "Partially applied. Fix the failed declarations or methods and rerun, run 'uloop compile' to apply every edit, or run 'uloop hot-reload --revert-all' to discard the applied patches."));
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
                    "Partially applied. Fix the failed declarations or methods and rerun, run 'uloop compile' to apply every edit, or run 'uloop hot-reload --revert-all' to discard the applied patches."));
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
                Is.EqualTo("Fix the failed declarations or methods and rerun, or run 'uloop compile'."));
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
            PausePointSidePortScope pausePointScope = new PausePointSidePortScope();
            pausePointScope.Port.RetargetLineDriftWarnings = () =>
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
                pausePointScope.Dispose();
            }
        }

        /// <summary>
        /// What: BuildApplyResponse drains expired-not-retargeted ids into Warnings.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithExpiredNotRetargetedIds_AddsAggregatedWarning()
        {
            PausePointSidePortScope pausePointScope = new PausePointSidePortScope();
            pausePointScope.Port.ExpiredNotRetargetedMarkerIds = () =>
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
                pausePointScope.Dispose();
            }
        }

        /// <summary>
        /// What: revert-all with an empty ledger reports ClearedCount 0 and AutoRefreshHeld false.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_RevertAllWithNoActivePatches_ReportsClearedCountZero()
        {
            // Verifies --revert-all succeeds with a clear message when the ledger is empty.
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
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
            Assert.That(response.AutoRefreshHeld, Is.False);
            Assert.That(response.Message, Is.EqualTo("No active hot-reload changes to revert."));
        }

        /// <summary>
        /// What: --revert-all after an armed hold reports AutoRefreshHeld false.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_RevertAll_ClearsAutoRefreshHeld()
        {
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            try
            {
                ApplyCoreFixtureTransplant(
                    nameof(HotReloadCoreFixture.ReplaceableCompute),
                    BindingFlags.Instance | BindingFlags.Public,
                    nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));
                HotReloadAutoRefreshHold.SyncToActiveChanges();
                Assert.That(HotReloadAutoRefreshHold.IsHeld, Is.True);

                HotReloadTool tool = new HotReloadTool();
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(
                    new JObject { ["RevertAll"] = true },
                    CancellationToken.None);
                HotReloadResponse response = baseResponse as HotReloadResponse;

                Assert.That(response, Is.Not.Null);
                Assert.That(response.Success, Is.True);
                Assert.That(response.ActivePatchTotal, Is.EqualTo(0));
                Assert.That(response.AutoRefreshHeld, Is.False);
                Assert.That(response.ClearedCount, Is.EqualTo(1));
                Assert.That(
                    response.Message,
                    Is.EqualTo("Reverted all active hot-reload changes."));
            }
            finally
            {
                HotReloadCompositionRoot.Services.Patcher.RevertAll();
                HotReloadAutoRefreshHold.SyncToActiveChanges();
            }
        }

        /// <summary>
        /// What: a newly armed apply response appends the fixed Auto Refresh hold sentence.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WhenHoldNewlyArmed_AppendsFixedHoldSentence()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1,
                autoRefreshHold: new HotReloadAutoRefreshHoldSyncResult(true, true, false),
                autoRefreshHoldNewlyArmed: true);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.AutoRefreshHeld, Is.True);
            Assert.That(
                response.Message,
                Does.EndWith(HotReloadAutoRefreshHoldConstants.NewlyArmedMessageSuffix));
        }

        /// <summary>
        /// What: a deferred release adds the fixed Warning and reports AutoRefreshHeld false.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WhenReleaseDeferred_AddsFixedWarning()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>(),
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0,
                autoRefreshHold: new HotReloadAutoRefreshHoldSyncResult(false, false, true));

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.AutoRefreshHeld, Is.False);
            Assert.That(
                response.Warnings,
                Does.Contain(HotReloadAutoRefreshHoldConstants.ReleaseDeferredWarning));
        }

        /// <summary>
        /// What: two compile-resolvable warnings plus a deferred hold warning still append
        /// the single-compile resolution sentence.
        /// </summary>
        [Test]
        public void BuildApplyResponse_TwoCompileWarningsPlusDeferredHold_KeepsSingleCompileSentence()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                },
                new List<string> { "compile warning one", "compile warning two" },
                patchedTotal: 1,
                activePatchTotal: 1,
                autoRefreshHold: new HotReloadAutoRefreshHoldSyncResult(false, false, true));

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Warnings,
                Is.EqualTo(
                    new[]
                    {
                        "compile warning one",
                        "compile warning two",
                        HotReloadAutoRefreshHoldConstants.ReleaseDeferredWarning
                    }));
            Assert.That(
                response.Message,
                Does.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
        }

        /// <summary>
        /// What: one compile-resolvable warning plus a deferred hold warning counts both lines
        /// but does not append the single-compile resolution sentence, because the hold line
        /// is not a warning a compile clears.
        /// </summary>
        [Test]
        public void BuildApplyResponse_OneCompileWarningPlusDeferredHold_OmitsSingleCompileSentence()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                },
                new List<string> { "compile warning one" },
                patchedTotal: 1,
                activePatchTotal: 1,
                autoRefreshHold: new HotReloadAutoRefreshHoldSyncResult(false, false, true));

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            AssertTwoWarningsWithoutSingleCompileSentence(response);
        }

        /// <summary>
        /// What: one compile-resolvable warning plus a blocked scene Refresh warning counts both
        /// lines but does not append the single-compile resolution sentence.
        /// </summary>
        [Test]
        public void BuildApplyResponse_OneCompileWarningPlusSceneRefreshHold_OmitsSingleCompileSentence()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                },
                new List<string> { "compile warning one" },
                patchedTotal: 1,
                activePatchTotal: 1,
                autoRefreshHold: new HotReloadAutoRefreshHoldSyncResult(
                    true,
                    false,
                    false,
                    HotReloadAutoRefreshHoldConstants.SceneRefreshBlockedWarning));

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Warnings,
                Does.Contain(HotReloadAutoRefreshHoldConstants.SceneRefreshBlockedWarning));
            AssertTwoWarningsWithoutSingleCompileSentence(response);
        }

        /// <summary>
        /// What: a retargeted pause-point warning beside two compile-resolvable warnings keeps
        /// the single-compile resolution sentence off, because a compile does not clear it.
        /// </summary>
        [Test]
        public void BuildApplyResponse_RetargetedPausePointBesideTwoCompileWarnings_OmitsSingleCompileSentence()
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
                    new List<string> { "compile warning one", "compile warning two" },
                    patchedTotal: 1,
                    activePatchTotal: 1,
                    retargetedPausePointIds: new List<string> { id });

                HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

                Assert.That(response.Warnings, Has.Count.EqualTo(3));
                Assert.That(
                    response.Message,
                    Does.Not.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
            }
            finally
            {
                UloopPausePointRegistry.ResetForTests();
            }
        }

        /// <summary>
        /// What: a pause-point line-drift warning beside two compile-resolvable warnings keeps
        /// the single-compile resolution sentence off.
        /// </summary>
        [Test]
        public void BuildApplyResponse_RetargetLineDriftBesideTwoCompileWarnings_OmitsSingleCompileSentence()
        {
            PausePointSidePortScope pausePointScope = new PausePointSidePortScope();
            pausePointScope.Port.RetargetLineDriftWarnings = () =>
                new List<(string, string, string)>
                {
                    ("Assets/Scripts/A.cs:10", "return a;", "return a + 1;")
                };
            try
            {
                HotReloadResponse response = HotReloadTool.BuildApplyResponse(
                    CreatePatchedResultWithTwoCompileWarnings());

                Assert.That(response.Warnings, Has.Count.EqualTo(3));
                Assert.That(
                    response.Message,
                    Does.Not.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
            }
            finally
            {
                pausePointScope.Dispose();
            }
        }

        /// <summary>
        /// What: an expired-not-retargeted pause-point warning beside two compile-resolvable
        /// warnings keeps the single-compile resolution sentence off.
        /// </summary>
        [Test]
        public void BuildApplyResponse_ExpiredNotRetargetedBesideTwoCompileWarnings_OmitsSingleCompileSentence()
        {
            PausePointSidePortScope pausePointScope = new PausePointSidePortScope();
            pausePointScope.Port.ExpiredNotRetargetedMarkerIds = () =>
                new List<string> { "Assets/Scripts/A.cs:10" };
            try
            {
                HotReloadResponse response = HotReloadTool.BuildApplyResponse(
                    CreatePatchedResultWithTwoCompileWarnings());

                Assert.That(response.Warnings, Has.Count.EqualTo(3));
                Assert.That(
                    response.Message,
                    Does.Not.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
            }
            finally
            {
                pausePointScope.Dispose();
            }
        }

        private static HotReloadOrchestratorResult CreatePatchedResultWithTwoCompileWarnings()
        {
            return new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                },
                new List<string> { "compile warning one", "compile warning two" },
                patchedTotal: 1,
                activePatchTotal: 1);
        }

        private static void AssertTwoWarningsWithoutSingleCompileSentence(HotReloadResponse response)
        {
            Assert.That(response.Warnings, Has.Count.EqualTo(2));
            Assert.That(response.Message, Does.Contain("2 warning(s). See Warnings."));
            Assert.That(
                response.Message,
                Does.Not.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
        }

        /// <summary>
        /// What: --status Sync warnings for a deferred release and a blocked scene Refresh
        /// appear on the response.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Status_PropagatesHoldSyncWarnings()
        {
            HotReloadAutoRefreshHoldService previous = HotReloadAutoRefreshHold.OverrideServiceForTesting;
            try
            {
                HotReloadCompositionRoot.Services.Patcher.RevertAll();
                bool held = true;
                bool isPlaying = true;
                bool preflightCanProceed = true;
                HotReloadAutoRefreshHold.OverrideServiceForTesting = new HotReloadAutoRefreshHoldService(
                    () => held,
                    value => held = value,
                    () => true,
                    () => isPlaying,
                    () => { },
                    () => { },
                    () => (preflightCanProceed, string.Empty, Array.Empty<string>()),
                    () => { });

                HotReloadResponse deferred = await ExecuteStatusAsync(CancellationToken.None);
                Assert.That(
                    deferred.Warnings,
                    Does.Contain(HotReloadAutoRefreshHoldConstants.ReleaseDeferredWarning));

                held = true;
                isPlaying = false;
                preflightCanProceed = false;
                HotReloadResponse blocked = await ExecuteStatusAsync(CancellationToken.None);
                Assert.That(
                    blocked.Warnings,
                    Does.Contain(HotReloadAutoRefreshHoldConstants.SceneRefreshBlockedWarning));
            }
            finally
            {
                HotReloadAutoRefreshHold.OverrideServiceForTesting = previous;
                HotReloadCompositionRoot.Services.Patcher.RevertAll();
                HotReloadAutoRefreshHold.SyncToActiveChanges();
            }
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
        /// What: an all-unchanged run that peeled leftover patches reports ClearedCount and
        /// the stale-patch sentence so testers do not read "nothing to patch" as a no-op.
        /// </summary>
        [Test]
        public void BuildApplyResponse_AllUnchangedWithRevertedPatches_SetsClearedCountAndStalePatchMessage()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>(),
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0,
                unchangedTotal: 5,
                revertedUnchangedTotal: 1);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.ClearedCount, Is.EqualTo(1));
            Assert.That(response.Message, Does.Contain("1 stale patch(es) were reverted"));
        }

        /// <summary>
        /// What: an all-unchanged run with no leftover patches keeps ClearedCount 0 and the
        /// historical nothing-to-patch message.
        /// </summary>
        [Test]
        public void BuildApplyResponse_AllUnchangedWithoutRevertedPatches_KeepsHistoricalMessage()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>(),
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0,
                unchangedTotal: 5,
                revertedUnchangedTotal: 0);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.ClearedCount, Is.EqualTo(0));
            Assert.That(
                response.Message,
                Is.EqualTo("All 5 methods are unchanged since the last compile; nothing to patch."));
        }

        /// <summary>
        /// What: a patched run that also peeled unchanged leftovers appends the stale-patch
        /// sentence after the untouched-methods note.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithUnchangedAndRevertedPatches_AppendsStalePatchNote()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1,
                unchangedTotal: 7,
                revertedUnchangedTotal: 2);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.ClearedCount, Is.EqualTo(2));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. "
                    + "7 unchanged methods were left untouched. "
                    + "2 stale patch(es) were reverted so those methods run the compiled IL again."));
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
        /// What: an indirect caller lifecycle note is returned per method and counted in the aggregate.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithIndirectLifecycleNote_ExposesAndAggregatesIt()
        {
            const string indirectNote =
                "SetUp is called only from one-shot lifecycle method(s) (Awake) in the compiled assemblies; "
                + "objects that already ran them will not run the patched body. It takes effect only for "
                + "newly created objects, or run `uloop compile` and re-enter Play Mode.";
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.SetUp", "Assets/A.cs", indirectNote)
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.Methods[0].LifecycleNote, Is.EqualTo(indirectNote));
            Assert.That(
                response.Message,
                Does.Contain("1 patched method(s) have one-shot lifecycle notes"));
        }

        /// <summary>
        /// What: the one-shot count covers patched rows only, and added messages a proxy delivers
        /// are counted in a sentence of their own; a message left to the compiler is in neither.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithForwardedUnityMessages_CountsThemApartFromPatchedNotes()
        {
            const string oneShotNote =
                "Awake is a one-shot lifecycle method; objects that already ran it will not run the "
                + "patched body. It takes effect only for newly created objects.";
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Awake", "Assets/A.cs", oneShotNote),
                    HotReloadMethodOutcome.Added(
                        "Type.Update",
                        "Assets/A.cs",
                        HotReloadUnityMessageNotes.Forwarded),
                    HotReloadMethodOutcome.Added(
                        "Type.Start",
                        "Assets/A.cs",
                        HotReloadUnityMessageNotes.ForwardedStart),
                    HotReloadMethodOutcome.Added(
                        "Type.OnEnable",
                        "Assets/A.cs",
                        HotReloadUnityMessageNotes.NotForwarded)
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Message,
                Does.Contain("1 patched method(s) have one-shot lifecycle notes"));
            Assert.That(
                response.Message,
                Does.Contain("2 added Unity message(s) are delivered by a hot-reload proxy"));
        }

        /// <summary>
        /// What: a run whose only notes are on forwarded added messages says nothing about
        /// patched methods having one-shot notes.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithOnlyForwardedUnityMessages_OmitsThePatchedNoteCount()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Added(
                        "Type.Update",
                        "Assets/A.cs",
                        HotReloadUnityMessageNotes.Forwarded)
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.Message, Does.Not.Contain("one-shot lifecycle notes"));
            Assert.That(
                response.Message,
                Does.Contain("1 added Unity message(s) are delivered by a hot-reload proxy"));
        }

        /// <summary>
        /// What: a skipped-only run reports that nothing from the requested file was applied, and
        /// carries the skipped method in Warnings.
        /// </summary>
        [Test]
        public void BuildApplyResponse_SkippedOnly_SaysNothingFromRequestedFilesApplied()
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

            Assert.That(response.Warnings, Is.EqualTo(new[] { "Skipped T.M: reason" }));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    HotReloadConstants.RequestedFilesAllSkippedMessage
                    + " 1 warning(s). See Warnings."));
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
        /// What: a file-level failure reaches Message even when cascading method failures are
        /// listed before it, so the cause is read before the symptoms.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithFileLevelFailureAfterCascade_LeadsWithTheFileReason()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Failed("T.M", "CS0103: the name 'Added' does not exist", "file.cs"),
                    HotReloadMethodOutcome.Failed(
                        "(file)",
                        "The assembly definition 'Assets/Feature/New.asmdef' changed on disk but is not imported.",
                        "file.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Hot reload finished with one or more Failed method outcomes. First file-level failure: "
                    + "The assembly definition 'Assets/Feature/New.asmdef' changed on disk but is not imported."
                    + " See Methods."));
            Assert.That(response.Methods[0].Method, Is.EqualTo("T.M"));
            Assert.That(response.Methods[1].Method, Is.EqualTo("(file)"));
        }

        /// <summary>
        /// What: several file-level failures report only the first reason plus how many there are,
        /// so a long reason cannot push the rest of Message out of sight.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithSeveralFileLevelFailures_ReportsFirstReasonAndCount()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Failed("(file)", "first reason", "a.cs"),
                    HotReloadMethodOutcome.Failed("(file)", "second reason", "b.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Hot reload finished with one or more Failed method outcomes. "
                    + "First of 2 file-level failures: first reason. See Methods."));
        }

        /// <summary>
        /// What: a multi-line file-level reason (a parse error listing every diagnostic, or a shim
        /// compilation failure with its hint) contributes only its first line to Message; the rest
        /// stays on the Methods row.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WithMultiLineFileLevelReason_PrefixesOnlyItsFirstLine()
        {
            const string multiLineReason =
                "Compilation of the shim assembly failed.\nAssets/A.cs(3,5): error CS0103: unknown\n"
                + "Add the new member with 'uloop compile'.";
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Failed("(file)", multiLineReason, "a.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Hot reload finished with one or more Failed method outcomes. "
                    + "First file-level failure: Compilation of the shim assembly failed. See Methods."));
            Assert.That(response.Methods[0].Reason, Is.EqualTo(multiLineReason));
        }

        /// <summary>
        /// What: every apply-message branch appends the warning-count suffix when Warnings is
        /// non-empty, the applied branch mentions Skipped before that suffix, and a Skipped
        /// method row keeps the single-compile resolution sentence off.
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
                    HotReloadConstants.RequestedFilesAllSkippedMessage
                    + " 2 warning(s). See Warnings."));

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
                    "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. Skipped: 1. "
                    + "3 warning(s). See Warnings."));
        }

        /// <summary>
        /// What: a Failed method row beside two orchestrator warnings keeps the single-compile
        /// resolution sentence off, because that method has to be fixed before the agent goes on.
        /// </summary>
        [Test]
        public void BuildApplyResponse_FailedMethodBesideTwoOrchestratorWarnings_OmitsSingleCompileResolution()
        {
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs"),
                        HotReloadMethodOutcome.Failed("Type.Broken", "reason", "Assets/A.cs")
                    },
                    new List<string> { "warn-a", "warn-b" },
                    patchedTotal: 1,
                    activePatchTotal: 1));

            Assert.That(response.Message, Does.Contain("2 warning(s). See Warnings."));
            Assert.That(
                response.Message,
                Does.Not.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
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
        /// What: a run whose warnings include a type notice saying the type requires a compile does
        /// not say that none of the warnings has to be cleared before continuing.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WarningsIncludeTypeNotice_OmitsSingleCompileResolution()
        {
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                    },
                    new List<string> { "Assets/A.cs: notice", "warn-b" },
                    patchedTotal: 1,
                    activePatchTotal: 1,
                    introducedTypeNoticeCount: 1));

            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. "
                    + "2 warning(s). See Warnings."));
        }

        /// <summary>
        /// What: a run that refused a type declaration does not say that none of its warnings has to
        /// be cleared before continuing, because the refused type exists only after a compile.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WarningsBesideFailedType_OmitsSingleCompileResolution()
        {
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                    },
                    new List<string> { "warn-a", "warn-b" },
                    patchedTotal: 1,
                    activePatchTotal: 1,
                    introducedTypes: new List<HotReloadIntroducedTypeOutcome>
                    {
                        HotReloadIntroducedTypeOutcome.Failed(
                            "Example.RefusedType",
                            "SomeAssembly",
                            "Assets/B.cs",
                            "refused")
                    }));

            Assert.That(response.Message, Does.Not.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
            Assert.That(response.Message, Does.Contain("2 warning(s). See Warnings."));
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
        /// What: after a domain reload discarded an earlier apply, the response asks to wire again
        /// the added fields of the types whose changes this run recovered, and names no other field.
        /// </summary>
        [Test]
        public void Build_RecoveredIdentityOfAddedFieldType_WarnsToWireTheFieldAgain()
        {
            HotReloadResponse response = HotReloadApplyResponseBuilder.Build(
                HotReloadCompositionRoot.Services,
                CreateResultWithAddedFields(),
                Array.Empty<string>(),
                new[] { "Ns.Host.Tick()" });

            string warning = response.Warnings.FirstOrDefault(
                entry => entry.Contains(RewireAfterDomainReloadWarningMarker));
            Assert.That(warning, Is.Not.Null, string.Join(" | ", response.Warnings));
            Assert.That(warning, Does.Contain("Ns.Host.Speed"));
            Assert.That(warning, Does.Not.Contain("Ns.Other.Count"));
        }

        /// <summary>
        /// What: a recovered constructor, whose label ends in "..ctor", still identifies its type,
        /// so the added fields of that type are named.
        /// </summary>
        [Test]
        public void Build_RecoveredConstructorOfAddedFieldType_WarnsToWireTheFieldAgain()
        {
            HotReloadResponse response = HotReloadApplyResponseBuilder.Build(
                HotReloadCompositionRoot.Services,
                CreateResultWithAddedFields(),
                Array.Empty<string>(),
                new[] { "Ns.Host..ctor()" });

            string warning = response.Warnings.FirstOrDefault(
                entry => entry.Contains(RewireAfterDomainReloadWarningMarker));
            Assert.That(warning, Is.Not.Null, string.Join(" | ", response.Warnings));
            Assert.That(warning, Does.Contain("Ns.Host.Speed"));
        }

        /// <summary>
        /// What: a run that recovered nothing a domain reload discarded does not ask to wire added
        /// fields again, because nothing was wired into them before.
        /// </summary>
        [Test]
        public void Build_NoRecoveredIdentity_DoesNotWarnToWireAgain()
        {
            HotReloadResponse response = HotReloadApplyResponseBuilder.Build(
                HotReloadCompositionRoot.Services,
                CreateResultWithAddedFields(),
                Array.Empty<string>(),
                Array.Empty<string>());

            Assert.That(
                response.Warnings.Any(entry => entry.Contains(RewireAfterDomainReloadWarningMarker)),
                Is.False,
                string.Join(" | ", response.Warnings));
        }

        /// <summary>
        /// What: the rewire warning suppresses the single-compile resolution sentence, because a
        /// compile does not bring wired values back, while the same result without a recovered
        /// identity keeps that sentence.
        /// </summary>
        [Test]
        public void Build_RewireWarningBesideTwoOrchestratorWarnings_OmitsSingleCompileResolution()
        {
            HotReloadResponse withRewire = HotReloadApplyResponseBuilder.Build(
                HotReloadCompositionRoot.Services,
                CreatePatchedResultWithTwoWarningsAndAddedField(),
                Array.Empty<string>(),
                new[] { "Ns.Host.Tick()" });
            HotReloadResponse withoutRewire = HotReloadApplyResponseBuilder.Build(
                HotReloadCompositionRoot.Services,
                CreatePatchedResultWithTwoWarningsAndAddedField(),
                Array.Empty<string>(),
                Array.Empty<string>());

            Assert.That(
                withRewire.Message,
                Does.Not.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
            Assert.That(withRewire.Message, Does.Contain("3 warning(s). See Warnings."));
            Assert.That(
                withoutRewire.Message,
                Does.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
        }

        private const string RewireAfterDomainReloadWarningMarker = "wire them again";

        private static HotReloadOrchestratorResult CreatePatchedResultWithTwoWarningsAndAddedField()
        {
            return new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Ns.Host.Tick()", "Assets/Host.cs")
                },
                new List<string> { "warn-a", "warn-b" },
                patchedTotal: 1,
                activePatchTotal: 1,
                addedFields: new[] { "Ns.Host.Speed" });
        }

        private static HotReloadOrchestratorResult CreateResultWithAddedFields()
        {
            return new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Added("Ns.Host.Tick()", "Assets/Host.cs"),
                    HotReloadMethodOutcome.Added("Ns.Other.Run()", "Assets/Other.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 2,
                addedFields: new[] { "Ns.Host.Speed", "Ns.Other.Count" });
        }

        /// <summary>
        /// What: BuildApplyResponse copies the live added-field ledger count onto AddedFieldTotal.
        /// </summary>
        [Test]
        public void BuildApplyResponse_CopiesAddedFieldTotalFromLiveRegistry()
        {
            new HotReloadDomainTestAccess().ReplaceAddedFields(
                "Assets/Tests/Editor/HotReload/ApplyAddedFieldTotal.cs",
                new[] { "Ns.Host.score" });
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                    },
                    new List<string>(),
                    patchedTotal: 1,
                    activePatchTotal: 1));

            Assert.That(response.AddedFieldTotal, Is.EqualTo(1));
            Assert.That(response.ActivePatchTotal, Is.EqualTo(1));
        }

        /// <summary>
        /// What: BuildApplyResponse copies orchestrator AddedConsts onto the public response
        /// and uses an empty array when the result has none.
        /// </summary>
        [Test]
        public void BuildApplyResponse_CopiesAddedConstsOrEmptyArray()
        {
            string[] addedConsts =
            {
                "Ns.Host.AddedTuning"
            };
            HotReloadResponse withConsts = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                    },
                    new List<string>(),
                    patchedTotal: 1,
                    activePatchTotal: 1,
                    addedConsts: addedConsts));
            Assert.That(withConsts.AddedConsts, Is.EqualTo(addedConsts));

            HotReloadResponse withoutConsts = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs")
                    },
                    new List<string>(),
                    patchedTotal: 1,
                    activePatchTotal: 1));
            Assert.That(withoutConsts.AddedConsts, Is.Not.Null);
            Assert.That(withoutConsts.AddedConsts, Is.Empty);
        }

        /// <summary>
        /// What: an applied run with a Skipped outcome and no other warnings lists the skipped
        /// method in Warnings and lets the warning-count suffix point the reader there.
        /// </summary>
        [Test]
        public void BuildApplyResponse_AppliedWithSkipped_ListsSkippedInWarnings()
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

            Assert.That(response.Warnings, Is.EqualTo(new[] { "Skipped T.Skip: reason" }));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. Skipped: 1. "
                    + "1 warning(s). See Warnings."));
        }

        /// <summary>
        /// What: an applied run that also skipped methods counts them right after Added, so the
        /// summary line does not read as if every edit was applied.
        /// </summary>
        [Test]
        public void BuildApplyResponse_AddedAndSkippedOutcomes_CountsSkippedAfterAdded()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Added("Type.AddedPing", "Assets/A.cs"),
                    HotReloadMethodOutcome.Skipped("Type.First", "reason", "Assets/A.cs"),
                    HotReloadMethodOutcome.Skipped("Type.Second", "reason", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 1);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Message,
                Does.StartWith("Hot reload applied. PatchedTotal=0, ActivePatchTotal=1. Added: 1. Skipped: 2."));
        }

        /// <summary>
        /// What: an applied run that re-applied a sibling's earlier changes says how many of the
        /// Patched and Added rows came from those siblings, right after the counts they are part of,
        /// so a reader who edited one method is not left wondering where the rest came from.
        /// </summary>
        [Test]
        public void BuildApplyResponse_SiblingRowsReapplied_SaysHowManyOfTheCountsCameFromSiblings()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Edited", "Assets/Requested.cs"),
                    HotReloadMethodOutcome.Patched("Sibling.Earlier", "Assets/Sibling.cs"),
                    HotReloadMethodOutcome.Added("Sibling.AddedEarlier", "Assets/Sibling.cs"),
                    HotReloadMethodOutcome.Skipped("Sibling.Skip", "reason", "Assets/Sibling.cs")
                },
                new List<string>(),
                patchedTotal: 2,
                activePatchTotal: 2,
                reappliedSiblingPaths: new[] { "Assets/Sibling.cs" });

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Message,
                Does.StartWith(
                    "Hot reload applied. PatchedTotal=2, ActivePatchTotal=2. Added: 1. "
                    + "2 of the patched and added rows re-applied changes from earlier reloads in sibling files. Skipped: 1."));
        }

        /// <summary>
        /// What: every Methods row of a pulled-in sibling file is marked as re-applied from a
        /// sibling whatever its Kind, while rows of the requested file and rows with no file are
        /// not, so a reader can tell which rows this edit did not produce.
        /// </summary>
        [Test]
        public void BuildApplyResponse_SiblingRows_AreMarkedReappliedFromSiblingWhateverTheirKind()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Edited", "Assets/Requested.cs"),
                    HotReloadMethodOutcome.Patched("Sibling.Earlier", "Assets/Sibling.cs"),
                    HotReloadMethodOutcome.Added("Sibling.AddedEarlier", "Assets/Sibling.cs"),
                    HotReloadMethodOutcome.Skipped("Sibling.Skip", "reason", "Assets/Sibling.cs"),
                    HotReloadMethodOutcome.Skipped("Type.NoFile", "reason", string.Empty)
                },
                new List<string>(),
                patchedTotal: 2,
                activePatchTotal: 2,
                reappliedSiblingPaths: new[] { "Assets/Sibling.cs" });

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Methods.Select(row => row.Method + "=" + row.ReappliedFromSibling).ToArray(),
                Is.EqualTo(new[]
                {
                    "Type.Edited=False",
                    "Sibling.Earlier=True",
                    "Sibling.AddedEarlier=True",
                    "Sibling.Skip=True",
                    "Type.NoFile=False"
                }));
        }

        /// <summary>
        /// What: a pulled-in sibling whose rows were all Skipped re-applied nothing, so the message
        /// adds no re-applied count for it.
        /// </summary>
        [Test]
        public void BuildApplyResponse_SiblingRowsAllSkipped_AddsNoReappliedCount()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Edited", "Assets/Requested.cs"),
                    HotReloadMethodOutcome.Skipped("Sibling.Skip", "reason", "Assets/Sibling.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1,
                reappliedSiblingPaths: new[] { "Assets/Sibling.cs" });

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(response.Message, Does.Not.Contain("re-applied changes from earlier reloads"));
        }

        /// <summary>
        /// What: an applied run without any Skipped outcome adds no Skipped-derived warning.
        /// </summary>
        [Test]
        public void BuildApplyResponse_AppliedWithoutSkipped_AddsNoSkippedWarning()
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

            Assert.That(response.Warnings, Is.Empty);
            Assert.That(
                response.Message,
                Is.EqualTo("Hot reload applied. PatchedTotal=1, ActivePatchTotal=1."));
        }

        /// <summary>
        /// What: Skipped outcomes sharing a reason collapse into one Warnings line naming every
        /// method, a reason with a single method keeps its own line, and the lines follow the
        /// order in which their reasons first appear in Methods.
        /// </summary>
        [Test]
        public void BuildApplyResponse_SkippedOutcomes_CollapseIntoOneWarningPerReason()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Skipped("A.Foo()", "first reason", "file.cs"),
                    HotReloadMethodOutcome.Skipped("A.Bar()", "first reason", "file.cs"),
                    HotReloadMethodOutcome.Patched("Type.Method", "Assets/A.cs"),
                    HotReloadMethodOutcome.Skipped("B.Baz()", "second reason", "file.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Warnings,
                Is.EqualTo(
                    new[]
                    {
                        "Skipped 2 methods: first reason (A.Foo(), A.Bar())",
                        "Skipped B.Baz(): second reason"
                    }));
        }

        /// <summary>
        /// What: when more methods share one reason than a warning lists, the line names the first
        /// five and counts the rest instead of spelling every name.
        /// </summary>
        [Test]
        public void BuildApplyResponse_ManySkippedWithOneReason_ListsFiveNamesAndCountsTheRest()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Skipped("A.M1()", "reason", "file.cs"),
                    HotReloadMethodOutcome.Skipped("A.M2()", "reason", "file.cs"),
                    HotReloadMethodOutcome.Skipped("A.M3()", "reason", "file.cs"),
                    HotReloadMethodOutcome.Skipped("A.M4()", "reason", "file.cs"),
                    HotReloadMethodOutcome.Skipped("A.M5()", "reason", "file.cs"),
                    HotReloadMethodOutcome.Skipped("A.M6()", "reason", "file.cs"),
                    HotReloadMethodOutcome.Skipped("A.M7()", "reason", "file.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0);

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            Assert.That(
                response.Warnings,
                Is.EqualTo(
                    new[]
                    {
                        "Skipped 7 methods: reason (A.M1(), A.M2(), A.M3(), A.M4(), A.M5(), +2 more (see Methods))"
                    }));
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

            Assert.That(response.Warnings, Is.EqualTo(new[] { "Skipped T.Skip: reason" }));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    HotReloadConstants.NoMethodsPatchedSeeSkippedOrAlreadyActiveMessage
                    + " 1 warning(s). See Warnings."));
        }

        /// <summary>
        /// What: a run whose requested file was all Skipped reports that nothing from it was
        /// applied, names the sibling re-apply separately, and recommends a compile.
        /// </summary>
        [Test]
        public void BuildApplyResponse_RequestedFileAllSkippedWithSiblingAdded_SaysNothingFromRequestedFilesApplied()
        {
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Skipped("T.M", "reason", "Assets/Requested.cs"),
                        HotReloadMethodOutcome.Added("S.N", "Assets/Sibling.cs")
                    },
                    new List<string>(),
                    patchedTotal: 0,
                    activePatchTotal: 0,
                    reappliedSiblingPaths: new[] { "Assets/Sibling.cs" }));

            Assert.That(response.Success, Is.True);
            Assert.That(
                response.Message,
                Does.StartWith(
                    HotReloadConstants.RequestedFilesAllSkippedMessage
                    + " Also re-applied siblings: Added=1."));
            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(HotReloadConstants.RequestedFilesAllSkippedRecommendedNextAction));
        }

        /// <summary>
        /// What: the same all-Skipped run without a sibling re-apply omits the sibling clause.
        /// </summary>
        [Test]
        public void BuildApplyResponse_RequestedFileAllSkippedWithoutSiblings_OmitsTheSiblingClause()
        {
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Skipped("T.M", "reason", "Assets/Requested.cs")
                    },
                    new List<string>(),
                    patchedTotal: 0,
                    activePatchTotal: 0));

            Assert.That(
                response.Message,
                Does.StartWith(HotReloadConstants.RequestedFilesAllSkippedMessage));
            Assert.That(response.Message, Does.Not.Contain("Also re-applied"));
            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(HotReloadConstants.RequestedFilesAllSkippedRecommendedNextAction));
        }

        /// <summary>
        /// What: a run that bound an introduced type keeps the type message and no next action,
        /// even though the only method outcome of the requested file was Skipped.
        /// </summary>
        [Test]
        public void BuildApplyResponse_RequestedFileAllSkippedBesideABoundIntroducedType_KeepsTheTypeMessage()
        {
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Skipped("T.M", "reason", "Assets/Requested.cs")
                    },
                    new List<string>(),
                    patchedTotal: 0,
                    activePatchTotal: 0,
                    introducedTypes: new[]
                    {
                        HotReloadIntroducedTypeOutcome.AlreadyActive(
                            "Example.Introduced",
                            "RetainedAssembly",
                            "Assets/Requested.cs",
                            bodyEdited: false)
                    }));

            Assert.That(
                response.Message,
                Does.StartWith(
                    string.Format(
                        HotReloadConstants.AlreadyActiveIntroducedTypesOnlyApplyMessageFormat,
                        1)
                    + " Skipped: 1."));
            Assert.That(response.RecommendedNextAction, Is.Empty);
        }

        /// <summary>
        /// What: a run that introduced a type and patched no method still counts the methods it
        /// skipped, so its message does not read as if nothing else was edited.
        /// </summary>
        [Test]
        public void BuildApplyResponse_IntroducedTypeBesideSkippedMethods_CountsTheSkippedMethods()
        {
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(
                new HotReloadOrchestratorResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Skipped("T.First", "reason", "Assets/Requested.cs"),
                        HotReloadMethodOutcome.Skipped("T.Second", "reason", "Assets/Requested.cs")
                    },
                    new List<string>(),
                    patchedTotal: 0,
                    activePatchTotal: 0,
                    introducedTypes: new[]
                    {
                        HotReloadIntroducedTypeOutcome.Introduced(
                            "Example.Introduced",
                            "IntroducedAssembly",
                            "Assets/Requested.cs")
                    }));

            Assert.That(
                response.Message,
                Does.StartWith(
                    string.Format(HotReloadConstants.IntroducedTypesOnlyApplyMessageFormat, 1)
                    + " Skipped: 2."));
        }

        /// <summary>
        /// What: a run that skipped an edit outside Play Mode asks the CLI for a compile and leaves
        /// the run's own next action alone.
        /// </summary>
        [Test]
        public void ApplyCompileFallbackDecision_SkippedInEditMode_RequestsTheCompileAndKeepsTheNextAction()
        {
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(CreateSkippedResult());
            string nextActionBefore = response.RecommendedNextAction;

            HotReloadTool.ApplyCompileFallbackDecision(
                response,
                CreateSkippedResult(),
                HotReloadCompileOnSkip.auto,
                isPlaying: false,
                compileRefusedDuringPlay: false);

            Assert.That(response.CompileFallback, Is.EqualTo("Requested"));
            Assert.That(response.RecommendedNextAction, Is.EqualTo(nextActionBefore));
        }

        /// <summary>
        /// What: the same run during play holds the compile back and appends the reason to the
        /// run's own next action instead of replacing it, because a compile would end the Play
        /// session but the advice about the unapplied edits still applies.
        /// </summary>
        [Test]
        public void ApplyCompileFallbackDecision_SkippedDuringPlay_HoldsTheCompileAndSaysWhy()
        {
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(CreateSkippedResult());
            string nextActionBefore = response.RecommendedNextAction;
            Assert.That(nextActionBefore, Is.Not.Empty);

            HotReloadTool.ApplyCompileFallbackDecision(
                response,
                CreateSkippedResult(),
                HotReloadCompileOnSkip.auto,
                isPlaying: true,
                compileRefusedDuringPlay: false);

            Assert.That(response.CompileFallback, Is.EqualTo("HeldForPlayMode"));
            Assert.That(response.RecommendedNextAction, Does.StartWith(nextActionBefore));
            Assert.That(
                response.RecommendedNextAction,
                Does.EndWith(HotReloadConstants.CompileFallbackHeldForPlayModeRecommendedNextAction));
        }

        /// <summary>
        /// What: a held compile still reports the reason when the run itself recommended nothing,
        /// so the caller is never left without a next action.
        /// </summary>
        [Test]
        public void ApplyCompileFallbackDecision_HeldWithNoExistingNextAction_ReportsOnlyTheReason()
        {
            HotReloadResponse response = new() { RecommendedNextAction = string.Empty };

            HotReloadTool.ApplyCompileFallbackDecision(
                response,
                CreateSkippedResult(),
                HotReloadCompileOnSkip.auto,
                isPlaying: true,
                compileRefusedDuringPlay: false);

            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(HotReloadConstants.CompileFallbackHeldForPlayModeRecommendedNextAction));
        }

        /// <summary>
        /// What: a held compile during play, with the Editor set to refuse compiles until play
        /// ends, points at stopping Play Mode instead of suggesting --compile-on-skip on, which
        /// would run a compile the Editor refuses.
        /// </summary>
        [Test]
        public void ApplyCompileFallbackDecision_HeldDuringPlayWhenTheEditorRefusesCompiles_PointsAtStoppingPlayMode()
        {
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(CreateSkippedResult());
            string nextActionBefore = response.RecommendedNextAction;

            HotReloadTool.ApplyCompileFallbackDecision(
                response,
                CreateSkippedResult(),
                HotReloadCompileOnSkip.auto,
                isPlaying: true,
                compileRefusedDuringPlay: true);

            Assert.That(response.CompileFallback, Is.EqualTo("HeldForPlayMode"));
            Assert.That(response.RecommendedNextAction, Does.StartWith(nextActionBefore));
            Assert.That(response.RecommendedNextAction, Does.Contain("control-play-mode --action Stop"));
            Assert.That(response.RecommendedNextAction, Does.Not.Contain("--compile-on-skip on"));
        }

        /// <summary>
        /// What: --compile-on-skip on during play, with the Editor set to refuse compiles until
        /// play ends, reports the compile as blocked and points at stopping Play Mode, so the
        /// CLI does not run a compile that can only be refused.
        /// </summary>
        [Test]
        public void ApplyCompileFallbackDecision_OnDuringPlayWhenTheEditorRefusesCompiles_ReportsBlocked()
        {
            HotReloadResponse response = new() { RecommendedNextAction = string.Empty };

            HotReloadTool.ApplyCompileFallbackDecision(
                response,
                CreateSkippedResult(),
                HotReloadCompileOnSkip.on,
                isPlaying: true,
                compileRefusedDuringPlay: true);

            Assert.That(response.CompileFallback, Is.EqualTo("BlockedByPlayModeSetting"));
            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(HotReloadConstants.CompileFallbackRefusedDuringPlayRecommendedNextAction));
        }

        /// <summary>
        /// What: --compile-on-skip off reports that the fallback was turned off and leaves the
        /// run's own next action alone.
        /// </summary>
        [Test]
        public void ApplyCompileFallbackDecision_SkippedWithFallbackOff_ReportsDisabled()
        {
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(CreateSkippedResult());
            string nextActionBefore = response.RecommendedNextAction;

            HotReloadTool.ApplyCompileFallbackDecision(
                response,
                CreateSkippedResult(),
                HotReloadCompileOnSkip.off,
                isPlaying: false,
                compileRefusedDuringPlay: false);

            Assert.That(response.CompileFallback, Is.EqualTo("Disabled"));
            Assert.That(response.RecommendedNextAction, Is.EqualTo(nextActionBefore));
        }

        /// <summary>
        /// What: a run that applied every edit needs no compile even during play.
        /// </summary>
        [Test]
        public void ApplyCompileFallbackDecision_EverythingApplied_ReportsNotNeeded()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("A.Foo()", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1);
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);

            HotReloadTool.ApplyCompileFallbackDecision(
                response,
                result,
                HotReloadCompileOnSkip.auto,
                isPlaying: true,
                compileRefusedDuringPlay: false);

            Assert.That(response.CompileFallback, Is.EqualTo("NotNeeded"));
        }

        /// <summary>
        /// What: --status writes the field too, so a caller never has to tell "no compile needed"
        /// from "this package does not report it".
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Status_WritesNotNeededCompileFallback()
        {
            HotReloadResponse response = await ExecuteStatusAsync(CancellationToken.None);

            JObject serialized = JObject.FromObject(response);
            Assert.That(serialized.ContainsKey("CompileFallback"), Is.True);
            Assert.That(serialized["CompileFallback"].ToString(), Is.EqualTo("NotNeeded"));
        }

        /// <summary>
        /// What: a refused parameter combination writes the field too, since no run happened that
        /// could have left an edit unapplied.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_ValidationFailure_WritesNotNeededCompileFallback()
        {
            HotReloadTool tool = new HotReloadTool();
            JObject parameters = new JObject
            {
                ["Status"] = true,
                ["Files"] = new JArray("Assets/Scripts/Player.cs")
            };

            UnityCliLoopToolResponse baseResponse =
                await tool.ExecuteAsync(parameters, CancellationToken.None);
            HotReloadResponse response = baseResponse as HotReloadResponse;

            Assert.That(response, Is.Not.Null);
            JObject serialized = JObject.FromObject(response);
            Assert.That(serialized.ContainsKey("CompileFallback"), Is.True);
            Assert.That(serialized["CompileFallback"].ToString(), Is.EqualTo("NotNeeded"));
        }

        // One skipped method: the smallest run that leaves a requested edit unapplied.
        private static HotReloadOrchestratorResult CreateSkippedResult()
        {
            return new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Skipped("A.Foo()", "reason", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0);
        }

        private static Func<HotReloadChangedFileAggregationResult> CreateNoChangedFilesDetector()
        {
            return () => new HotReloadChangedFileAggregationResult(
                hasBaseline: true,
                changedProjectRelativePaths: new List<string>(),
                scanLimitWarnings: new List<string>());
        }

        // Applies a handwritten transplant to a HotReloadCoreFixture method without invoking it.
        private static void ApplyCoreFixtureTransplant(
            string originalName,
            BindingFlags originalFlags,
            string shimName)
        {
            MethodInfo original = typeof(HotReloadCoreFixture).GetMethod(originalName, originalFlags);
            MethodInfo shim = typeof(HotReloadHandwrittenShims).GetMethod(
                shimName,
                BindingFlags.Static | BindingFlags.Public);
            Assert.That(original, Is.Not.Null);
            Assert.That(shim, Is.Not.Null);

            HotReloadPatchResult applyResult = new HotReloadDomainTestAccess().ApplyPatch(
                original,
                shim,
                HotReloadPatchShape.Transplant,
                "Assets/Tests/Fixture.cs");
            Assert.That(applyResult.Success, Is.True, applyResult.ErrorMessage);
        }

        // Registers one added-member ledger row the same way the added-row status test does.
        private static void RegisterAddedMemberForStatus(string filePath, string methodKey)
        {
            MethodInfo shim = typeof(HotReloadAddedMemberHost).GetMethod(
                nameof(HotReloadAddedMemberHost.ExistingCaller),
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(shim, Is.Not.Null);
            new HotReloadDomainTestAccess().RegisterAddedMember(filePath, methodKey, shim, filePath);
        }

        private static async Task<HotReloadResponse> ExecuteStatusAsync(CancellationToken ct)
        {
            HotReloadTool tool = new HotReloadTool();
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(
                new JObject { ["Status"] = true },
                ct);
            HotReloadResponse response = baseResponse as HotReloadResponse;
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Success, Is.True);
            return response;
        }

        private static HotReloadMethodResult FindStatusRow(
            HotReloadResponse response,
            string kind,
            string methodToken)
        {
            HotReloadMethodResult found = null;
            for (int index = 0; index < response.Methods.Count; index++)
            {
                HotReloadMethodResult row = response.Methods[index];
                if (row.Kind == kind && row.Method.Contains(methodToken))
                {
                    found = row;
                    break;
                }
            }

            Assert.That(found, Is.Not.Null, $"No {kind} row containing '{methodToken}'.");
            return found;
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
