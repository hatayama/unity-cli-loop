using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies when the sibling verdict cache answers from a recorded verdict and when it does not.
    /// </summary>
    public sealed class HotReloadSiblingVerdictCacheTests
    {
        private const string SnapshotPath = "/proj/Library/UloopHotReload/SourceSnapshot/Asm-mvid1/a.cs";
        private const string OtherSnapshotPath = "/proj/Library/UloopHotReload/SourceSnapshot/Asm-mvid2/a.cs";
        private const string SourcePath = "/proj/Assets/A.cs";
        private const string OtherSourcePath = "/proj/Temp/Copy/A.cs";

        /// <summary>
        /// What: an empty cache has no verdict.
        /// </summary>
        [Test]
        public void TryGetVerdict_WhenNothingRecorded_ReturnsFalse()
        {
            HotReloadSiblingVerdictCache cache = new HotReloadSiblingVerdictCache();

            bool found = cache.TryGetVerdict(SnapshotPath, SourcePath, 10, 100, out bool _);

            Assert.That(found, Is.False);
        }

        /// <summary>
        /// What: a recorded verdict is returned for the same pair and the same stamp.
        /// </summary>
        [Test]
        public void TryGetVerdict_SamePairAndStamp_ReturnsRecordedVerdict()
        {
            HotReloadSiblingVerdictCache cache = new HotReloadSiblingVerdictCache();
            cache.Record(SnapshotPath, SourcePath, 10, 100, true);

            bool found = cache.TryGetVerdict(SnapshotPath, SourcePath, 10, 100, out bool matches);

            Assert.That(found, Is.True);
            Assert.That(matches, Is.True);
        }

        /// <summary>
        /// What: a different length makes the recorded verdict unusable.
        /// </summary>
        [Test]
        public void TryGetVerdict_DifferentLength_ReturnsFalse()
        {
            HotReloadSiblingVerdictCache cache = new HotReloadSiblingVerdictCache();
            cache.Record(SnapshotPath, SourcePath, 10, 100, true);

            bool found = cache.TryGetVerdict(SnapshotPath, SourcePath, 11, 100, out bool _);

            Assert.That(found, Is.False);
        }

        /// <summary>
        /// What: a different last write time makes the recorded verdict unusable.
        /// </summary>
        [Test]
        public void TryGetVerdict_DifferentWriteTime_ReturnsFalse()
        {
            HotReloadSiblingVerdictCache cache = new HotReloadSiblingVerdictCache();
            cache.Record(SnapshotPath, SourcePath, 10, 100, true);

            bool found = cache.TryGetVerdict(SnapshotPath, SourcePath, 10, 101, out bool _);

            Assert.That(found, Is.False);
        }

        /// <summary>
        /// What: the same source compared with another snapshot has no verdict, because the
        /// snapshot path is part of the key.
        /// </summary>
        [Test]
        public void TryGetVerdict_OtherSnapshotPath_ReturnsFalse()
        {
            HotReloadSiblingVerdictCache cache = new HotReloadSiblingVerdictCache();
            cache.Record(SnapshotPath, SourcePath, 10, 100, true);

            bool found = cache.TryGetVerdict(OtherSnapshotPath, SourcePath, 10, 100, out bool _);

            Assert.That(found, Is.False);
        }

        /// <summary>
        /// What: another source compared with the same snapshot has no verdict, because the
        /// source path is part of the key.
        /// </summary>
        [Test]
        public void TryGetVerdict_OtherSourcePath_ReturnsFalse()
        {
            HotReloadSiblingVerdictCache cache = new HotReloadSiblingVerdictCache();
            cache.Record(SnapshotPath, SourcePath, 10, 100, true);

            bool found = cache.TryGetVerdict(SnapshotPath, OtherSourcePath, 10, 100, out bool _);

            Assert.That(found, Is.False);
        }

        /// <summary>
        /// What: recording the pair again replaces the earlier verdict.
        /// </summary>
        [Test]
        public void Record_SamePairAgain_ReplacesTheVerdict()
        {
            HotReloadSiblingVerdictCache cache = new HotReloadSiblingVerdictCache();
            cache.Record(SnapshotPath, SourcePath, 10, 100, true);
            cache.Record(SnapshotPath, SourcePath, 10, 100, false);

            bool found = cache.TryGetVerdict(SnapshotPath, SourcePath, 10, 100, out bool matches);

            Assert.That(found, Is.True);
            Assert.That(matches, Is.False);
            Assert.That(cache.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// What: Clear drops every recorded verdict.
        /// </summary>
        [Test]
        public void Clear_DropsEveryVerdict()
        {
            HotReloadSiblingVerdictCache cache = new HotReloadSiblingVerdictCache();
            cache.Record(SnapshotPath, SourcePath, 10, 100, true);
            cache.Record(OtherSnapshotPath, SourcePath, 10, 100, false);

            cache.Clear();

            bool found = cache.TryGetVerdict(SnapshotPath, SourcePath, 10, 100, out bool _);
            Assert.That(found, Is.False);
            Assert.That(cache.Count, Is.EqualTo(0));
        }
    }
}
