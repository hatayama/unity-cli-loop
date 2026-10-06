using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

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
            HotReloadSourceSnapshotCapture capture = new HotReloadSourceSnapshotCapture(() => captureCount++);

            capture.EnsureCaptured();
            capture.EnsureCaptured();

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
            });

            InvalidOperationException thrown =
                Assert.Throws<InvalidOperationException>(() => capture.EnsureCaptured());
            Assert.That(thrown, Is.SameAs(failure));

            Assert.DoesNotThrow(() => capture.EnsureCaptured());
            Assert.That(captureCount, Is.EqualTo(2));

            capture.EnsureCaptured();
            Assert.That(captureCount, Is.EqualTo(2));
        }
    }
}
