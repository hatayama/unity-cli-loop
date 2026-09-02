using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure coverage for the in-memory superseded-signature ledger.
    /// </summary>
    public class HotReloadSupersededSignatureRegistryTests
    {
        [TearDown]
        public void TearDown()
        {
            HotReloadSupersededSignatureRegistry.ClearAll();
        }

        /// <summary>
        /// What: Record stores a replacement display name retrievable by the old method key.
        /// </summary>
        [Test]
        public void Record_ThenTryGetReplacement_ReturnsStoredDisplayName()
        {
            HotReloadSupersededSignatureRegistry.Record(
                "Old.Key(System.Int32)",
                "New.Key(System.String)");

            bool found = HotReloadSupersededSignatureRegistry.TryGetReplacement(
                "Old.Key(System.Int32)",
                out string replacement);

            Assert.That(found, Is.True);
            Assert.That(replacement, Is.EqualTo("New.Key(System.String)"));
        }

        /// <summary>
        /// What: TryGetReplacement is false when the method key was never recorded.
        /// </summary>
        [Test]
        public void TryGetReplacement_UnknownKey_ReturnsFalse()
        {
            bool found = HotReloadSupersededSignatureRegistry.TryGetReplacement(
                "Missing.Key()",
                out string replacement);

            Assert.That(found, Is.False);
            Assert.That(replacement, Is.Null);
        }

        /// <summary>
        /// What: ClearAll drops every recorded superseded signature.
        /// </summary>
        [Test]
        public void ClearAll_DropsRecordedKeys()
        {
            HotReloadSupersededSignatureRegistry.Record("Old.Key()", "New.Key()");
            HotReloadSupersededSignatureRegistry.ClearAll();

            bool found = HotReloadSupersededSignatureRegistry.TryGetReplacement(
                "Old.Key()",
                out string _);

            Assert.That(found, Is.False);
        }
    }
}
