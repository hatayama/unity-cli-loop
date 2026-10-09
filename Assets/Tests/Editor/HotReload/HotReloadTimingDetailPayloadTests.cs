using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Tests the timing detail entry that breaks down the time outside an apply run's phases.
    /// </summary>
    public sealed class HotReloadTimingDetailPayloadTests
    {
        /// <summary>
        /// What: the entry carries each phase, the steps in order, and the part of the time outside
        /// the phases that no step covers.
        /// </summary>
        [Test]
        public void Build_ReportsThePhasesTheStepsAndTheUnaccountedRest()
        {
            HotReloadTimingBreakdown breakdown = new HotReloadTimingBreakdown(100, 50, 10, 1000);
            List<KeyValuePair<string, long>> details = new List<KeyValuePair<string, long>>
            {
                new KeyValuePair<string, long>("plan", 300),
                new KeyValuePair<string, long>("sibling_detect", 400),
            };

            HotReloadTimingDetailPayload payload = HotReloadTimingDetailPayload.Build(breakdown, details, 2);

            Assert.That(payload.TotalMs, Is.EqualTo(1000));
            Assert.That(payload.AnalysisMs, Is.EqualTo(100));
            Assert.That(payload.ShimCompileMs, Is.EqualTo(50));
            Assert.That(payload.PatchMs, Is.EqualTo(10));
            Assert.That(payload.OtherMs, Is.EqualTo(840));
            Assert.That(payload.UnaccountedMs, Is.EqualTo(140));
            Assert.That(payload.GroupCount, Is.EqualTo(2));
            Assert.That(payload.Steps.Count, Is.EqualTo(2));
            Assert.That(payload.Steps[0].Step, Is.EqualTo("plan"));
            Assert.That(payload.Steps[0].Ms, Is.EqualTo(300));
            Assert.That(payload.Steps[1].Step, Is.EqualTo("sibling_detect"));
            Assert.That(payload.Steps[1].Ms, Is.EqualTo(400));
        }

        /// <summary>
        /// What: steps that add up to more than the time outside the phases report a negative
        /// unaccounted time as it is, because that is the sign that a step overlaps a phase.
        /// </summary>
        [Test]
        public void Build_StepsOverlappingAPhase_ReportsANegativeUnaccounted()
        {
            HotReloadTimingBreakdown breakdown = new HotReloadTimingBreakdown(100, 50, 10, 1000);
            List<KeyValuePair<string, long>> details = new List<KeyValuePair<string, long>>
            {
                new KeyValuePair<string, long>("plan", 900),
            };

            HotReloadTimingDetailPayload payload = HotReloadTimingDetailPayload.Build(breakdown, details, 1);

            Assert.That(payload.UnaccountedMs, Is.EqualTo(-60));
        }

        /// <summary>
        /// What: a run that measured no step leaves all of the time outside the phases unaccounted.
        /// </summary>
        [Test]
        public void Build_WithoutSteps_ReportsOtherAsUnaccounted()
        {
            HotReloadTimingBreakdown breakdown = new HotReloadTimingBreakdown(100, 50, 10, 1000);

            HotReloadTimingDetailPayload payload = HotReloadTimingDetailPayload.Build(
                breakdown,
                new List<KeyValuePair<string, long>>(),
                0);

            Assert.That(payload.UnaccountedMs, Is.EqualTo(payload.OtherMs));
            Assert.That(payload.Steps, Is.Empty);
        }
    }
}
