using System;
using System.Collections;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the collection preview serializer for null input, Unity objects as elements and keys, dictionary
    /// truncation, collections that are not walked, compiler-generated closure fields, and a throwing ToString.
    /// </summary>
    public sealed class SourcePausePointCollectionPreviewSerializerTests
    {
        private readonly List<ScriptableObject> _createdObjects = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (ScriptableObject createdObject in _createdObjects)
            {
                if (createdObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdObject);
                }
            }

            _createdObjects.Clear();
        }

        /// <summary>
        /// Verifies a null value has no preview.
        /// </summary>
        [Test]
        public void TrySerialize_WithNull_ReturnsFalse()
        {
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(null, 10, ref truncated, out string preview);

            Assert.That(serialized, Is.False);
            Assert.That(preview, Is.Empty);
        }

        /// <summary>
        /// Verifies a Unity object element is shown by name, and a destroyed one as destroyed.
        /// </summary>
        [Test]
        public void TrySerialize_WithUnityObjectElements_ShowsTheNameOrDestroyed()
        {
            ScriptableObject live = CreateObject("LiveProbe");
            ScriptableObject destroyed = CreateObject("DestroyedProbe");
            UnityEngine.Object.DestroyImmediate(destroyed);
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(
                new List<ScriptableObject> { live, destroyed }, 10, ref truncated, out string preview);

            Assert.That(serialized, Is.True);
            Assert.That(preview, Is.EqualTo("[\"LiveProbe\",\"(destroyed)\"]"));
        }

        /// <summary>
        /// Verifies a Unity object dictionary key is shown by name.
        /// </summary>
        [Test]
        public void TrySerialize_WithAUnityObjectKey_UsesItsNameAsTheKey()
        {
            ScriptableObject key = CreateObject("KeyProbe");
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(
                new Dictionary<ScriptableObject, int> { { key, 3 } }, 10, ref truncated, out string preview);

            Assert.That(serialized, Is.True);
            Assert.That(preview, Is.EqualTo("{\"KeyProbe\":3}"));
        }

        /// <summary>
        /// Verifies a dictionary with more entries than the limit is cut at the limit and reported as truncated.
        /// </summary>
        [Test]
        public void TrySerialize_WithMoreDictionaryEntriesThanTheLimit_TruncatesAtTheLimit()
        {
            Dictionary<string, int> dictionary = new Dictionary<string, int> { { "a", 1 }, { "b", 2 }, { "c", 3 } };
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(dictionary, 2, ref truncated, out string preview);

            Assert.That(serialized, Is.True);
            Assert.That(truncated, Is.True);
            Assert.That(preview, Is.EqualTo("{\"a\":1,\"b\":2}"));
        }

        /// <summary>
        /// Verifies a non-generic dictionary is previewed by its entries.
        /// </summary>
        [Test]
        public void TrySerialize_WithAHashtable_PreviewsItsEntries()
        {
            Hashtable hashtable = new Hashtable { { "only", 1 } };
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(hashtable, 10, ref truncated, out string preview);

            Assert.That(serialized, Is.True);
            Assert.That(truncated, Is.False);
            Assert.That(preview, Is.EqualTo("{\"only\":1}"));
        }

        /// <summary>
        /// Verifies a nested sequence that is not materialized is shown by its ToString instead of being enumerated.
        /// </summary>
        [Test]
        public void TrySerialize_WithALazySequenceInsideAList_ShowsItsToStringWithoutEnumerating()
        {
            int enumeratedCount = 0;
            IEnumerable<int> nested = CountEnumeration(() => enumeratedCount++);
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(
                new List<object> { nested }, 10, ref truncated, out string preview);

            Assert.That(serialized, Is.True);
            Assert.That(preview, Is.EqualTo("[\"" + nested + "\"]"));
            Assert.That(enumeratedCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a closure object shows its captured local and leaves out the compiler-generated this field.
        /// </summary>
        [Test]
        public void TrySerialize_WithAClosureObject_ShowsCapturedLocalsWithoutTheThisField()
        {
            Func<string> closure = CreateClosure("captured");
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(closure.Target, 10, ref truncated, out string preview);

            Assert.That(serialized, Is.True);
            Assert.That(preview, Is.EqualTo("{\"label\":\"captured\"}"));
        }

        /// <summary>
        /// Verifies an element whose ToString throws is shown as the exception type and the exception is logged.
        /// </summary>
        [Test]
        public void TrySerialize_WhenAnElementToStringThrows_ShowsTheExceptionType()
        {
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: preview boom");
            bool truncated = false;

            bool serialized = SourcePausePointCollectionPreviewSerializer.TrySerialize(
                new List<object> { new ThrowingToString() }, 10, ref truncated, out string preview);

            Assert.That(serialized, Is.True);
            Assert.That(preview, Is.EqualTo("[\"(toString threw InvalidOperationException)\"]"));
        }

        private ScriptableObject CreateObject(string name)
        {
            ScriptableObject createdObject = ScriptableObject.CreateInstance<ScriptableObject>();
            createdObject.name = name;
            _createdObjects.Add(createdObject);
            return createdObject;
        }

        // Capturing both a local and this makes the compiler put a <>4__this field on the closure object.
        private Func<string> CreateClosure(string label)
        {
            return () => label + _createdObjects.Count;
        }

        private sealed class ThrowingToString
        {
            public override string ToString() => throw new InvalidOperationException("preview boom");
        }

        private static IEnumerable<int> CountEnumeration(Action onEnumerated)
        {
            onEnumerated();
            yield return 1;
        }
    }
}
