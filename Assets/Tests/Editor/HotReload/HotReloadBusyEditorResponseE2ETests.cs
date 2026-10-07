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
    /// Covers the next step a response recommends when the Editor's state stops a run, at the
    /// commit boundary or when the request arrives: a state that ends on its own asks for a wait
    /// and a retry, and one that leaves errors behind asks for a fix.
    /// </summary>
    public class HotReloadBusyEditorResponseE2ETests
    {
        private const string CallerFileName = "HotReloadCrossFileAddedMemberCaller.cs";

        private const string CallerBodyAnchor = "return host.Value();";

        private const string CallerProjectRelativePath = "Assets/Tests/Editor/HotReload/" + CallerFileName;

        private static readonly HotReloadEditorStateSnapshot IdleEditor =
            new HotReloadEditorStateSnapshot(isCompiling: false, isUpdating: false, scriptCompilationFailed: false);

        private static readonly HotReloadEditorStateSnapshot CompilingEditor =
            new HotReloadEditorStateSnapshot(isCompiling: true, isUpdating: false, scriptCompilationFailed: false);

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
        /// What: a compile the Editor starts after the request arrived refuses the run at the commit
        /// boundary, and the response asks for a wait and a retry rather than for a fix.
        /// </summary>
        [Test]
        public async Task Run_WhenTheEditorIsCompilingAtCommit_RecommendsWaitingNotFixing()
        {
            // The first capture is the early check while the file is resolved; the second is the
            // commit boundary, so only the boundary sees the compile.
            int captures = 0;
            HotReloadResponse response = await RunEditingTheCallerBodyAsync(
                () => ++captures == 1 ? IdleEditor : CompilingEditor);

            AssertRefusedAtTheCommitBoundary(response);
            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(HotReloadConstants.EditorNotReadyRecommendedNextAction));
            Assert.That(response.RecommendedNextAction, Does.Not.Contain("Fix the failed declarations"));
            Assert.That(response.RetryAfterEditorReady, Is.True);
        }

        /// <summary>
        /// What: a failed last compile refuses the run at the same boundary, but its errors stay
        /// until the reader fixes them, so the response keeps the fix advice.
        /// </summary>
        [Test]
        public async Task Run_WhenTheLastCompileFailedAtCommit_KeepsTheFixAdvice()
        {
            HotReloadEditorStateSnapshot compileFailed =
                new HotReloadEditorStateSnapshot(isCompiling: false, isUpdating: false, scriptCompilationFailed: true);
            HotReloadResponse response = await RunEditingTheCallerBodyAsync(() => compileFailed);

            AssertRefusedAtTheCommitBoundary(response);
            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(HotReloadConstants.FailedWithNoApplyRecommendedNextAction));
            Assert.That(response.RetryAfterEditorReady, Is.False);
        }

        /// <summary>
        /// What: a compile already running when the request arrives refuses the file before the
        /// transform, and the response asks for a retry once the Editor settles.
        /// </summary>
        [Test]
        public async Task Run_WhenTheEditorIsCompilingWhenTheRequestArrives_RefusesBeforeTheTransformAndAsksForARetry()
        {
            HotReloadResponse response = await RunEditingTheCallerBodyAsync(() => CompilingEditor);

            AssertRefusedBeforeTheTransform(response, HotReloadConstants.EditorCompilingBeforeTransformReason);
        }

        /// <summary>
        /// What: an asset import already running when the request arrives refuses the file before
        /// the transform, and the response asks for a retry once the Editor settles.
        /// </summary>
        [Test]
        public async Task Run_WhenTheEditorIsImportingWhenTheRequestArrives_RefusesBeforeTheTransformAndAsksForARetry()
        {
            HotReloadEditorStateSnapshot importing =
                new HotReloadEditorStateSnapshot(isCompiling: false, isUpdating: true, scriptCompilationFailed: false);
            HotReloadResponse response = await RunEditingTheCallerBodyAsync(() => importing);

            AssertRefusedBeforeTheTransform(response, HotReloadConstants.EditorImportingBeforeTransformReason);
        }

        /// <summary>
        /// What: a file whose patches are already active and whose source is unchanged still reports
        /// AlreadyActive while the Editor compiles, because it has nothing to apply.
        /// </summary>
        [Test]
        public async Task Run_WhenAnUnchangedAppliedFileArrivesWhileTheEditorIsCompiling_StaysAlreadyActive()
        {
            string editedPath = WriteEditedCaller();
            HotReloadStubEditorStateSnapshotCapture capture =
                new HotReloadStubEditorStateSnapshotCapture(() => IdleEditor);

            // One scope for both runs: the second run has to see the applied-source record the first
            // run left in the same domain.
            using (HotReloadServicesTestScope.BeginWithEditorState(capture))
            {
                HotReloadResponse applied = await RunAndBuildAsync(editedPath);
                Assert.That(applied.Outcome, Is.EqualTo("Applied"), "Precondition: the first run applies. " + applied.Message);
                Assert.That(applied.PatchedTotal, Is.GreaterThan(0), "Precondition: the first run patches. " + applied.Message);

                capture.Capture = () => CompilingEditor;
                HotReloadResponse unchanged = await RunAndBuildAsync(editedPath);

                Assert.That(unchanged.Methods.Where(row => row.Kind == "Failed"), Is.Empty, unchanged.Message);
                Assert.That(unchanged.Success, Is.True, unchanged.Message);
                Assert.That(unchanged.Outcome, Is.EqualTo("Applied"), unchanged.Message);
                Assert.That(unchanged.AlreadyActiveTotal, Is.GreaterThan(0), unchanged.Message);
                Assert.That(unchanged.PatchedTotal, Is.EqualTo(0), unchanged.Message);
                Assert.That(unchanged.RetryAfterEditorReady, Is.False);
            }
        }

        // Why a fresh services graph per call: these runs are refused or read only one state
        // sequence, so nothing has to carry over from one run to the next.
        private static async Task<HotReloadResponse> RunEditingTheCallerBodyAsync(
            Func<HotReloadEditorStateSnapshot> editorState)
        {
            string editedPath = WriteEditedCaller();

            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            using (HotReloadServicesTestScope.BeginWithEditorState(
                new HotReloadStubEditorStateSnapshotCapture(editorState)))
            {
                return await RunAndBuildAsync(editedPath);
            }
        }

        private static string WriteEditedCaller()
        {
            string callerSource = File.ReadAllText(FixturePath(CallerFileName));
            Assert.That(callerSource, Does.Contain(CallerBodyAnchor), "Precondition: caller body anchor must exist.");
            return HotReloadTestSourceWriter.WriteEditedSource(
                "BusyEditorResponseCaller.cs",
                callerSource.Replace(CallerBodyAnchor, "return host.Value() + 3;", StringComparison.Ordinal));
        }

        // Runs the caller with the edited copy on the installed services and builds the response
        // the tool would, with the caller's absolute path as the selected file.
        private static async Task<HotReloadResponse> RunAndBuildAsync(string editedPath)
        {
            string[] files = { FixturePath(CallerFileName) };
            HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                files,
                editedPath,
                CancellationToken.None);
            return HotReloadApplyResponseBuilder.Build(
                HotReloadCompositionRoot.Services,
                result,
                null,
                Array.Empty<string>(),
                Array.Empty<HotReloadWiredValueRestoreFailure>(),
                isPlaying: false,
                isPaused: false,
                selectedFiles: files);
        }

        private static void AssertRefusedBeforeTheTransform(HotReloadResponse response, string expectedReason)
        {
            Assert.That(response.PatchedTotal, Is.EqualTo(0), "A refused run must patch nothing. " + response.Message);
            HotReloadMethodResult[] failedRows = response.Methods.Where(row => row.Kind == "Failed").ToArray();
            Assert.That(failedRows, Has.Length.EqualTo(1), response.Message);
            Assert.That(failedRows[0].Reason, Is.EqualTo(expectedReason));
            // Never reaching the boundary is what shows the transform was skipped.
            Assert.That(failedRows[0].Reason, Does.Not.Contain("became busy"));
            Assert.That(response.RetryAfterEditorReady, Is.True);
            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(HotReloadConstants.EditorNotReadyRecommendedNextAction));
            Assert.That(response.SelectedFiles, Is.EqualTo(new[] { CallerProjectRelativePath }));
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
