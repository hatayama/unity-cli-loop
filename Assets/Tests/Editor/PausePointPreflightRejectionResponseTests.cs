#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture for the structured "this command was refused before it did anything" flag every
    /// tool a pause-point --trigger can dispatch reports. The CLI aborts a wait on that flag, so the
    /// wiring from a rejected preflight to the wire field is what these tests pin.
    /// </summary>
    public class PausePointPreflightRejectionResponseTests
    {
        [Test]
        public void SimulateKeyboardResponse_ByDefault_DoesNotClaimRejectionBeforeExecution()
        {
            // Verifies a failure that is not a preflight rejection leaves the flag false.
            SimulateKeyboardResponse response = new() { Success = false, Message = "Key 'Spacf' is not a known key name." };

            Assert.That(response.RejectedBeforeExecution, Is.False);
        }

        [Test]
        public void SimulateKeyboardResponse_WhenRejectedBeforeExecution_SerializesTheFlag()
        {
            // Verifies simulate-keyboard reports the flag under the exact name the CLI matches on.
            string json = JsonConvert.SerializeObject(
                new SimulateKeyboardResponse { Success = false, RejectedBeforeExecution = true });

            Assert.That(json, Does.Contain("\"RejectedBeforeExecution\":true"));
        }

        [Test]
        public void SimulateMouseInputResponse_WhenRejectedBeforeExecution_SerializesTheFlag()
        {
            // Verifies simulate-mouse-input reports the flag under the exact name the CLI matches on.
            string json = JsonConvert.SerializeObject(
                new SimulateMouseInputResponse { Success = false, RejectedBeforeExecution = true });

            Assert.That(json, Does.Contain("\"RejectedBeforeExecution\":true"));
        }

        [Test]
        public void SimulateMouseUiResponse_WhenRejectedBeforeExecution_SerializesTheFlag()
        {
            // Verifies simulate-mouse-ui reports the flag under the exact name the CLI matches on.
            string json = JsonConvert.SerializeObject(
                new SimulateMouseUiResponse { Success = false, RejectedBeforeExecution = true });

            Assert.That(json, Does.Contain("\"RejectedBeforeExecution\":true"));
        }

        [Test]
        public void ReplayInputResponse_WhenRejectedBeforeExecution_SerializesTheFlag()
        {
            // Verifies replay-input reports the flag under the exact name the CLI matches on.
            string json = JsonConvert.SerializeObject(
                new ReplayInputResponse { Success = false, RejectedBeforeExecution = true });

            Assert.That(json, Does.Contain("\"RejectedBeforeExecution\":true"));
        }

        [Test]
        public void MouseUiPreflightFailure_WhenPreflightRejected_ReportsRejectionBeforeExecution()
        {
            // Verifies simulate-mouse-ui's preflight rejection response sets the flag and names the marker.
            SimulateMouseUiResponse response = MouseUiSimulationResponseFactory.CreatePreflightFailure(
                CreateMouseUiCommand(),
                PlayModeToolPreflightResult.FailureRejectedByPausePoint("PlayMode is paused.", "marker"));

            Assert.That(response.RejectedBeforeExecution, Is.True);
            Assert.That(response.RejectedByActivePausePointId, Is.EqualTo("marker"));
        }

        [Test]
        public void MouseUiFailure_WhenNotAPreflightRejection_DoesNotClaimRejectionBeforeExecution()
        {
            // Verifies a mid-flight simulate-mouse-ui failure leaves the flag false.
            SimulateMouseUiResponse response = MouseUiSimulationResponseFactory.CreateFailure(
                CreateMouseUiCommand(),
                "No EventSystem in the scene.");

            Assert.That(response.RejectedBeforeExecution, Is.False);
        }

#if ULOOP_HAS_INPUT_SYSTEM
        [Test]
        public void KeyboardPreflightFailure_WhenPreflightRejected_ReportsRejectionBeforeExecution()
        {
            // Verifies simulate-keyboard's preflight rejection response sets the flag and names the marker.
            SimulateKeyboardResponse response = KeyboardInputSimulationResponseFactory.PreflightRejectedResult(
                UnityCliLoopKeyboardAction.Press,
                PlayModeToolPreflightResult.FailureRejectedByPausePoint("PlayMode is paused.", "marker"));

            Assert.That(response.RejectedBeforeExecution, Is.True);
            Assert.That(response.RejectedByActivePausePointId, Is.EqualTo("marker"));
            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("PlayMode is paused."));
        }

        [Test]
        public void MouseInputPreflightFailure_WhenPreflightRejected_ReportsRejectionBeforeExecution()
        {
            // Verifies simulate-mouse-input's preflight rejection response sets the flag and names the marker.
            SimulateMouseInputResponse response = MouseInputSimulationResponseFactory.PreflightRejectedResult(
                UnityCliLoopMouseInputAction.Click,
                PlayModeToolPreflightResult.FailureRejectedByPausePoint("PlayMode is paused.", "marker"));

            Assert.That(response.RejectedBeforeExecution, Is.True);
            Assert.That(response.RejectedByActivePausePointId, Is.EqualTo("marker"));
            Assert.That(response.Success, Is.False);
        }

        [Test]
        public void ReplayInputPreflightFailure_WhenPreflightRejected_ReportsRejectionBeforeExecution()
        {
            // Verifies replay-input's preflight rejection response sets the flag and names the marker.
            ReplayInputResponse response = ReplayInputResponseFactory.PreflightRejectedResult(
                ReplayInputAction.Start,
                PlayModeToolPreflightResult.FailureRejectedByPausePoint("PlayMode is paused.", "marker"));

            Assert.That(response.RejectedBeforeExecution, Is.True);
            Assert.That(response.RejectedByActivePausePointId, Is.EqualTo("marker"));
            Assert.That(response.Success, Is.False);
        }
