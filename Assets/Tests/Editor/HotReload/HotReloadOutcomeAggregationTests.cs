using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Tests run-level outcome counting.
    /// </summary>
    public sealed class HotReloadOutcomeAggregationTests
    {
        /// <summary>
        /// What: every HotReloadMethodOutcomeKind is counted into its own bucket, so a newly added
        /// kind cannot be silently dropped from the run summary.
        /// </summary>
        [Test]
        public void CountMethodOutcomeKinds_WithEveryKind_CountsEachBucketOnce()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>
            {
                HotReloadMethodOutcome.Patched("Type.Patched()", "Assets/A.cs"),
                HotReloadMethodOutcome.Skipped("Type.Skipped()", "unsupported", "Assets/A.cs"),
                HotReloadMethodOutcome.Failed("Type.Failed()", "shim compile failed", "Assets/A.cs"),
                HotReloadMethodOutcome.Added("Type.Added()", "Assets/A.cs"),
                HotReloadMethodOutcome.AlreadyActive("Type.AlreadyActive()", "Assets/A.cs"),
                HotReloadMethodOutcome.Stale("Type.Stale()", "Assets/A.cs")
            };

            (int patchedCount, int failedCount, int skippedCount, int alreadyActiveCount, int addedCount, int staleCount) =
                HotReloadOutcomeAggregation.CountMethodOutcomeKinds(outcomes);

            Assert.That(patchedCount, Is.EqualTo(1));
            Assert.That(failedCount, Is.EqualTo(1));
            Assert.That(skippedCount, Is.EqualTo(1));
            Assert.That(alreadyActiveCount, Is.EqualTo(1));
            Assert.That(addedCount, Is.EqualTo(1));
            Assert.That(staleCount, Is.EqualTo(1));
        }
    }
}
