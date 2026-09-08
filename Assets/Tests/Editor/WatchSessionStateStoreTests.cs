using System;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies watch expressions round-trip through SessionState and that unusable stored
    /// content degrades to "no watches to restore" instead of failing Editor startup.
    /// </summary>
    [TestFixture]
    public sealed class WatchSessionStateStoreTests
    {
        private const string RecordsKey = "io.github.hatayama.uloopmcp.watch.persistedRecords";

        private string _originalValue;

        [SetUp]
        public void SetUp()
        {
            _originalValue = SessionState.GetString(RecordsKey, string.Empty);
            SessionState.EraseString(RecordsKey);
        }

        [TearDown]
        public void TearDown()
        {
            SessionState.SetString(RecordsKey, _originalValue);
        }

        /// <summary>
        /// What: expressions containing newlines, quotes, and non-ASCII survive the round trip byte for byte.
        /// </summary>
        [Test]
        public void SaveThenLoad_WithAwkwardExpressionText_RoundTripsEveryField()
        {
            const string awkwardExpression = "\"quoted\" + \n\t'x' + \"\\u00e9\\u3042\"";
            WatchSessionStateStore store = new();

            store.Save(new List<WatchPersistedRecord>
            {
                new() { Id = "first", Expression = awkwardExpression, MaxHistory = 7 },
                new() { Id = "second", Expression = "1 + 2", MaxHistory = 20 }
            });
            IReadOnlyList<WatchPersistedRecord> loaded = store.Load();

            Assert.That(loaded, Has.Count.EqualTo(2));
            Assert.That(loaded[0].Id, Is.EqualTo("first"));
            Assert.That(loaded[0].Expression, Is.EqualTo(awkwardExpression));
            Assert.That(loaded[0].MaxHistory, Is.EqualTo(7));
            Assert.That(loaded[1].Id, Is.EqualTo("second"));
        }

        /// <summary>
        /// What: saving an empty list clears the store rather than leaving the previous records behind.
        /// </summary>
        [Test]
        public void Save_WithEmptyList_LeavesNothingToRestore()
        {
            WatchSessionStateStore store = new();
            store.Save(new List<WatchPersistedRecord>
            {
                new() { Id = "first", Expression = "1 + 2", MaxHistory = 20 }
            });

            store.Save(Array.Empty<WatchPersistedRecord>());

            Assert.That(store.Load(), Is.Empty);
        }

        /// <summary>
        /// What: a record without an expression cannot be recompiled, so saving it is rejected.
        /// </summary>
        [Test]
        public void Save_WithNullExpression_ThrowsBecauseTheRecordCouldNeverBeRestored()
        {
            WatchSessionStateStore store = new();

            Assert.That(
                () => store.Save(new List<WatchPersistedRecord>
                {
                    new() { Id = "first", Expression = null, MaxHistory = 20 }
                }),
                Throws.ArgumentException);
        }

        /// <summary>
        /// What: an unset key means no watches were persisted, not an error.
        /// </summary>
        [Test]
        public void Load_WhenKeyWasNeverWritten_ReturnsEmpty()
        {
            WatchSessionStateStore store = new();

            Assert.That(store.Load(), Is.Empty);
        }

        /// <summary>
        /// What: content written by another uloop generation degrades to empty instead of throwing.
        /// </summary>
        [Test]
        public void Load_WhenStoredJsonIsNotAWatchRecordList_ReturnsEmpty()
        {
            SessionState.SetString(RecordsKey, "{ this is not json");
            WatchSessionStateStore store = new();

            Assert.That(store.Load(), Is.Empty);
        }
    }
}
