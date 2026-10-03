using System.Collections;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies a HashSet previews as its contents like the other materialized collections, while a
    /// deferred LINQ sequence, a HashSet subclass and a user-defined generic-only collection are
    /// still never enumerated.
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

        /// <summary>
        /// Verifies a HashSet subclass is not previewed, so its reimplemented enumerator never runs.
        /// </summary>
        [Test]
        public void TrySerialize_WhenValueIsHashSetSubclass_DoesNotEnumerate()
        {
            CountingHashSet set = new CountingHashSet { 1, 2 };
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(
                set, DefaultMaxElementCount, ref truncated, out string _);

            Assert.That(serialized, Is.False);
            Assert.That(set.EnumeratorCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a HashSet subclass nested in a List shows its type name, so its reimplemented enumerator never runs.
        /// </summary>
        [Test]
        public void TrySerialize_WhenHashSetSubclassIsNestedInList_ShowsTypeNameWithoutEnumerating()
        {
            CountingHashSet set = new CountingHashSet { 1, 2 };
            List<CountingHashSet> holder = new List<CountingHashSet> { set };
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(
                holder, DefaultMaxElementCount, ref truncated, out string preview);

            Assert.That(serialized, Is.True);
            Assert.That(preview, Does.Contain(nameof(CountingHashSet)));
            Assert.That(set.EnumeratorCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a user-defined collection that implements only the generic ICollection is not previewed, so its enumerator never runs.
        /// </summary>
        [Test]
        public void TrySerialize_WhenValueIsGenericOnlyUserCollection_DoesNotEnumerate()
        {
            CountingGenericCollection collection = new CountingGenericCollection { 1, 2 };
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(
                collection, DefaultMaxElementCount, ref truncated, out string _);

            Assert.That(serialized, Is.False);
            Assert.That(collection.EnumeratorCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a user-defined generic-only collection nested in a List shows its type name, so its enumerator never runs.
        /// </summary>
        [Test]
        public void TrySerialize_WhenGenericOnlyUserCollectionIsNestedInList_ShowsTypeNameWithoutEnumerating()
        {
            CountingGenericCollection collection = new CountingGenericCollection { 1, 2 };
            List<CountingGenericCollection> holder = new List<CountingGenericCollection> { collection };
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(
                holder, DefaultMaxElementCount, ref truncated, out string preview);

            Assert.That(serialized, Is.True);
            Assert.That(preview, Does.Contain(nameof(CountingGenericCollection)));
            Assert.That(collection.EnumeratorCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// A HashSet subclass that reimplements enumeration and counts how often it is enumerated.
        /// </summary>
        private sealed class CountingHashSet : HashSet<int>, IEnumerable<int>
        {
            public int EnumeratorCalls { get; private set; }

            IEnumerator<int> IEnumerable<int>.GetEnumerator()
            {
                EnumeratorCalls++;
                return base.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                EnumeratorCalls++;
                return base.GetEnumerator();
            }
        }

        /// <summary>
        /// A collection that implements only the generic ICollection and counts how often it is enumerated.
        /// </summary>
        private sealed class CountingGenericCollection : ICollection<int>
        {
            private readonly List<int> _items = new List<int>();

            public int EnumeratorCalls { get; private set; }

            public int Count => _items.Count;

            public bool IsReadOnly => false;

            public void Add(int item)
            {
                _items.Add(item);
            }

            public void Clear()
            {
                _items.Clear();
            }

            public bool Contains(int item)
            {
                return _items.Contains(item);
            }

            public void CopyTo(int[] array, int arrayIndex)
            {
                _items.CopyTo(array, arrayIndex);
            }

            public bool Remove(int item)
            {
                return _items.Remove(item);
            }

            public IEnumerator<int> GetEnumerator()
            {
                EnumeratorCalls++;
                return _items.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                EnumeratorCalls++;
                return _items.GetEnumerator();
            }
        }
    }
}
