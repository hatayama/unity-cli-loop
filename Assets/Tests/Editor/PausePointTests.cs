using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies named pause point behavior without pausing the real Unity Editor during tests.
    /// </summary>
    [TestFixture]
    public sealed class PausePointTests
    {
        private DateTime _nowUtc;
        private bool _originalEnterPlayModeOptionsEnabled;
        private EnterPlayModeOptions _originalEnterPlayModeOptions;
        private FakePauseController _pauseController;

        [SetUp]
        public void SetUp()
        {
            _nowUtc = new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc);
            _originalEnterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _originalEnterPlayModeOptions = EditorSettings.enterPlayModeOptions;
            _pauseController = new FakePauseController();
            UloopPausePointRegistry.ConfigureForTests(_pauseController, () => _nowUtc);
        }

        [TearDown]
        public void TearDown()
        {
            EditorSettings.enterPlayModeOptionsEnabled = _originalEnterPlayModeOptionsEnabled;
            EditorSettings.enterPlayModeOptions = _originalEnterPlayModeOptions;
            UloopPausePointRegistry.ResetForTests();
        }

        [Test]
        public void Break_WhenPausePointIsNotArmed_DoesNotPause()
        {
            // Verifies marker calls are no-op until the CLI arms the same id.
            UnityCliLoopDebug.Break("jump");

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(_pauseController.PauseCount, Is.EqualTo(0));
            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.NotArmed));
            Assert.That(snapshot.IsArmed, Is.False);
        }

        [Test]
        public void Break_WhenPausePointIsArmed_RecordsHitAndRequestsPause()
        {
            // Verifies an armed marker hit records state and requests a Unity pause.
            UloopPausePointRegistry.Arm("jump", 30);

            UnityCliLoopDebug.Break("jump");

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");
            Assert.That(_pauseController.PauseCount, Is.EqualTo(1));
            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Hit));
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.IsArmed, Is.False);
            Assert.That(snapshot.IsPaused, Is.True);
            Assert.That(snapshot.HitCount, Is.EqualTo(1));
        }

        [Test]
        public void GetStatus_WhenTimeoutPasses_ExpiresAndDisarms()
        {
            // Verifies timeout disarms the marker before a late hit can pause Unity.
            UloopPausePointRegistry.Arm("jump", 1);
            _nowUtc = _nowUtc.AddSeconds(2);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");
            UnityCliLoopDebug.Break("jump");

            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Expired));
            Assert.That(snapshot.IsArmed, Is.False);
            Assert.That(_pauseController.PauseCount, Is.EqualTo(0));
        }

        [Test]
        public void Clear_WhenPausePointIsArmed_DisarmsWithoutPause()
        {
            // Verifies explicit clear prevents later marker hits from pausing Unity.
            UloopPausePointRegistry.Arm("jump", 30);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Clear("jump");
            UnityCliLoopDebug.Break("jump");

            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Cleared));
            Assert.That(snapshot.IsArmed, Is.False);
            Assert.That(_pauseController.PauseCount, Is.EqualTo(0));
        }

        [Test]
        public void ClearAll_WhenPausePointWasHit_ClearsTerminalStatus()
        {
            // Verifies bulk clear hides stale terminal hit status from future waits.
            UloopPausePointRegistry.Arm("jump", 30);
            UnityCliLoopDebug.Break("jump");

            UloopPausePointClearAllResult result = UloopPausePointRegistry.ClearAll();
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(result.ClearedCount, Is.EqualTo(1));
            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Cleared));
            Assert.That(snapshot.IsHit, Is.False);
        }

        [Test]
        public void BreakMethod_WhenSourceIsScanned_UsesUnityEditorConditionalWithoutDebugBreak()
        {
            // Verifies the public marker follows Unity's conditional call-site removal pattern.
            string sourcePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Packages/src/Runtime/PausePoints/UnityCliLoopDebug.cs");
            string source = File.ReadAllText(sourcePath);

            Assert.That(source, Does.Contain("[Conditional(\"UNITY_EDITOR\")]"));
            Assert.That(source, Does.Contain("public static void Break(string id)"));
            Assert.That(source, Does.Not.Contain("Debug.Break"));
        }

        [Test]
        public async Task Enable_WhenPlayModeInactiveAndDomainReloadEnabled_ReturnsWarning()
        {
            // Verifies PlayMode entry risk is reported only when Domain Reload can clear the marker.
            EditorSettings.enterPlayModeOptionsEnabled = false;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;

            PausePointResponse response = await EnableDebugBreakAsync("jump");

            Assert.That(response.Warning, Does.Contain("Domain Reload is enabled"));
            Assert.That(response.Warning, Does.Contain("keep Domain Reload disabled"));
        }

        [Test]
        public async Task Enable_WhenPlayModeInactiveAndDomainReloadDisabled_ReturnsNoWarning()
        {
            // Verifies the normal no-domain-reload workflow does not suggest re-arming after Play starts.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

            PausePointResponse response = await EnableDebugBreakAsync("dash");

            Assert.That(response.Warning, Is.Empty);
        }

        private static async Task<PausePointResponse> EnableDebugBreakAsync(string id)
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = id,
                ["timeoutSeconds"] = 30
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);
            return response;
        }

        /// <summary>
        /// Test double that records pause requests without mutating Unity Editor state.
        /// </summary>
        private sealed class FakePauseController : IUloopPausePointPauseController
        {
            public bool IsPlaying { get; private set; } = true;
            public bool IsPaused { get; private set; }
            public int PauseCount { get; private set; }

            public void Pause()
            {
                PauseCount++;
                IsPaused = true;
            }
        }
    }
}
