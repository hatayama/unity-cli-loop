using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies a HashSet previews as its contents like the other materialized collections, while a
    /// deferred LINQ sequence is still never enumerated.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointHashSetPreviewTests
    {
        private const int DefaultMaxElementCount = 10;

        /// <summary>
        /// Verifies a top-level HashSet previews as a JSON array of its elements.
        /// </summary>
        [Test]
        public void TrySerialize_WhenValueIsHashSet_ReturnsJsonArray()
        {
            HashSet<int> set = new HashSet<int> { 1, 2, 3 };
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(
                set, DefaultMaxElementCount, ref truncated, out string preview);

            Assert.That(serialized, Is.True);
            Assert.That(preview, Is.EqualTo("[1,2,3]"));
            Assert.That(truncated, Is.False);
        }

        /// <summary>
        /// Verifies a HashSet nested in a List previews as a nested JSON array, not its type name.
        /// </summary>
        [Test]
        public void TrySerialize_WhenHashSetIsNestedInList_ReturnsNestedJsonArray()
        {
            List<HashSet<int>> sets = new List<HashSet<int>> { new HashSet<int> { 4, 5 } };
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(
                sets, DefaultMaxElementCount, ref truncated, out string preview);

            Assert.That(serialized, Is.True);
            Assert.That(preview, Is.EqualTo("[[4,5]]"));
        }

        /// <summary>
        /// Verifies a HashSet larger than the element cap previews only the capped elements and reports truncation.
        /// </summary>
        [Test]
        public void TrySerialize_WhenHashSetExceedsElementCap_TruncatesPreview()
        {
            HashSet<int> set = new HashSet<int> { 1, 2, 3 };
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(
                set, 2, ref truncated, out string preview);

            Assert.That(serialized, Is.True);
            Assert.That(preview, Is.EqualTo("[1,2]"));
            Assert.That(truncated, Is.True);
        }

        /// <summary>
        /// Verifies a deferred LINQ sequence is not previewed, so its selector never runs.
        /// </summary>
        [Test]
        public void TrySerialize_WhenValueIsDeferredSelect_DoesNotEnumerate()
        {
            int selectorCalls = 0;
            IEnumerable<int> deferred = new[] { 1, 2 }.Select(value =>
            {
                selectorCalls++;
                return value;
            });
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(
                deferred, DefaultMaxElementCount, ref truncated, out string _);

            Assert.That(serialized, Is.False);
            Assert.That(selectorCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a deferred LINQ sequence nested in a List is left unenumerated, so its selector never runs.
        /// </summary>
        [Test]
        public void TrySerialize_WhenDeferredSelectIsNestedInList_DoesNotEnumerate()
        {
            int selectorCalls = 0;
            IEnumerable<int> deferred = new[] { 1, 2 }.Select(value =>
            {
                selectorCalls++;
                return value;
            });
            List<IEnumerable<int>> holder = new List<IEnumerable<int>> { deferred };
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(
                holder, DefaultMaxElementCount, ref truncated, out string preview);

            Assert.That(serialized, Is.True);
            Assert.That(preview, Does.Not.Contain("1,2"));
            Assert.That(selectorCalls, Is.EqualTo(0));
        }
    }
}
