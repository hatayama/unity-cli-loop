using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Tests the accumulator that sums the milliseconds of each phase of one apply run.
    /// </summary>
    public sealed class HotReloadRunTimingTests
    {
        /// <summary>
        /// What: two analysis spans of one run, such as a worker run and its rerun, add up.
        /// </summary>
        [Test]
        public void AddAnalysis_CalledTwice_SumsTheMilliseconds()
        {
            HotReloadRunTiming timing = new HotReloadRunTiming();

            timing.AddAnalysis(30);
            timing.AddAnalysis(12);

            Assert.That(timing.Complete(100).AnalysisMs, Is.EqualTo(42));
        }

        /// <summary>
        /// What: completing the run copies each phase into its own field and the total as given.
        /// Every value differs, so a phase written into another's field fails.
        /// </summary>
        [Test]
        public void Complete_CopiesEveryPhaseAndTheTotal()
        {
            HotReloadRunTiming timing = new HotReloadRunTiming();
            timing.AddAnalysis(5);
            timing.AddShimCompile(7);
            timing.AddPatch(1);

            HotReloadTimingBreakdown breakdown = timing.Complete(50);

            Assert.That(breakdown.AnalysisMs, Is.EqualTo(5), "AnalysisMs");
            Assert.That(breakdown.ShimCompileMs, Is.EqualTo(7), "ShimCompileMs");
            Assert.That(breakdown.PatchMs, Is.EqualTo(1), "PatchMs");
            Assert.That(breakdown.TotalMs, Is.EqualTo(50), "TotalMs");
        }

        /// <summary>
        /// What: a run that reached no phase, such as one whose files all dropped out before any
        /// group ran, reports zero for every phase and still reports its total.
        /// </summary>
        [Test]
        public void Complete_WithoutPhases_ReportsZeroPhasesAndTheTotal()
        {
            HotReloadTimingBreakdown breakdown = new HotReloadRunTiming().Complete(3);

            Assert.That(breakdown.AnalysisMs, Is.EqualTo(0), "AnalysisMs");
            Assert.That(breakdown.ShimCompileMs, Is.EqualTo(0), "ShimCompileMs");
            Assert.That(breakdown.PatchMs, Is.EqualTo(0), "PatchMs");
            Assert.That(breakdown.TotalMs, Is.EqualTo(3), "TotalMs");
        }

        /// <summary>
        /// What: a negative total is refused instead of being reported, since elapsed time never is.
        /// </summary>
        [Test]
        public void Complete_NegativeTotal_Throws()
        {
            HotReloadRunTiming timing = new HotReloadRunTiming();

            Assert.Throws<ArgumentOutOfRangeException>(() => timing.Complete(-1));
        }
    }
}
