using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies the owner files of introduced types discarded by Play entry are stored per type,
    /// listed once per path, and forgotten when their types are removed or the ledger is cleared.
    /// </summary>
    [TestFixture]
    public sealed class HotReloadPlayModeEntryDropSourceLedgerTests
    {
        private HotReloadPlayModeEntryDropLedgerSessionScope _ledgerSessionScope;

        [SetUp]
        public void SetUp()
        {
            _ledgerSessionScope = new HotReloadPlayModeEntryDropLedgerSessionScope();
        }

        [TearDown]
        public void TearDown()
        {
            _ledgerSessionScope.Restore();
        }

        /// <summary>
        /// What: recorded owner paths are listed once each in ordinal order, across Record calls.
        /// </summary>
        [Test]
        public void GetProjectRelativePaths_AfterRecords_ReturnsDistinctPathsInOrdinalOrder()
        {
            HotReloadPlayModeEntryDropSourceLedger.Record(new[]
            {
                new HotReloadPlayModeEntryDropSource("type:A|T2", "Assets/B.cs"),
                new HotReloadPlayModeEntryDropSource("type:A|T1", "Assets/A.cs")
            });
            HotReloadPlayModeEntryDropSourceLedger.Record(new[]
            {
                new HotReloadPlayModeEntryDropSource("type:A|T3", "Assets/B.cs")
            });

            Assert.That(
                HotReloadPlayModeEntryDropSourceLedger.GetProjectRelativePaths(),
                Is.EqualTo(new[] { "Assets/A.cs", "Assets/B.cs" }));
        }

        /// <summary>
        /// What: a file that owns two discarded types stays listed until both types are removed.
        /// </summary>
        [Test]
        public void Remove_WhenOnlyOneOfTwoTypesOfAFileIsRemoved_KeepsThePathUntilTheSecond()
        {
            HotReloadPlayModeEntryDropSourceLedger.Record(new[]
            {
                new HotReloadPlayModeEntryDropSource("type:A|T1", "Assets/A.cs"),
                new HotReloadPlayModeEntryDropSource("type:A|T2", "Assets/A.cs")
            });

            HotReloadPlayModeEntryDropSourceLedger.Remove(new[] { "type:A|T1" });

            Assert.That(
                HotReloadPlayModeEntryDropSourceLedger.GetProjectRelativePaths(),
                Is.EqualTo(new[] { "Assets/A.cs" }));

            HotReloadPlayModeEntryDropSourceLedger.Remove(new[] { "type:A|T2" });

            Assert.That(HotReloadPlayModeEntryDropSourceLedger.GetProjectRelativePaths(), Is.Empty);
        }

        /// <summary>
        /// What: Clear forgets every recorded owner path.
        /// </summary>
        [Test]
        public void Clear_ForgetsEveryPath()
        {
            HotReloadPlayModeEntryDropSourceLedger.Record(new[]
            {
                new HotReloadPlayModeEntryDropSource("type:A|T1", "Assets/A.cs")
            });

            HotReloadPlayModeEntryDropSourceLedger.Clear();

            Assert.That(HotReloadPlayModeEntryDropSourceLedger.GetProjectRelativePaths(), Is.Empty);
        }

        /// <summary>
        /// What: a source without an identity or a path, or with a separator character in either,
        /// is rejected, because the stored line could not be read back as one identity and one path.
        /// </summary>
        [TestCase("", "Assets/A.cs")]
        [TestCase(null, "Assets/A.cs")]
        [TestCase("type:A|T1", "")]
        [TestCase("type:A|T1", null)]
        [TestCase("type:A|T1", "Assets/A\t.cs")]
        [TestCase("type:A|T1", "Assets/A\n.cs")]
        [TestCase("type:A|\tT1", "Assets/A.cs")]
        [TestCase("type:A|\nT1", "Assets/A.cs")]
        public void Constructor_WithAnEmptyOrSeparatorBearingPart_Throws(string identity, string path)
        {
            Assert.Throws<ArgumentException>(() => new HotReloadPlayModeEntryDropSource(identity, path));
        }
    }
}
