using System;
using System.Collections.Generic;
using System.Threading;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Tests the accumulator that records the steps of one request to the transform worker.
    /// </summary>
    public sealed class TransformWorkerRequestTimingTests
    {
        /// <summary>
        /// What: a step added twice, such as the two reads of one response, is summed into the
        /// entry where it was first seen, and the other steps keep their order.
        /// </summary>
        [Test]
        public void AddStep_SameStepTwice_SumsAndKeepsFirstSeenOrder()
        {
            TransformWorkerRequestTiming timing = new TransformWorkerRequestTiming();

            timing.AddStep("a", 5);
            timing.AddStep("b", 7);
            timing.AddStep("a", 3);

            Assert.That(timing.Steps, Is.EqualTo(new[]
            {
                new KeyValuePair<string, long>("a", 8),
                new KeyValuePair<string, long>("b", 7)
            }));
        }

        /// <summary>
        /// What: a step without a name is refused, since the vibe entry could not tell it apart.
        /// </summary>
        [Test]
        public void AddStep_EmptyStep_Throws()
        {
            TransformWorkerRequestTiming timing = new TransformWorkerRequestTiming();

            Assert.Throws<ArgumentException>(() => timing.AddStep(string.Empty, 1));
        }

        /// <summary>
        /// What: a negative span is refused, since elapsed time never is.
        /// </summary>
        [Test]
        public void AddStep_NegativeMilliseconds_Throws()
        {
            TransformWorkerRequestTiming timing = new TransformWorkerRequestTiming();

            Assert.Throws<ArgumentOutOfRangeException>(() => timing.AddStep("a", -1));
        }

        /// <summary>
        /// What: a measured scope disposed twice records its span once; the second dispose adds
        /// nothing to the step.
        /// </summary>
        [Test]
        public void Measure_DisposedTwice_AddsTheStepOnce()
        {
            TransformWorkerRequestTiming timing = new TransformWorkerRequestTiming();

            IDisposable scope = timing.Measure(TransformWorkerRequestTiming.ReadOutputStep);
            // Why sleep: a span of 0 ms added twice would still read 0, hiding a double count.
            Thread.Sleep(2);
            scope.Dispose();
            long afterFirstDispose = timing.Steps[0].Value;
            scope.Dispose();

            Assert.That(afterFirstDispose, Is.GreaterThanOrEqualTo(1), "first span");
            Assert.That(timing.Steps.Count, Is.EqualTo(1), "step count");
            Assert.That(timing.Steps[0].Key, Is.EqualTo(TransformWorkerRequestTiming.ReadOutputStep), "step");
            Assert.That(timing.Steps[0].Value, Is.EqualTo(afterFirstDispose), "ms after second dispose");
        }

        /// <summary>
        /// What: each conversation a request begins is counted, so a retried request reports two.
        /// </summary>
        [Test]
        public void MarkAttempt_CalledTwice_ReportsTwoAttempts()
        {
            TransformWorkerRequestTiming timing = new TransformWorkerRequestTiming();

            timing.MarkAttempt();
            timing.MarkAttempt();

            Assert.That(timing.Attempts, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a request starts out as not having started the worker, and marking it flips that.
        /// </summary>
        [Test]
        public void MarkWorkerStarted_SetsWorkerStarted()
        {
            TransformWorkerRequestTiming timing = new TransformWorkerRequestTiming();
            Assert.That(timing.WorkerStarted, Is.False, "initial");

            timing.MarkWorkerStarted();

            Assert.That(timing.WorkerStarted, Is.True, "after mark");
        }
    }
}
