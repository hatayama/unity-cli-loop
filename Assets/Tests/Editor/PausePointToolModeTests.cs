using System;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies pause point tool mode parameters and response history mapping.
    /// </summary>
    [TestFixture]
    public sealed class PausePointToolModeTests
    {
        private FakePausePointPauseController _pauseController;

        [SetUp]
        public void SetUp()
        {
            _pauseController = new FakePausePointPauseController();
            UloopPausePointRegistry.ConfigureForTests(_pauseController, () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// Verifies mode and max-history JSON parameters reach the enable response.
        /// </summary>
        [Test]
        public async Task Enable_WhenModeAndMaxHistoryAreProvided_MapsParameters()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["timeoutSeconds"] = 30,
                ["mode"] = UloopPausePointCaptureMode.Continuous,
                ["maxHistory"] = 2
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Mode, Is.EqualTo(UloopPausePointCaptureMode.Continuous));
            Assert.That(response.MaxHistory, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies an unknown mode returns a user-facing validation failure with supported values.
        /// </summary>
        [Test]
        public async Task Enable_WhenModeIsUnknown_ReturnsSupportedModeValidationFailure()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["mode"] = "unknown",
                ["maxHistory"] = 20
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("Mode must be one of: single-shot, continuous, trace."));
        }

        /// <summary>
        /// Verifies an out-of-range max-history value returns a user-facing validation failure.
        /// </summary>
        [Test]
        public async Task Enable_WhenMaxHistoryIsOutOfRange_ReturnsValidationFailure()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["mode"] = UloopPausePointCaptureMode.SingleShot,
                ["maxHistory"] = UloopPausePointRegistry.MaxHistoryLimit + 1
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("MaxHistory must be between 1 and 100."));
        }

        /// <summary>
        /// Verifies a non-positive max-history value returns a validation failure.
        /// </summary>
        [Test]
        public async Task Enable_WhenMaxHistoryIsZero_ReturnsValidationFailure()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["mode"] = UloopPausePointCaptureMode.SingleShot,
                ["maxHistory"] = 0
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("MaxHistory must be between 1 and 100."));
        }

        /// <summary>
        /// Verifies the CLI-only status bridge exposes mode and captured history fields.
        /// </summary>
        [Test]
        public void StatusBridge_WhenContinuousMarkerHasHit_ReturnsHistoryFields()
        {
            UloopPausePointRegistry.Enable("jump", 30, UloopPausePointCaptureMode.Continuous, 20);
            UloopPausePointRegistry.Hit("jump");

            PausePointStatusResponse response = PausePointStatusBridgeCommand.Execute(
                new JObject { ["id"] = "jump" });

            Assert.That(response.Mode, Is.EqualTo(UloopPausePointCaptureMode.Continuous));
            Assert.That(response.MaxHistory, Is.EqualTo(20));
            Assert.That(response.CapturedVariableHistory, Has.Count.EqualTo(1));
            Assert.That(response.HistoryDroppedCount, Is.EqualTo(0));
        }

        private sealed class FakePausePointPauseController : IUloopPausePointPauseController
        {
            public int PauseCount { get; private set; }
            public bool IsPlaying => true;
            public bool IsPaused => PauseCount > 0;

            public void Pause()
            {
                PauseCount++;
            }

            public void Resume()
            {
                // Why zero: Unity's isPaused is a bool; Option B Resume must fully clear pause.
                PauseCount = 0;
            }
        }
    }
}
