using System;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for capturing the source snapshot once per domain.
    /// </summary>
    public class HotReloadSourceSnapshotCaptureTests
    {
        /// <summary>
        /// What: once a capture has completed, a later call does not run it again.
        /// </summary>
        [Test]
        public void EnsureCaptured_CalledTwice_RunsTheCaptureOnce()
        {
            int captureCount = 0;
            HotReloadSourceSnapshotCapture capture = new HotReloadSourceSnapshotCapture(() =>
            {
                captureCount++;
                return true;
            });

            capture.EnsureCaptured("test");
            capture.EnsureCaptured("test");

            Assert.That(captureCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a capture that throws passes the exception to the caller and is not marked done, so
        /// the next call runs it again; once that run completes, later calls do not.
        /// </summary>
        [Test]
        public void EnsureCaptured_WhenTheCaptureThrows_RunsItAgainOnTheNextCall()
        {
            int captureCount = 0;
            InvalidOperationException failure = new InvalidOperationException("The capture failed.");
            HotReloadSourceSnapshotCapture capture = new HotReloadSourceSnapshotCapture(() =>
            {
                captureCount++;
                if (captureCount == 1)
                {
                    throw failure;
                }

                return true;
            });

            InvalidOperationException thrown =
                Assert.Throws<InvalidOperationException>(() => capture.EnsureCaptured("test"));
            Assert.That(thrown, Is.SameAs(failure));

            Assert.DoesNotThrow(() => capture.EnsureCaptured("test"));
            Assert.That(captureCount, Is.EqualTo(2));

            capture.EnsureCaptured("test");
            Assert.That(captureCount, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a capture that runs to completion writes one captured entry naming its trigger and how long it took.
        /// </summary>
        [Test]
        public void EnsureCaptured_WhenTheCaptureRuns_WritesOneCapturedEntryWithItsTrigger()
        {
            VibeLogger.ClearMemoryLogs();
            HotReloadSourceSnapshotCapture capture = new HotReloadSourceSnapshotCapture(() => true);

            capture.EnsureCaptured(HotReloadConstants.SourceSnapshotCaptureTriggerDomainLoad);

            JObject context = HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogSourceSnapshotCaptured);
            Assert.That((string)context["trigger"], Is.EqualTo(HotReloadConstants.SourceSnapshotCaptureTriggerDomainLoad));
            Assert.That(context["captureMs"].Type, Is.EqualTo(JTokenType.Integer), "captureMs type");
            Assert.That((long)context["captureMs"], Is.GreaterThanOrEqualTo(0), "captureMs");
        }

        /// <summary>
        /// What: a call that finds the capture already done writes no second entry, so the entry keeps the first trigger.
        /// </summary>
        [Test]
        public void EnsureCaptured_WhenAlreadyCaptured_WritesNoSecondEntry()
        {
            VibeLogger.ClearMemoryLogs();
            HotReloadSourceSnapshotCapture capture = new HotReloadSourceSnapshotCapture(() => true);

            capture.EnsureCaptured(HotReloadConstants.SourceSnapshotCaptureTriggerDomainLoad);
            capture.EnsureCaptured(HotReloadConstants.SourceSnapshotCaptureTriggerFirstUpdateTick);

            JObject context = HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogSourceSnapshotCaptured);
            Assert.That((string)context["trigger"], Is.EqualTo(HotReloadConstants.SourceSnapshotCaptureTriggerDomainLoad));
        }

        /// <summary>
        /// What: a capture that throws writes no captured entry.
        /// </summary>
        [Test]
        public void EnsureCaptured_WhenTheCaptureThrows_WritesNoEntry()
        {
            VibeLogger.ClearMemoryLogs();
            HotReloadSourceSnapshotCapture capture = new HotReloadSourceSnapshotCapture(() =>
            {
                throw new InvalidOperationException("The capture failed.");
            });

            Assert.Throws<InvalidOperationException>(
                () => capture.EnsureCaptured(HotReloadConstants.SourceSnapshotCaptureTriggerDomainLoad));

            Assert.That(CountCapturedEntries(), Is.EqualTo(0));
        }

        /// <summary>
        /// What: a capture that saw no compilation assembly is not marked done and writes no entry,
        /// so the next call runs it again and the entry names that call's trigger.
        /// </summary>
        [Test]
        public void EnsureCaptured_WhenTheCaptureSawNoAssembly_RunsItAgainOnTheNextCall()
        {
            VibeLogger.ClearMemoryLogs();
            int captureCount = 0;
            HotReloadSourceSnapshotCapture capture = new HotReloadSourceSnapshotCapture(() =>
            {
                captureCount++;
                return captureCount > 1;
            });

            capture.EnsureCaptured(HotReloadConstants.SourceSnapshotCaptureTriggerDomainLoad);
            Assert.That(CountCapturedEntries(), Is.EqualTo(0), "entries after the first call");

            capture.EnsureCaptured(HotReloadConstants.SourceSnapshotCaptureTriggerFirstUpdateTick);

            Assert.That(captureCount, Is.EqualTo(2), "captures");
            JObject context = HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogSourceSnapshotCaptured);
            Assert.That((string)context["trigger"], Is.EqualTo(HotReloadConstants.SourceSnapshotCaptureTriggerFirstUpdateTick));
        }

        private static int CountCapturedEntries()
        {
            return JArray.Parse(VibeLogger.GetLogsForAi(HotReloadConstants.VibeLogSourceSnapshotCaptured)).Count;
        }
    }
}
