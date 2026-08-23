using System.Collections.Generic;
using NUnit.Framework;

using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies Play-entry drop identities are stored, unioned, removed, and cleared.
    /// </summary>
    [TestFixture]
    public sealed class HotReloadPlayModeEntryDropLedgerTests
    {
        [SetUp]
        public void SetUp()
        {
            HotReloadPlayModeEntryDropLedger.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadPlayModeEntryDropLedger.Clear();
        }

        /// <summary>
        /// What: Record unions identities and GetIdentities returns them in ordinal order.
        /// </summary>
        [Test]
        public void Record_WhenCalledTwice_UnionsIdentitiesWithoutDuplicates()
        {
            HotReloadPlayModeEntryDropLedger.Record(new[] { "Type.B()", "Type.A()" });
            HotReloadPlayModeEntryDropLedger.Record(new[] { "Type.A()", "Type.C()" });

            IReadOnlyList<string> identities = HotReloadPlayModeEntryDropLedger.GetIdentities();

            Assert.That(identities, Is.EqualTo(new[] { "Type.A()", "Type.B()", "Type.C()" }));
            Assert.That(HotReloadPlayModeEntryDropLedger.Count, Is.EqualTo(3));
            Assert.That(
                SessionState.GetString(HotReloadConstants.PlayModeEntryDropSessionStateKey, string.Empty),
                Is.EqualTo("Type.A()\nType.B()\nType.C()"));
        }

        /// <summary>
        /// What: Remove deletes only the named identities and leaves the rest.
        /// </summary>
        [Test]
        public void Remove_WhenSomeIdentitiesMatch_LeavesTheRest()
        {
            HotReloadPlayModeEntryDropLedger.Record(new[] { "Type.A()", "Type.B()", "Type.C()" });

            HotReloadPlayModeEntryDropLedger.Remove(new[] { "Type.B()", "Type.Missing()" });

            Assert.That(
                HotReloadPlayModeEntryDropLedger.GetIdentities(),
                Is.EqualTo(new[] { "Type.A()", "Type.C()" }));
        }

        /// <summary>
        /// What: Clear empties the SessionState record.
        /// </summary>
        [Test]
        public void Clear_WhenIdentitiesExist_RemovesAll()
        {
            HotReloadPlayModeEntryDropLedger.Record(new[] { "Type.A()" });

            HotReloadPlayModeEntryDropLedger.Clear();

            Assert.That(HotReloadPlayModeEntryDropLedger.Count, Is.EqualTo(0));
            Assert.That(HotReloadPlayModeEntryDropLedger.GetIdentities(), Is.Empty);
            Assert.That(
                SessionState.GetString(HotReloadConstants.PlayModeEntryDropSessionStateKey, string.Empty),
                Is.EqualTo(string.Empty));
        }
    }
}
