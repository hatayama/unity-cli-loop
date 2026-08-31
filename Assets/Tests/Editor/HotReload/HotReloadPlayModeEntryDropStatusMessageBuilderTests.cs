using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies the --status drop Message is emitted only when no changes are active
    /// and discarded identities remain.
    /// </summary>
    [TestFixture]
    public sealed class HotReloadPlayModeEntryDropStatusMessageBuilderTests
    {
        /// <summary>
        /// What: zero active changes with leftover identities uses the Play-entry discard wording.
        /// </summary>
        [Test]
        public void Build_WhenActiveCountIsZeroAndDroppedCountIsPositive_ReturnsExactDropMessage()
        {
            string message = HotReloadPlayModeEntryDropStatusMessageBuilder.Build(0, 2);

            Assert.That(
                message,
                Is.EqualTo(
                    "0 change(s) currently active. 2 change(s) were discarded by the domain reload when Play Mode was entered — hot-reloaded edits that were never compiled are not in effect. Re-apply 'uloop hot-reload', or edit the files and run 'uloop compile'."));
        }

        /// <summary>
        /// What: active changes keep the existing --status Message path (no drop sentence).
        /// </summary>
        [Test]
        public void Build_WhenActiveCountIsPositive_ReturnsNull()
        {
            string message = HotReloadPlayModeEntryDropStatusMessageBuilder.Build(1, 2);

            Assert.That(message, Is.Null);
        }

        /// <summary>
        /// What: no leftover identities keep the existing --status Message path.
        /// </summary>
        [Test]
        public void Build_WhenDroppedCountIsZero_ReturnsNull()
        {
            string message = HotReloadPlayModeEntryDropStatusMessageBuilder.Build(0, 0);

            Assert.That(message, Is.Null);
        }
    }
}
