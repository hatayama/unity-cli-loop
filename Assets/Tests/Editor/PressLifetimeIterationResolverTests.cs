using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests pause-priority decisions for one WaitForPressLifetime loop iteration.
    /// </summary>
    public sealed class PressLifetimeIterationResolverTests
    {
        [Test]
        public void ResolvePostWaitOutcome_WhenFrameWaitObservesPause_ReturnsPausedRegardlessOfDuration()
        {
            // Verifies a pause observed directly by the frame-wait race always wins, even when
            // the requested duration has already elapsed (baseWaitSatisfied=true).
            PressLifetimeIterationDecision decision = PressLifetimeIterationResolver.ResolvePostWaitOutcome(
                frameWaitOutcome: InputSimulationWaitOutcome.Paused,
                baseWaitSatisfied: true,
                isPausedFallback: false);

            Assert.That(decision, Is.EqualTo(PressLifetimeIterationDecision.Paused));
        }

        [Test]
        public void ResolvePostWaitOutcome_WhenFrameWaitObservesPauseDuringEdgeExtension_ReturnsPaused()
        {
            // Verifies a pause takes priority even while still extending the hold for press-edge
            // observation (duration not yet satisfied).
            PressLifetimeIterationDecision decision = PressLifetimeIterationResolver.ResolvePostWaitOutcome(
                frameWaitOutcome: InputSimulationWaitOutcome.Paused,
                baseWaitSatisfied: false,
                isPausedFallback: false);

            Assert.That(decision, Is.EqualTo(PressLifetimeIterationDecision.Paused));
        }

        [Test]
        public void ResolvePostWaitOutcome_WhenTimedOutWithDurationSatisfiedAndPausedFallback_ReturnsPaused()
        {
            // Verifies the defensive re-check: a per-frame-wait timeout that coincided with the
            // duration completing must still report Paused when Unity is actually paused,
            // instead of silently absorbing the pause into a Completed result.
            PressLifetimeIterationDecision decision = PressLifetimeIterationResolver.ResolvePostWaitOutcome(
                frameWaitOutcome: InputSimulationWaitOutcome.TimedOut,
                baseWaitSatisfied: true,
                isPausedFallback: true);

            Assert.That(decision, Is.EqualTo(PressLifetimeIterationDecision.Paused));
        }

        [Test]
        public void ResolvePostWaitOutcome_WhenTimedOutWithDurationSatisfiedAndNotPaused_ReturnsCompleted()
        {
            // Verifies the pre-existing legitimate behavior is preserved: a per-frame-wait
            // timeout after the duration has genuinely elapsed, with no pause involved, still
            // completes the Press/KeyDown successfully.
            PressLifetimeIterationDecision decision = PressLifetimeIterationResolver.ResolvePostWaitOutcome(
                frameWaitOutcome: InputSimulationWaitOutcome.TimedOut,
                baseWaitSatisfied: true,
                isPausedFallback: false);

            Assert.That(decision, Is.EqualTo(PressLifetimeIterationDecision.Completed));
        }

        [Test]
        public void ResolvePostWaitOutcome_WhenTimedOutBeforeDurationSatisfied_ReturnsTimedOut()
        {
            // Verifies a genuine timeout (duration not yet reached) is still reported as TimedOut.
            PressLifetimeIterationDecision decision = PressLifetimeIterationResolver.ResolvePostWaitOutcome(
                frameWaitOutcome: InputSimulationWaitOutcome.TimedOut,
                baseWaitSatisfied: false,
                isPausedFallback: false);

            Assert.That(decision, Is.EqualTo(PressLifetimeIterationDecision.TimedOut));
        }

        [Test]
        public void ResolvePostWaitOutcome_WhenFrameObserved_ReturnsContinue()
        {
            // Verifies a normally observed frame just advances the loop to recheck duration/frame
            // count on the next iteration.
            PressLifetimeIterationDecision decision = PressLifetimeIterationResolver.ResolvePostWaitOutcome(
                frameWaitOutcome: InputSimulationWaitOutcome.Completed,
                baseWaitSatisfied: false,
                isPausedFallback: false);

            Assert.That(decision, Is.EqualTo(PressLifetimeIterationDecision.Continue));
        }
    }
}
