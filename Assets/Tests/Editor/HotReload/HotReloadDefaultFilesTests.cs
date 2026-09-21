using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for defaulting omitted hot-reload files from compile snapshot changes.
    /// </summary>
    public class HotReloadDefaultFilesTests
    {
        // Why isolate the drop ledgers: the live Editor may hold discarded introduced files from a
        // real Play entry, and an omitted --files run would select them next to the stubbed changes.
        private HotReloadPlayModeEntryDropLedgerSessionScope _ledgerSessionScope;

        [SetUp]
        public void SetUp()
        {
            _ledgerSessionScope = new HotReloadPlayModeEntryDropLedgerSessionScope();
        }

        [TearDown]
        public void TearDown()
        {
            _ledgerSessionScope.Restore();
        }

        /// <summary>
        /// What: the installed services select and apply through the production collaborators.
        /// </summary>
        [Test]
        public void InstalledServices_UseProductionCollaborators()
        {
            Assert.That(
                HotReloadCompositionRoot.Services.ChangeDetector,
                Is.TypeOf<HotReloadChangeDetector>());
            Assert.That(
                HotReloadCompositionRoot.Services.Orchestrator,
                Is.TypeOf<HotReloadOrchestrator>());
        }

        /// <summary>
        /// What: omitting --files retains existing warnings, appends selection warnings, and prefixes
        /// the exact selection message.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilesAreOmittedAndChangesExist_AppliesSelectedFilesAndPrefixesMessage()
        {
            using IDisposable detectorScope = HotReloadServicesTestScope.BeginWithChangeDetector(
                new HotReloadStubChangeDetector(() =>
                    new HotReloadChangedFileAggregationResult(
                        hasBaseline: true,
                        changedProjectRelativePaths: new List<string> { "Assets/Selected.cs" },
                        scanLimitWarnings: new List<string> { "scan limit warning" })));
            List<string> appliedFiles = null;
            using IDisposable orchestratorScope = HotReloadServicesTestScope.BeginWithOrchestrator(
                new HotReloadStubOrchestrator((files, ignoredCt) =>
                {
                    appliedFiles = new List<string>(files);
                    return Task.FromResult(
                        new HotReloadOrchestratorResult(
                            new List<HotReloadMethodOutcome>
                            {
                                HotReloadMethodOutcome.Patched("Host.Selected()", "Assets/Selected.cs")
                            },
                            new List<string> { "orchestrator warning" },
                            patchedTotal: 1,
                            activePatchTotal: 1));
                }));

            HotReloadResponse response = await ExecuteAsync(new JObject());

            Assert.That(appliedFiles, Is.EqualTo(new[] { "Assets/Selected.cs" }));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "--files was omitted; 1 changed file(s) since the last compile were selected: Assets/Selected.cs."
                    + " New files that have never been compiled are not selected automatically. "
                    + "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. 2 warning(s). See Warnings. "
                    + "A single 'uloop compile' clears all of them at once when you want them gone; none of them has to be cleared before you keep working."));
            Assert.That(
                response.Warnings,
                Is.EqualTo(new[] { "orchestrator warning", "scan limit warning" }));
        }

        /// <summary>
        /// What: a compile baseline with no changed sources returns the no-changed-files validation failure.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilesAreOmittedAndNoChangesExist_ReturnsNoChangedFilesFailure()
        {
            using IDisposable detectorScope = HotReloadServicesTestScope.BeginWithChangeDetector(
                new HotReloadStubChangeDetector(() =>
                    new HotReloadChangedFileAggregationResult(
                        hasBaseline: true,
                        changedProjectRelativePaths: new List<string>(),
                        scanLimitWarnings: new List<string>())));
            using IDisposable orchestratorScope = HotReloadServicesTestScope.BeginWithOrchestrator(
                new HotReloadStubOrchestrator(FailIfApplyRuns));

            HotReloadResponse response = await ExecuteAsync(new JObject());

            Assert.That(response.Success, Is.False);
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "No .cs files changed since the last compile were found. Files that have never been "
                    + "compiled are not selected automatically; pass them (and any other path) with --files."));
            Assert.That(response.ErrorCode, Is.EqualTo(HotReloadValidationErrorCodes.NoChangedFiles));
            Assert.That(
                response.NextActions,
                Is.EqualTo(
                    new[]
                    {
                        "Save the edited .cs files to disk, then run 'uloop hot-reload' again.",
                        "Pass project-relative .cs paths with --files (required for new files that have not "
                        + "been compiled yet)."
                    }));
        }

        /// <summary>
        /// What: --status and --revert-all bypass changed-source detection when --files is omitted.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenStatusOrRevertAll_IsSet_DoesNotDetectChangedFiles()
        {
            int detectorCallCount = 0;
            using IDisposable detectorScope = HotReloadServicesTestScope.BeginWithChangeDetector(
                new HotReloadStubChangeDetector(() =>
                {
                    detectorCallCount++;
                    return new HotReloadChangedFileAggregationResult(
                        hasBaseline: false,
                        changedProjectRelativePaths: new List<string>(),
                        scanLimitWarnings: new List<string>());
                }));

            HotReloadResponse statusResponse = await ExecuteAsync(new JObject { ["Status"] = true });
            HotReloadResponse revertResponse = await ExecuteAsync(new JObject { ["RevertAll"] = true });

            Assert.That(statusResponse.Success, Is.True);
            Assert.That(revertResponse.Success, Is.True);
            Assert.That(detectorCallCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: an omitted --files run selects the changed files first and then the existing owner
        /// file of an introduced type Play entry discarded, and names both groups in the message.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilesAreOmittedAndPlayEntryDiscardedAnIntroducedFile_SelectsItAfterTheChangedFiles()
        {
            RecordDroppedIntroducedSource(ExistingDroppedSourcePath);
            using IDisposable detectorScope = BeginChangedFiles("Assets/Changed1.cs", "Assets/Changed2.cs");
            List<string> appliedFiles = new List<string>();
            using IDisposable orchestratorScope = BeginRecordingOrchestrator(appliedFiles);

            HotReloadResponse response = await ExecuteAsync(new JObject());

            Assert.That(
                appliedFiles,
                Is.EqualTo(new[] { "Assets/Changed1.cs", "Assets/Changed2.cs", ExistingDroppedSourcePath }));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "--files was omitted; 2 changed file(s) since the last compile were selected: "
                    + "Assets/Changed1.cs, Assets/Changed2.cs. 1 new file(s) declaring a type hot reload "
                    + "introduced were selected again, because entering Play Mode or 'uloop hot-reload "
                    + "--revert-all' dropped what earlier reloads had applied from them: "
                    + ExistingDroppedSourcePath + "."
                    + " Other new files that have never been compiled are not selected automatically. "
                    + AppliedMessageTail));
        }

        /// <summary>
        /// What: a discarded owner file that is also a changed file is selected once, and the message
        /// keeps its wording from before discarded files were selected.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenTheDiscardedFileIsAlsoChanged_SelectsItOnceWithTheUsualMessage()
        {
            RecordDroppedIntroducedSource(ExistingDroppedSourcePath);
            using IDisposable detectorScope = BeginChangedFiles(ExistingDroppedSourcePath);
            List<string> appliedFiles = new List<string>();
            using IDisposable orchestratorScope = BeginRecordingOrchestrator(appliedFiles);

            HotReloadResponse response = await ExecuteAsync(new JObject());

            Assert.That(appliedFiles, Is.EqualTo(new[] { ExistingDroppedSourcePath }));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "--files was omitted; 1 changed file(s) since the last compile were selected: "
                    + ExistingDroppedSourcePath + "."
                    + " New files that have never been compiled are not selected automatically. "
                    + AppliedMessageTail));
        }

        /// <summary>
        /// What: with no changed file, a discarded owner file alone is still applied instead of the
        /// no-changed-files failure.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenNothingChangedButPlayEntryDiscardedAnIntroducedFile_AppliesThatFile()
        {
            RecordDroppedIntroducedSource(ExistingDroppedSourcePath);
            using IDisposable detectorScope = BeginChangedFiles();
            List<string> appliedFiles = new List<string>();
            using IDisposable orchestratorScope = BeginRecordingOrchestrator(appliedFiles);

            HotReloadResponse response = await ExecuteAsync(new JObject());

            Assert.That(appliedFiles, Is.EqualTo(new[] { ExistingDroppedSourcePath }));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "--files was omitted; no file changed since the last compile. 1 new file(s) declaring "
                    + "a type hot reload introduced were selected again, because entering Play Mode or "
                    + "'uloop hot-reload --revert-all' dropped what earlier reloads had applied from them: "
                    + ExistingDroppedSourcePath + "."
                    + " Other new files that have never been compiled are not selected automatically. "
                    + AppliedMessageTail));
        }

        /// <summary>
        /// What: a discarded owner file no longer on disk is not selected.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenTheDiscardedFileIsGoneFromDisk_DoesNotSelectIt()
        {
            string missingPath = "Assets/HotReloadDroppedIntroducedSourceMissing_" + Guid.NewGuid().ToString("N") + ".cs";
            Assert.That(
                File.Exists(ToAbsolutePath(missingPath)),
                Is.False,
                "Precondition: the discarded file must not exist on disk.");
            RecordDroppedIntroducedSource(missingPath);
            using IDisposable detectorScope = BeginChangedFiles("Assets/Changed1.cs");
            List<string> appliedFiles = new List<string>();
            using IDisposable orchestratorScope = BeginRecordingOrchestrator(appliedFiles);

            HotReloadResponse response = await ExecuteAsync(new JObject());

            Assert.That(appliedFiles, Is.EqualTo(new[] { "Assets/Changed1.cs" }));
            Assert.That(response.Message, Does.Not.Contain(missingPath));
        }

        /// <summary>
        /// What: explicit --files are applied as given, without the discarded owner files.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilesArePassed_DoesNotAddDiscardedFiles()
        {
            RecordDroppedIntroducedSource(ExistingDroppedSourcePath);
            using IDisposable detectorScope = BeginChangedFiles("Assets/Changed1.cs");
            List<string> appliedFiles = new List<string>();
            using IDisposable orchestratorScope = BeginRecordingOrchestrator(appliedFiles);

            await ExecuteAsync(new JObject { ["Files"] = new JArray("Assets/Explicit.cs") });

            Assert.That(appliedFiles, Is.EqualTo(new[] { "Assets/Explicit.cs" }));
        }

        /// <summary>
        /// What: without compile snapshots an omitted --files run still fails, even when a discarded
        /// owner file could be selected.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenNoBaselineExists_FailsEvenWithADiscardedFile()
        {
            RecordDroppedIntroducedSource(ExistingDroppedSourcePath);
            using IDisposable detectorScope = HotReloadServicesTestScope.BeginWithChangeDetector(
                new HotReloadStubChangeDetector(() =>
                    new HotReloadChangedFileAggregationResult(
                        hasBaseline: false,
                        changedProjectRelativePaths: new List<string>(),
                        scanLimitWarnings: new List<string>())));
            using IDisposable orchestratorScope = HotReloadServicesTestScope.BeginWithOrchestrator(
                new HotReloadStubOrchestrator(FailIfApplyRuns));

            HotReloadResponse response = await ExecuteAsync(new JObject());

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(HotReloadValidationErrorCodes.FilesRequired));
        }

        private static void RecordDroppedIntroducedSource(string projectRelativePath)
        {
            if (projectRelativePath == ExistingDroppedSourcePath)
            {
                Assert.That(
                    File.Exists(ToAbsolutePath(projectRelativePath)),
                    Is.True,
                    "Precondition: the discarded file must exist on disk.");
            }

            HotReloadPlayModeEntryDropSourceLedger.Record(new[]
            {
                new HotReloadPlayModeEntryDropSource(
                    HotReloadPlayModeEntryDropIdentity.ForType("Fixture.Assembly", "Fixture.Introduced"),
                    projectRelativePath)
            });
        }

        private static string ToAbsolutePath(string projectRelativePath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", projectRelativePath));
        }

        private static IDisposable BeginChangedFiles(params string[] changedPaths)
        {
            return HotReloadServicesTestScope.BeginWithChangeDetector(
                new HotReloadStubChangeDetector(() =>
                    new HotReloadChangedFileAggregationResult(
                        hasBaseline: true,
                        changedProjectRelativePaths: new List<string>(changedPaths),
                        scanLimitWarnings: new List<string>())));
        }

        private static IDisposable BeginRecordingOrchestrator(List<string> appliedFiles)
        {
            return HotReloadServicesTestScope.BeginWithOrchestrator(
                new HotReloadStubOrchestrator((files, ignoredCt) =>
                {
                    appliedFiles.AddRange(files);
                    return Task.FromResult(
                        new HotReloadOrchestratorResult(
                            new List<HotReloadMethodOutcome>
                            {
                                HotReloadMethodOutcome.Patched("Host.Selected()", "Assets/Changed1.cs")
                            },
                            new List<string>(),
                            patchedTotal: 1,
                            activePatchTotal: 1));
                }));
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

        private const string ExistingDroppedSourcePath = "Assets/Tests/Editor/HotReload/HotReloadDefaultFilesTests.cs";

        private const string AppliedMessageTail =
            "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1.";
    }
}
