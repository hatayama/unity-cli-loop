using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies CompileController keeps the Play-stop Warning on the delayed status-polling path
    /// when external Scene changes abort compile before Unity starts compiling.
    /// </summary>
    [TestFixture]
    public sealed class CompileControllerPlayModeStopWarningTests
    {
        /// <summary>
        /// What: an external Scene-change refusal stores the received Play-stop Warning for status polling.
        /// </summary>
        [Test]
        public async Task TryCompileAsync_WhenExternalSceneChangeBlocks_PersistsReceivedPlayModeStopWarning()
        {
            const string expectedWarning =
                "Play Mode was active when this compile was requested. The compile stops Play Mode and the domain reload discards the Play session state — re-establish your runtime state before continuing verification.";
            UnityCliLoopCompileResultSessionRepository compileResultSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreateCompileResultSessionRepository();
            UnityCliLoopPendingCompileSessionRepository pendingCompileSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreatePendingCompileSessionRepository();
            UnityCliLoopEditorSessionStateSnapshot originalSnapshot =
                UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot();
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();

            try
            {
                using CompileController controller = new(
                    compileResultSessionRepository,
                    pendingCompileSessionRepository);
                controller.SetResultRecordingContext(
                    CompileResultRecordingContext.Create(
                        new CompileSchema
                        {
                            WaitForDomainReload = true,
                            RequestId = "compile_scene_change_play_stop_warning",
                            ForceRecompile = false
                        }));
                controller.SetExternalSceneChangeResolutionForTesting(_ => (
                    false,
                    "Open Scene files have changed externally and compile stopped.",
                    new[] { "Assets/Scenes/Sample.unity" }));

                await controller.TryCompileAsync(
                    forceRecompile: false,
                    expectedWarning,
                    CancellationToken.None);

                UnityCliLoopStoredCompileResult storedResult =
                    compileResultSessionRepository.GetCompileResult("compile_scene_change_play_stop_warning");
                CompileResponse storedResponse = JsonConvert.DeserializeObject<CompileResponse>(
                    storedResult.ResultJson,
                    UnityCliLoopJsonResponseSerializerSettings.Settings);

                Assert.That(storedResult.HasResult, Is.True);
                Assert.That(storedResponse.Warning, Is.EqualTo(expectedWarning));
            }
            finally
            {
                originalSnapshot.Restore();
            }
        }
    }
}
