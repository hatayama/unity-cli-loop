using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies the opt-in dynamic-code partial-results holder.
    /// </summary>
    [TestFixture]
    public sealed class UloopDynamicCodePartialResultsTests
    {
        [SetUp]
        public void SetUp()
        {
            UloopDynamicCodePartialResults.OpenExecutionScope();
        }

        [TearDown]
        public void TearDown()
        {
            UloopDynamicCodePartialResults.AfterGenerationValidatedForTesting = null;
            UloopDynamicCodePartialResults.Clear();
        }

        /// <summary>
        /// What: Set records string values and represents null as the literal null string.
        /// </summary>
        [Test]
        public void Set_WhenValuesAreProvided_SnapshotPreservesStringAndNullValues()
        {
            UloopDynamicCodePartialResults.Set("count", 42);
            UloopDynamicCodePartialResults.Set("optional", null);

            Dictionary<string, string> snapshot = UloopDynamicCodePartialResults.Snapshot();

            Assert.That(snapshot, Has.Count.EqualTo(2));
            Assert.That(snapshot["count"], Is.EqualTo("42"));
            Assert.That(snapshot["optional"], Is.EqualTo("null"));
        }

        /// <summary>
        /// What: Clear removes entries before the next dynamic-code execution begins.
        /// </summary>
        [Test]
        public void Clear_AfterValuesAreSet_SnapshotIsEmpty()
        {
            UloopDynamicCodePartialResults.Set("result", "captured");

            UloopDynamicCodePartialResults.Clear();

            Dictionary<string, string> snapshot = UloopDynamicCodePartialResults.Snapshot();
            Assert.That(snapshot, Is.Empty);
        }

        /// <summary>
        /// What: Snapshot excludes a stale write that resumes after the next execution scope opens.
        /// </summary>
        [Test]
        public void Snapshot_WhenStaleSetResumesAfterNextScopeOpens_ExcludesStaleEntryAndKeepsCurrentEntries()
        {
            UloopDynamicCodePartialResults.AfterGenerationValidatedForTesting = () =>
            {
                UloopDynamicCodePartialResults.AfterGenerationValidatedForTesting = null;
                UloopDynamicCodePartialResults.OpenExecutionScope();
                UloopDynamicCodePartialResults.Set("currentRequest", "ready");
            };

            UloopDynamicCodePartialResults.Set("lateFromCancelledRequest", "late");

            Dictionary<string, string> snapshot = UloopDynamicCodePartialResults.Snapshot();
            Assert.That(snapshot["currentRequest"], Is.EqualTo("ready"));
            Assert.That(snapshot.ContainsKey("lateFromCancelledRequest"), Is.False);
        }
    }
}
