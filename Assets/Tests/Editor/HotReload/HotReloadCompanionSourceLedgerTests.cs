using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the text form the companion ledger is carried across a Domain Reload in.
    /// </summary>
    public class HotReloadCompanionSourceLedgerTests
    {
        /// <summary>
        /// What: what Serialize writes restores to the same files and hashes in a fresh ledger,
        /// replacing whatever that ledger held before.
        /// </summary>
        [Test]
        public void Restore_WhatSerializeWrote_GivesBackTheSameFilesAndHashes()
        {
            HotReloadCompanionSourceLedger written = new HotReloadCompanionSourceLedger();
            written.Record("Assets/B.cs", "hash-b");
            written.Record("Assets/A.cs", "hash-a");
            HotReloadCompanionSourceLedger restored = new HotReloadCompanionSourceLedger();
            restored.Record("Assets/Old.cs", "hash-old");

            restored.Restore(written.Serialize());

            Assert.That(restored.ListPaths(), Is.EquivalentTo(new[] { "Assets/A.cs", "Assets/B.cs" }));
            Assert.That(restored.TryGetHash("Assets/A.cs"), Is.EqualTo("hash-a"));
            Assert.That(restored.TryGetHash("Assets/B.cs"), Is.EqualTo("hash-b"));
        }

        /// <summary>
        /// What: a line without exactly one path and one hash is refused rather than restored,
        /// because a path read from corrupted state would bring back a file nobody gave a reload.
        /// </summary>
        [Test]
        public void Restore_LineWithoutAHash_Throws()
        {
            HotReloadCompanionSourceLedger ledger = new HotReloadCompanionSourceLedger();

            Assert.Throws<InvalidOperationException>(() => ledger.Restore("Assets/A.cs"));
        }
    }
}
