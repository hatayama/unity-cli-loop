using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the next step a response recommends when the Editor's state stops a run at the
    /// commit boundary: a state that ends on its own asks for a wait, and one that leaves errors
    /// behind asks for a fix.
    /// </summary>
    public class HotReloadBusyEditorResponseE2ETests
    {
        private const string CallerFileName = "HotReloadCrossFileAddedMemberCaller.cs";

        private const string CallerBodyAnchor = "return host.Value();";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        /// <summary>
        /// What: a compile the Editor is running at the commit boundary refuses the run, and the
        /// response asks the reader to wait and rerun rather than to fix anything.
        /// </summary>
        [Test]
        public async Task Run_WhenTheEditorIsCompilingAtCommit_RecommendsWaitingNotFixing()
        {
            HotReloadResponse response = await RunEditingTheCallerBodyAsync(
                new HotReloadEditorStateSnapshot(isCompiling: true, isUpdating: false, scriptCompilationFailed: false));

            AssertRefusedAtTheCommitBoundary(response);
            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(HotReloadConstants.EditorNotReadyRecommendedNextAction));
            Assert.That(response.RecommendedNextAction, Does.Not.Contain("Fix the failed declarations"));
        }

        /// <summary>
        /// What: a failed last compile refuses the run at the same boundary, but its errors stay
        /// until the reader fixes them, so the response keeps the fix advice.
        /// </summary>
        [Test]
        public async Task Run_WhenTheLastCompileFailedAtCommit_KeepsTheFixAdvice()
        {
            HotReloadResponse response = await RunEditingTheCallerBodyAsync(
                new HotReloadEditorStateSnapshot(isCompiling: false, isUpdating: false, scriptCompilationFailed: true));

            AssertRefusedAtTheCommitBoundary(response);
            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(HotReloadConstants.FailedWithNoApplyRecommendedNextAction));
        }

        // Why a state fixed for the whole run: a run that edits an existing file reads the Editor
        // state only at the commit boundary, so the boundary is what sees this state.
        private static async Task<HotReloadResponse> RunEditingTheCallerBodyAsync(
            HotReloadEditorStateSnapshot editorState)
        {
            string callerPath = FixturePath(CallerFileName);
            string callerSource = File.ReadAllText(callerPath);
            Assert.That(callerSource, Does.Contain(CallerBodyAnchor), "Precondition: caller body anchor must exist.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "BusyEditorResponseCaller.cs",
                callerSource.Replace(CallerBodyAnchor, "return host.Value() + 3;", StringComparison.Ordinal));

            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            using (HotReloadServicesTestScope.BeginWithEditorState(
                new HotReloadStubEditorStateSnapshotCapture(() => editorState)))
            {
                HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                    new[] { callerPath },
                    editedPath,
                    CancellationToken.None);
                return HotReloadApplyResponseBuilder.Build(
                    HotReloadCompositionRoot.Services,
                    result,
                    null,
                    Array.Empty<string>(),
                    Array.Empty<HotReloadWiredValueRestoreFailure>(),
                    isPlaying: false,
                    isPaused: false);
            }
        }

        private static void AssertRefusedAtTheCommitBoundary(HotReloadResponse response)
        {
            Assert.That(response.PatchedTotal, Is.EqualTo(0), "A refused run must patch nothing. " + response.Message);
            HotReloadMethodResult failedRow = response.Methods.FirstOrDefault(row => row.Kind == "Failed");
            Assert.That(failedRow, Is.Not.Null, "A refused run must report a failed row. " + response.Message);
            Assert.That(failedRow.Reason, Does.Contain("became busy"));
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }
    }
}
