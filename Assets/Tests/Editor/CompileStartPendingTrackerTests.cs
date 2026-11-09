using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how one compile request publishes its pending-start seconds to the compile status report
    /// and withdraws that publication when the request ends.
    /// </summary>
    [TestFixture]
    public sealed class CompileStartPendingTrackerTests
    {
        private Func<double> _originalReader;

        [SetUp]
        public void SetUp()
        {
            _originalReader = CompileStartPendingCoordination.ReadPendingSeconds;
            CompileStartPendingCoordination.ReadPendingSeconds = null;
        }

        [TearDown]
        public void TearDown()
        {
            CompileStartPendingCoordination.ReadPendingSeconds = _originalReader;
        }

        /// <summary>
        /// Verifies Begin publishes a reader that reports zero pending seconds and no observed start.
        /// </summary>
        [Test]
        public void Begin_PublishesReaderReportingZero()
        {
            CompileStartPendingTracker tracker = new CompileStartPendingTracker();

            tracker.Begin();

            Assert.That(CompileStartPendingCoordination.ReadPendingSeconds, Is.Not.Null);
            Assert.That(CompileStartPendingCoordination.ReadPendingSeconds(), Is.EqualTo(0d));
            Assert.That(tracker.HasCompilationStarted, Is.False);
        }

        /// <summary>
        /// Verifies pending milliseconds reported by the watchdog are exposed as seconds through the published reader.
        /// </summary>
        [Test]
        public void ReportPending_ExposesSecondsThroughPublishedReader()
        {
            CompileStartPendingTracker tracker = new CompileStartPendingTracker();
            tracker.Begin();

            tracker.ReportPending(12500);

            Assert.That(CompileStartPendingCoordination.ReadPendingSeconds(), Is.EqualTo(12.5d));
        }

        /// <summary>
        /// Verifies the compilationStarted event marks the start and resets the pending seconds to zero.
        /// </summary>
        [Test]
        public void MarkStarted_ResetsPendingSecondsAndRecordsStart()
        {
            CompileStartPendingTracker tracker = new CompileStartPendingTracker();
            tracker.Begin();
            tracker.ReportPending(12500);

            tracker.MarkStarted();

            Assert.That(tracker.HasCompilationStarted, Is.True);
            Assert.That(CompileStartPendingCoordination.ReadPendingSeconds(), Is.EqualTo(0d));
        }

        /// <summary>
        /// Verifies Begin on a reused tracker forgets pending time the previous request still carried.
        /// </summary>
        [Test]
        public void Begin_WhilePreviousRequestStillHasPendingTime_ResetsPendingSeconds()
        {
            CompileStartPendingTracker tracker = new CompileStartPendingTracker();
            tracker.Begin();
            tracker.ReportPending(3000);

            tracker.Begin();

            Assert.That(CompileStartPendingCoordination.ReadPendingSeconds(), Is.EqualTo(0d));
        }

        /// <summary>
        /// Verifies Begin on a reused tracker forgets that the previous request had started compiling.
        /// </summary>
        [Test]
        public void Begin_AfterPreviousRequestStarted_ResetsStartedState()
        {
            CompileStartPendingTracker tracker = new CompileStartPendingTracker();
            tracker.Begin();
            tracker.MarkStarted();

            tracker.Begin();

            Assert.That(tracker.HasCompilationStarted, Is.False);
        }

        /// <summary>
        /// Verifies End withdraws the reader this tracker published.
        /// </summary>
        [Test]
        public void End_WhenOwnReaderIsPublished_ClearsPublication()
        {
            CompileStartPendingTracker tracker = new CompileStartPendingTracker();
            tracker.Begin();

            tracker.End();

            Assert.That(CompileStartPendingCoordination.ReadPendingSeconds, Is.Null);
        }

        /// <summary>
        /// Verifies End leaves a reader published by a newer request untouched.
        /// </summary>
        [Test]
        public void End_WhenAnotherReaderIsPublished_LeavesItInPlace()
        {
            CompileStartPendingTracker staleTracker = new CompileStartPendingTracker();
            CompileStartPendingTracker currentTracker = new CompileStartPendingTracker();
            staleTracker.Begin();
            currentTracker.Begin();
            currentTracker.ReportPending(4000);

            staleTracker.End();

            Assert.That(CompileStartPendingCoordination.ReadPendingSeconds, Is.Not.Null);
            Assert.That(CompileStartPendingCoordination.ReadPendingSeconds(), Is.EqualTo(4d));
        }
    }
}
