using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for defaulting omitted hot-reload files from compile snapshot changes.
    /// </summary>
    public class HotReloadDefaultFilesTests
    {
        private Func<HotReloadChangedFileAggregationResult> _previousDetector;
        private Func<IReadOnlyList<string>, CancellationToken, Task<HotReloadOrchestratorResult>> _previousApply;

        [SetUp]
        public void SetUp()
        {
            _previousDetector = HotReloadTool.DetectChangedFilesForTesting;
            _previousApply = HotReloadTool.RunApplyAsyncForTesting;
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadTool.DetectChangedFilesForTesting = _previousDetector;
            HotReloadTool.RunApplyAsyncForTesting = _previousApply;
        }

        /// <summary>
        /// What: omitting --files applies the sorted changed sources and prefixes the exact selection message.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilesAreOmittedAndChangesExist_AppliesSelectedFilesAndPrefixesMessage()
        {
            HotReloadTool.DetectChangedFilesForTesting = () =>
                new HotReloadChangedFileAggregationResult(
                    hasBaseline: true,
                    changedProjectRelativePaths: new List<string> { "Assets/Selected.cs" },
                    scanLimitWarnings: new List<string> { "scan limit warning" });
            List<string> appliedFiles = null;
            HotReloadTool.RunApplyAsyncForTesting = (files, ignoredCt) =>
            {
                appliedFiles = new List<string>(files);
                return Task.FromResult(
                    new HotReloadOrchestratorResult(
                        new List<HotReloadMethodOutcome>
                        {
                            HotReloadMethodOutcome.Patched("Host.Selected()", "Assets/Selected.cs")
                        },
                        new List<string>(),
                        patchedTotal: 1,
                        activePatchTotal: 1));
            };

            HotReloadResponse response = await ExecuteAsync(new JObject());

            Assert.That(appliedFiles, Is.EqualTo(new[] { "Assets/Selected.cs" }));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "--files was omitted; 1 changed file(s) since the last compile were selected: Assets/Selected.cs. "
                    + "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. 1 warning(s). See Warnings."));
            Assert.That(response.Warnings, Is.EqualTo(new[] { "scan limit warning" }));
        }

        /// <summary>
        /// What: a compile baseline with no changed sources returns the no-changed-files validation failure.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilesAreOmittedAndNoChangesExist_ReturnsNoChangedFilesFailure()
        {
            HotReloadTool.DetectChangedFilesForTesting = () =>
                new HotReloadChangedFileAggregationResult(
                    hasBaseline: true,
                    changedProjectRelativePaths: new List<string>(),
                    scanLimitWarnings: new List<string>());
            HotReloadTool.RunApplyAsyncForTesting = FailIfApplyRuns;

            HotReloadResponse response = await ExecuteAsync(new JObject());

            Assert.That(response.Success, Is.False);
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "No .cs files changed since the last compile were found; pass explicit paths with --files."));
            Assert.That(response.ErrorCode, Is.EqualTo(HotReloadValidationErrorCodes.NoChangedFiles));
            Assert.That(
                response.NextActions,
                Is.EqualTo(
                    new[]
                    {
                        "Save the edited .cs files to disk, then run 'uloop hot-reload' again.",
                        "Pass project-relative .cs paths with --files."
                    }));
        }

        /// <summary>
        /// What: --status and --revert-all bypass changed-source detection when --files is omitted.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenStatusOrRevertAll_IsSet_DoesNotDetectChangedFiles()
        {
            int detectorCallCount = 0;
            HotReloadTool.DetectChangedFilesForTesting = () =>
            {
                detectorCallCount++;
                return new HotReloadChangedFileAggregationResult(
                    hasBaseline: false,
                    changedProjectRelativePaths: new List<string>(),
                    scanLimitWarnings: new List<string>());
            };

            HotReloadResponse statusResponse = await ExecuteAsync(new JObject { ["Status"] = true });
            HotReloadResponse revertResponse = await ExecuteAsync(new JObject { ["RevertAll"] = true });

            Assert.That(statusResponse.Success, Is.True);
            Assert.That(revertResponse.Success, Is.True);
            Assert.That(detectorCallCount, Is.EqualTo(0));
        }

        private static Task<HotReloadOrchestratorResult> FailIfApplyRuns(
            IReadOnlyList<string> files,
            CancellationToken ct)
        {
            throw new AssertionException("Apply must not run for a default-file validation failure.");
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
    }
}
