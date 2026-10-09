using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Contract of the ledger of assemblies the hot reload runs of a project edited: most recent
    /// first, no duplicates, capped, and written only when its content changes.
    /// </summary>
    public sealed class HotReloadWarmUpTargetLedgerTests
    {
        private const string Header = "uloop-hot-reload-warm-up-targets 1";

        // Why a fixed past time: a write within the file system's timestamp resolution of the
        // previous one could leave the write time unchanged and hide a rewrite.
        private static readonly DateTime PinnedWriteTimeUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "uloop-warm-up-ledger-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, true);
            }
        }

        /// <summary>
        /// What: the first record creates the file with the header and the names in the given order.
        /// </summary>
        [Test]
        public void Record_WithoutAFile_WritesTheHeaderAndTheNames()
        {
            HotReloadWarmUpTargetLedger.Record(_projectRoot, new[] { "A", "B" });

            Assert.That(File.ReadAllText(LedgerPath(), Encoding.UTF8), Is.EqualTo(Header + "\nA\nB\n"));
        }

        /// <summary>
        /// What: a later record puts its names first and keeps the earlier names after them, once each.
        /// </summary>
        [Test]
        public void Record_Again_PutsTheNewNamesFirstAndKeepsTheRestWithoutDuplicates()
        {
            HotReloadWarmUpTargetLedger.Record(_projectRoot, new[] { "A", "B", "C" });

            HotReloadWarmUpTargetLedger.Record(_projectRoot, new[] { "C", "D", "C" });

            Assert.That(HotReloadWarmUpTargetLedger.Read(_projectRoot), Is.EqualTo(new[] { "C", "D", "A", "B" }));
        }

        /// <summary>
        /// What: the ledger keeps at most the 32 most recent names.
        /// </summary>
        [Test]
        public void Record_MoreThanTheCap_KeepsTheFirst32()
        {
            List<string> names = new List<string>();
            for (int index = 0; index < 40; index++)
            {
                names.Add("Assembly" + index);
            }

            HotReloadWarmUpTargetLedger.Record(_projectRoot, names);

            Assert.That(HotReloadWarmUpTargetLedger.Read(_projectRoot), Is.EqualTo(names.GetRange(0, 32)));
        }

        /// <summary>
        /// What: a record that leaves the content as it is does not rewrite the file.
        /// </summary>
        [Test]
        public void Record_WithTheSameContent_DoesNotRewriteTheFile()
        {
            HotReloadWarmUpTargetLedger.Record(_projectRoot, new[] { "A", "B" });
            File.SetLastWriteTimeUtc(LedgerPath(), PinnedWriteTimeUtc);

            HotReloadWarmUpTargetLedger.Record(_projectRoot, new[] { "A" });

            Assert.That(File.GetLastWriteTimeUtc(LedgerPath()), Is.EqualTo(PinnedWriteTimeUtc));
        }

        /// <summary>
        /// What: an empty record writes nothing.
        /// </summary>
        [Test]
        public void Record_WithNoNames_WritesNothing()
        {
            HotReloadWarmUpTargetLedger.Record(_projectRoot, Array.Empty<string>());

            Assert.That(File.Exists(LedgerPath()), Is.False);
        }

        /// <summary>
        /// What: a project without a ledger reads as no names.
        /// </summary>
        [Test]
        public void Read_WithoutAFile_ReturnsNoNames()
        {
            Assert.That(HotReloadWarmUpTargetLedger.Read(_projectRoot), Is.Empty);
        }

        /// <summary>
        /// What: a file with another header reads as no names.
        /// </summary>
        [Test]
        public void Read_WithAnotherHeader_ReturnsNoNames()
        {
            WriteLedger("uloop-hot-reload-warm-up-targets 2\nA\n");

            Assert.That(HotReloadWarmUpTargetLedger.Read(_projectRoot), Is.Empty);
        }

        /// <summary>
        /// What: blank lines, including CRLF leftovers, are skipped.
        /// </summary>
        [Test]
        public void Read_SkipsBlankLines()
        {
            WriteLedger(Header + "\r\nA\r\n\n  \nB\n");

            Assert.That(HotReloadWarmUpTargetLedger.Read(_projectRoot), Is.EqualTo(new[] { "A", "B" }));
        }

        private string LedgerPath()
        {
            return Path.Combine(
                _projectRoot,
                HotReloadConstants.WarmUpRelativeDirectory,
                HotReloadConstants.WarmUpTargetsFileName);
        }

        private void WriteLedger(string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LedgerPath()));
            File.WriteAllText(LedgerPath(), content, new UTF8Encoding(false));
        }
    }
}