#endif

        [Test]
        public void MouseUiUseCase_WhenPlayModeIsStopped_ReportsRejectionBeforeExecution()
        {
            // Verifies the flag survives the real simulate-mouse-ui entry point, not just the factory:
            // an EditMode run has PlayMode stopped, which is exactly the preflight rejection the CLI aborts on.
            SimulateMouseUiResponse response = RunToCompletion(
                new SimulateMouseUiUseCase().ExecuteAsync(
                    new SimulateMouseUiSchema { Action = UnityCliLoopMouseUiAction.Click },
                    CancellationToken.None));

            Assert.That(response.Success, Is.False);
            Assert.That(response.RejectedBeforeExecution, Is.True);
        }

#if ULOOP_HAS_INPUT_SYSTEM
        [Test]
        public void KeyboardUseCase_WhenPlayModeIsStopped_ReportsRejectionBeforeExecution()
        {
            // Verifies simulate-keyboard's own entry point wires the rejected preflight to the wire flag.
            SimulateKeyboardResponse response = RunToCompletion(
                new SimulateKeyboardUseCase().ExecuteAsync(
                    new SimulateKeyboardSchema { Action = UnityCliLoopKeyboardAction.Press, Key = "Space" },
                    CancellationToken.None));

            Assert.That(response.Success, Is.False);
            Assert.That(response.RejectedBeforeExecution, Is.True);
        }

        [Test]
        public void MouseInputUseCase_WhenPlayModeIsStopped_ReportsRejectionBeforeExecution()
        {
            // Verifies simulate-mouse-input's own entry point wires the rejected preflight to the wire flag.
            SimulateMouseInputResponse response = RunToCompletion(
                new SimulateMouseInputUseCase().ExecuteAsync(
                    new SimulateMouseInputSchema { Action = UnityCliLoopMouseInputAction.Click },
                    CancellationToken.None));

            Assert.That(response.Success, Is.False);
            Assert.That(response.RejectedBeforeExecution, Is.True);
        }

        [Test]
        public void ReplayInputUseCase_WhenPlayModeIsStopped_ReportsRejectionBeforeExecution()
        {
            // Verifies replay-input's own entry point wires the rejected preflight to the wire flag.
            // ExecuteStart is private, so Start is driven through the public ReplayInputAsync.
            ReplayInputResponse response = RunToCompletion(
                new ReplayInputUseCase().ReplayInputAsync(
                    new ReplayInputSchema { Action = ReplayInputAction.Start },
                    CancellationToken.None));

            Assert.That(response.Success, Is.False);
            Assert.That(response.RejectedBeforeExecution, Is.True);
        }
#endif

        // Why not await or block: a preflight rejection is returned before the use case reaches any
        // real await, so the task is already complete. Asserting that instead of blocking keeps a
        // regression from hanging the EditMode suite.
        private static TResponse RunToCompletion<TResponse>(Task<TResponse> execution)
        {
            Assert.That(execution.IsCompleted, Is.True,
                "The use case must reject the preflight before awaiting anything.");
            return execution.Result;
        }

        private static MouseUiSimulationCommand CreateMouseUiCommand()
        {
            (MouseUiSimulationCommand? command, string? errorMessage) =
                MouseUiSimulationCommand.TryFromSchema(new SimulateMouseUiSchema
                {
                    Action = UnityCliLoopMouseUiAction.Click
                });
            Assert.That(errorMessage, Is.Null);
            Assert.That(command, Is.Not.Null);
            return command!;
        }
    }
}
