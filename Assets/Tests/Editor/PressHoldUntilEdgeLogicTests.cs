#nullable enable
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Pure unit tests for Press hold-until-edge wait decisions.
    /// </summary>
    public sealed class PressHoldUntilEdgeLogicTests
    {
        [Test]
        public void IsBaseWaitSatisfied_WhenFramesAndDurationMet_ReturnsTrue()
        {
            // Verifies the normal Press wait can finish once min frames and duration both pass.
            bool satisfied = PressHoldUntilEdgeLogic.IsBaseWaitSatisfied(
                observedFrames: 2,
                minimumObservationFrames: 2,
                elapsedSeconds: 0f,
                durationSeconds: 0f);

            Assert.That(satisfied, Is.True);
        }

        [Test]
        public void IsBaseWaitSatisfied_WhenFramesShort_ReturnsFalse()
        {
            // Verifies duration alone is not enough before the minimum observation frames elapse.
            bool satisfied = PressHoldUntilEdgeLogic.IsBaseWaitSatisfied(
                observedFrames: 1,
                minimumObservationFrames: 2,
                elapsedSeconds: 1f,
                durationSeconds: 0f);

            Assert.That(satisfied, Is.False);
        }

        [Test]
        public void ShouldExtendHoldForEdge_WhenEdgeAlreadyObserved_ReturnsFalse()
        {
            // Verifies release proceeds immediately once the gameplay press edge was seen.
            bool shouldExtend = PressHoldUntilEdgeLogic.ShouldExtendHoldForEdge(
                pressEdgeObserved: true,
                baseWaitSatisfied: true,
                elapsedMilliseconds: 10,
                timeoutMilliseconds: 5000);

            Assert.That(shouldExtend, Is.False);
        }

        [Test]
        public void ShouldExtendHoldForEdge_WhenEdgeMissingAndUnderTimeout_ReturnsTrue()
        {
            // Verifies release is delayed until the edge is observed (within the existing timeout budget).
            bool shouldExtend = PressHoldUntilEdgeLogic.ShouldExtendHoldForEdge(
                pressEdgeObserved: false,
                baseWaitSatisfied: true,
                elapsedMilliseconds: 100,
                timeoutMilliseconds: 5000);

            Assert.That(shouldExtend, Is.True);
        }

        [Test]
        public void ShouldExtendHoldForEdge_WhenTimeoutExceeded_ReturnsFalse()
        {
            // Verifies the hold does not block forever when the edge never becomes visible.
            bool shouldExtend = PressHoldUntilEdgeLogic.ShouldExtendHoldForEdge(
                pressEdgeObserved: false,
                baseWaitSatisfied: true,
                elapsedMilliseconds: 5000,
                timeoutMilliseconds: 5000);

            Assert.That(shouldExtend, Is.False);
        }

        [Test]
        public void ShouldExtendHoldForEdge_WhenBaseWaitNotSatisfied_ReturnsFalse()
        {
            // Verifies edge extension only starts after duration + min frames already completed.
            bool shouldExtend = PressHoldUntilEdgeLogic.ShouldExtendHoldForEdge(
                pressEdgeObserved: false,
                baseWaitSatisfied: false,
                elapsedMilliseconds: 0,
                timeoutMilliseconds: 5000);

            Assert.That(shouldExtend, Is.False);
        }

        [Test]
        public void CountExtendedFrames_WhenBeyondBase_ReturnsDelta()
        {
            // Verifies response can report how many frames release was delayed for edge observation.
            int extended = PressHoldUntilEdgeLogic.CountExtendedFrames(
                observedFrames: 5,
                baseSatisfiedFrameCount: 2);

            Assert.That(extended, Is.EqualTo(3));
        }

        [Test]
        public void CountExtendedFrames_WhenNotBeyondBase_ReturnsZero()
        {
            // Verifies no extension is reported when release happened at the normal wait end.
            int extended = PressHoldUntilEdgeLogic.CountExtendedFrames(
                observedFrames: 2,
                baseSatisfiedFrameCount: 2);

            Assert.That(extended, Is.EqualTo(0));
        }
    }
}
