using System;
using System.IO;
using System.Text;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the contract of <see cref="HotReloadSourceStampManifest"/>: what it
    /// writes reads back, and a file that cannot be parsed in full answers no stamp at all.
    /// </summary>
    public sealed class HotReloadSourceStampManifestTests
    {
        private const long FirstTicks = 637134336000000000;
        private const long SecondTicks = 637135200000000000;

        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        /// <summary>
        /// Verifies that a snapshot directory without a manifest answers no stamp.
        /// </summary>
        [Test]
        public void Load_WhenTheFileIsMissing_IsEmpty()
        {
            HotReloadSourceStampManifest manifest = HotReloadSourceStampManifest.Load(_tempRoot);

            Assert.That(manifest.Count, Is.EqualTo(0));
            Assert.That(manifest.TryGetStamp("x.cs", out long _, out long _), Is.False);
        }

        /// <summary>
        /// Verifies that every line Write wrote is read back with its length and write time.
        /// </summary>
        [Test]
        public void Load_RoundTripsWhatWriteWrote()
        {
            HotReloadSourceStampManifest.Write(
                _tempRoot,
                new[]
                {
                    HotReloadSourceStampManifest.FormatLine("a.cs", 7, FirstTicks),
                    HotReloadSourceStampManifest.FormatLine("b.cs", 2, SecondTicks),
                });

            HotReloadSourceStampManifest manifest = HotReloadSourceStampManifest.Load(_tempRoot);

            Assert.That(manifest.Count, Is.EqualTo(2));
            Assert.That(manifest.TryGetStamp("a.cs", out long firstLength, out long firstTicks), Is.True);
            Assert.That(firstLength, Is.EqualTo(7));
            Assert.That(firstTicks, Is.EqualTo(FirstTicks));
            Assert.That(manifest.TryGetStamp("b.cs", out long secondLength, out long secondTicks), Is.True);
            Assert.That(secondLength, Is.EqualTo(2));
            Assert.That(secondTicks, Is.EqualTo(SecondTicks));
        }

        /// <summary>
        /// Verifies that a manifest of another format version is ignored, even for a line that
        /// would parse.
        /// </summary>
        [Test]
        public void Load_WhenTheHeaderDiffers_IsEmpty()
        {
            WriteRawManifest("uloop-source-stamps 2\n" + ValidLine() + "\n");

            AssertIgnoredAsAWhole();
        }

        /// <summary>
        /// Verifies that a line with two fields makes the whole manifest ignored, including the
        /// valid line before it.
        /// </summary>
        [Test]
        public void Load_WhenALineHasTwoFields_IsEmpty()
        {
            WriteRawManifest(Header() + ValidLine() + "\n" + "a.cs\t7\n");

            AssertIgnoredAsAWhole();
        }

        /// <summary>
        /// Verifies that a length that is not an integer makes the whole manifest ignored.
        /// </summary>
        [Test]
        public void Load_WhenALengthIsNotAnInteger_IsEmpty()
        {
            WriteRawManifest(Header() + ValidLine() + "\n" + "a.cs\t7x\t1\n");

            AssertIgnoredAsAWhole();
        }

        /// <summary>
        /// Verifies that a signed write time makes the whole manifest ignored rather than read as
        /// a stamp no source can have.
        /// </summary>
        [Test]
        public void Load_WhenAWriteTimeIsNegative_IsEmpty()
        {
            WriteRawManifest(Header() + ValidLine() + "\n" + "a.cs\t7\t-1\n");

            AssertIgnoredAsAWhole();
        }

        /// <summary>
        /// Verifies that a manifest whose last line has no line break, as a file cut off while
        /// being written would, is ignored as a whole.
        /// </summary>
        [Test]
        public void Load_WhenTheTextDoesNotEndWithALineBreak_IsEmpty()
        {
            WriteRawManifest(Header() + ValidLine() + "\n" + "a.cs\t7\t1");

            AssertIgnoredAsAWhole();
        }

        /// <summary>
        /// Verifies that a file name listed twice makes the whole manifest ignored, since neither
        /// line can be told to be the right one.
        /// </summary>
        [Test]
        public void Load_WhenAFileNameRepeats_IsEmpty()
        {
            WriteRawManifest(Header() + ValidLine() + "\n" + ValidLine() + "\n");

            AssertIgnoredAsAWhole();
        }

        /// <summary>
        /// Verifies that a manifest with CRLF line endings, as a Windows checkout tool could
        /// leave it, still reads.
        /// </summary>
        [Test]
        public void Load_AcceptsCarriageReturnLineEndings()
        {
            WriteRawManifest(HotReloadConstants.SourceStampManifestHeader + "\r\n" + ValidLine() + "\r\n");

            HotReloadSourceStampManifest manifest = HotReloadSourceStampManifest.Load(_tempRoot);

            Assert.That(manifest.Count, Is.EqualTo(1));
            Assert.That(manifest.TryGetStamp("b.cs", out long length, out long ticks), Is.True);
            Assert.That(length, Is.EqualTo(2));
            Assert.That(ticks, Is.EqualTo(SecondTicks));
        }

        /// <summary>
        /// Verifies that a file name the manifest has no line for answers no stamp.
        /// </summary>
        [Test]
        public void TryGetStamp_UnknownFileName_IsFalse()
        {
            WriteRawManifest(Header() + ValidLine() + "\n");

            HotReloadSourceStampManifest manifest = HotReloadSourceStampManifest.Load(_tempRoot);

            Assert.That(manifest.TryGetStamp("a.cs", out long _, out long _), Is.False);
        }

        private static string Header()
        {
            return HotReloadConstants.SourceStampManifestHeader + "\n";
        }

        // Why a valid line in front of every malformed one: a loader that skipped the bad line
        // instead of dropping the file would still answer this one, so the test tells them apart.
        private static string ValidLine()
        {
            return HotReloadSourceStampManifest.FormatLine("b.cs", 2, SecondTicks);
        }

        private void WriteRawManifest(string text)
        {
            File.WriteAllText(
                Path.Combine(_tempRoot, HotReloadConstants.SourceStampManifestFileName),
                text,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private void AssertIgnoredAsAWhole()
        {
            HotReloadSourceStampManifest manifest = HotReloadSourceStampManifest.Load(_tempRoot);

            Assert.That(manifest.Count, Is.EqualTo(0));
            Assert.That(manifest.TryGetStamp("b.cs", out long _, out long _), Is.False);
        }
    }
}
