#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using NUnit.Framework;
using UnityEngine.InputSystem.LowLevel;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Verifies deferred latch-sync fire and one-shot consume rules independently of Input System callbacks.
    /// </summary>
    public sealed class DeferredPlayerLatchSyncDecisionTests
    {
        /// <summary>
        /// Verifies a Dynamic player update both runs the sync and consumes the one-shot registration.
        /// </summary>
        [Test]
        public void Decide_WhenDynamic_RunsSyncAndUnsubscribes()
        {
            DeferredLatchSyncTickDecision decision =
                DeferredPlayerLatchSyncDecision.Decide(InputUpdateType.Dynamic);

            Assert.That(decision.ShouldSync, Is.True);
            Assert.That(decision.ShouldUnsubscribe, Is.True);
        }

        /// <summary>
        /// Verifies a Fixed player update both runs the sync and consumes the one-shot registration.
        /// </summary>
        [Test]
        public void Decide_WhenFixed_RunsSyncAndUnsubscribes()
        {
            DeferredLatchSyncTickDecision decision =
                DeferredPlayerLatchSyncDecision.Decide(InputUpdateType.Fixed);

            Assert.That(decision.ShouldSync, Is.True);
            Assert.That(decision.ShouldUnsubscribe, Is.True);
        }

        /// <summary>
        /// Verifies a Manual player update both runs the sync and consumes the one-shot registration.
        /// </summary>
        [Test]
        public void Decide_WhenManual_RunsSyncAndUnsubscribes()
        {
            DeferredLatchSyncTickDecision decision =
                DeferredPlayerLatchSyncDecision.Decide(InputUpdateType.Manual);

            Assert.That(decision.ShouldSync, Is.True);
            Assert.That(decision.ShouldUnsubscribe, Is.True);
        }

        /// <summary>
        /// Verifies an Editor update neither syncs nor consumes the registration, so a paused
        /// PlayMode session still syncs on the first player update after resume.
        /// </summary>
        [Test]
        public void Decide_WhenEditor_DoesNotSyncAndDoesNotUnsubscribe()
        {
            DeferredLatchSyncTickDecision decision =
                DeferredPlayerLatchSyncDecision.Decide(InputUpdateType.Editor);

            Assert.That(decision.ShouldSync, Is.False);
            Assert.That(decision.ShouldUnsubscribe, Is.False);
        }

        /// <summary>
        /// Verifies the combined Default mask does not count as a player update. Why not HasFlag:
        /// Default includes Editor, and treating it as fireable would consume the one-shot during
        /// editor-only pause updates.
        /// </summary>
        [Test]
        public void Decide_WhenDefaultMask_DoesNotSyncAndDoesNotUnsubscribe()
        {
            DeferredLatchSyncTickDecision decision =
                DeferredPlayerLatchSyncDecision.Decide(InputUpdateType.Default);

            Assert.That(decision.ShouldSync, Is.False);
            Assert.That(decision.ShouldUnsubscribe, Is.False);
        }
    }
}
#endif
