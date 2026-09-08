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
        /// What: every HotReloadMethodOutcomeKind is counted into its own tally bucket. Each kind
        /// appears a different number of times, so a swapped bucket cannot pass unnoticed.
        /// </summary>
        [Test]
        public void CountMethodOutcomeKinds_WithEveryKind_CountsEachKindIntoItsOwnBucket()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            AddOutcomes(outcomes, 1, HotReloadMethodOutcome.Patched("Type.Patched()", "Assets/A.cs"));
            AddOutcomes(outcomes, 2, HotReloadMethodOutcome.Failed("Type.Failed()", "shim compile failed", "Assets/A.cs"));
            AddOutcomes(outcomes, 3, HotReloadMethodOutcome.Skipped("Type.Skipped()", "unsupported", "Assets/A.cs"));
            AddOutcomes(outcomes, 4, HotReloadMethodOutcome.AlreadyActive("Type.AlreadyActive()", "Assets/A.cs"));
            AddOutcomes(outcomes, 5, HotReloadMethodOutcome.Added("Type.Added()", "Assets/A.cs"));
            AddOutcomes(outcomes, 6, HotReloadMethodOutcome.Stale("Type.Stale()", "Assets/A.cs"));

            HotReloadOutcomeTally tally = HotReloadOutcomeAggregation.CountMethodOutcomeKinds(outcomes);

            Assert.That(tally.PatchedCount, Is.EqualTo(1));
            Assert.That(tally.FailedCount, Is.EqualTo(2));
            Assert.That(tally.SkippedCount, Is.EqualTo(3));
            Assert.That(tally.AlreadyActiveCount, Is.EqualTo(4));
            Assert.That(tally.AddedCount, Is.EqualTo(5));
            Assert.That(tally.StaleCount, Is.EqualTo(6));
            Assert.That(tally.HasFailure, Is.True);
        }

        /// <summary>
        /// What: a run without a failed outcome reports no failure, so the summary success flag
        /// derived from the tally stays true.
        /// </summary>
        [Test]
        public void CountMethodOutcomeKinds_WithoutFailedOutcome_ReportsNoFailure()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>
            {
                HotReloadMethodOutcome.Patched("Type.Patched()", "Assets/A.cs")
            };

            HotReloadOutcomeTally tally = HotReloadOutcomeAggregation.CountMethodOutcomeKinds(outcomes);

            Assert.That(tally.FailedCount, Is.EqualTo(0));
            Assert.That(tally.HasFailure, Is.False);
        }

        private static void AddOutcomes(List<HotReloadMethodOutcome> outcomes, int count, HotReloadMethodOutcome outcome)
        {
            for (int index = 0; index < count; index++)
            {
                outcomes.Add(outcome);
            }
        }
    }
}
