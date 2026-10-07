using System;
using System.Collections.Generic;

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

        /// <summary>
        /// What: the time outside the three phases is reported as the total minus the phases.
        /// </summary>
        [Test]
        public void Complete_ReportsOtherAsTotalMinusThePhases()
        {
            HotReloadRunTiming timing = new HotReloadRunTiming();
            timing.AddAnalysis(100);
            timing.AddShimCompile(200);
            timing.AddPatch(30);

            Assert.That(timing.Complete(1000).OtherMs, Is.EqualTo(670));
        }

        /// <summary>
        /// What: a total that the phases fill exactly leaves nothing outside them.
        /// </summary>
        [Test]
        public void Complete_TotalEqualToThePhases_ReportsZeroOther()
        {
            HotReloadRunTiming timing = new HotReloadRunTiming();
            timing.AddAnalysis(100);
            timing.AddShimCompile(200);
            timing.AddPatch(30);

            Assert.That(timing.Complete(330).OtherMs, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a total shorter than the phases inside it is refused instead of reporting a
        /// negative time outside the phases.
        /// </summary>
        [Test]
        public void Complete_TotalBelowThePhases_Throws()
        {
            HotReloadRunTiming timing = new HotReloadRunTiming();
            timing.AddAnalysis(100);
            timing.AddShimCompile(200);
            timing.AddPatch(30);

            Assert.Throws<ArgumentOutOfRangeException>(() => timing.Complete(329));
        }

        /// <summary>
        /// What: a step measured twice in one run, such as once per group, adds up under one entry,
        /// and the steps keep the order they were first seen in.
        /// </summary>
        [Test]
        public void AddDetail_SameStepTwice_SumsAndKeepsFirstSeenOrder()
        {
            HotReloadRunTiming timing = new HotReloadRunTiming();

            timing.AddDetail("plan", 3);
            timing.AddDetail("resolve_inputs", 5);
            timing.AddDetail("plan", 4);

            Assert.That(timing.Details, Is.EqualTo(new[]
            {
                new KeyValuePair<string, long>("plan", 7),
                new KeyValuePair<string, long>("resolve_inputs", 5),
            }));
        }

        /// <summary>
        /// What: a negative step time is refused instead of shrinking the step's sum.
        /// </summary>
        [Test]
        public void AddDetail_NegativeMilliseconds_Throws()
        {
            HotReloadRunTiming timing = new HotReloadRunTiming();

            Assert.Throws<ArgumentOutOfRangeException>(() => timing.AddDetail("plan", -1));
        }

        /// <summary>
        /// What: a step without a name is refused, whether the name is empty or null.
        /// </summary>
        [Test]
        public void AddDetail_EmptyStep_Throws()
        {
            HotReloadRunTiming timing = new HotReloadRunTiming();

            Assert.Throws<ArgumentException>(() => timing.AddDetail(string.Empty, 1));
            Assert.Throws<ArgumentException>(() => timing.AddDetail(null, 1));
        }

        /// <summary>
        /// What: disposing a measured scope adds its elapsed time to the step.
        /// </summary>
        [Test]
        public void MeasureDetail_AddsTheElapsedOnDispose()
        {
            HotReloadRunTiming timing = new HotReloadRunTiming();

            using (timing.MeasureDetail("plan"))
            {
            }

            Assert.That(timing.Details.Count, Is.EqualTo(1));
            Assert.That(timing.Details[0].Key, Is.EqualTo("plan"));
            Assert.That(timing.Details[0].Value, Is.GreaterThanOrEqualTo(0));
        }

        /// <summary>
        /// What: a run that measured no step reports no steps.
        /// </summary>
        [Test]
        public void Details_WithoutAnyStep_IsEmpty()
        {
            HotReloadRunTiming timing = new HotReloadRunTiming();

            Assert.That(timing.Details, Is.Empty);
        }
    }
}
