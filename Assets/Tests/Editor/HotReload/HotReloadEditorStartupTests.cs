using System;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the source snapshot capture the Editor startup runs before this
    /// domain serves any command. Works on a capture installed in place of the production one.
    /// </summary>
    public class HotReloadEditorStartupTests
    {
        /// <summary>
        /// What: the capture before serving commands runs the installed capture once and records the domain-load trigger.
        /// </summary>
        [Test]
        public void CaptureSourceSnapshotBeforeServingCommands_RunsTheInstalledCaptureOnceWithTheDomainLoadTrigger()
        {
            VibeLogger.ClearMemoryLogs();
            int captureCount = 0;
            HotReloadSourceSnapshotCapture capture = new HotReloadSourceSnapshotCapture(() =>
            {
                captureCount++;
                return true;
            });

            using (HotReloadServicesTestScope.BeginWithSourceSnapshotCapture(capture))
            {
                HotReloadEditorStartup.CaptureSourceSnapshotBeforeServingCommands();
            }

            Assert.That(captureCount, Is.EqualTo(1), "captures");
            JObject context = HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogSourceSnapshotCaptured);
            Assert.That((string)context["trigger"], Is.EqualTo(HotReloadConstants.SourceSnapshotCaptureTriggerDomainLoad));
        }

        /// <summary>
        /// What: a capture that throws before serving commands passes the exception on and stays
        /// undone, so the first update tick's capture runs it again.
        /// </summary>
        [Test]
        public void CaptureSourceSnapshotBeforeServingCommands_WhenTheCaptureThrows_ThrowsAndLeavesItForTheNextCall()
        {
            VibeLogger.ClearMemoryLogs();
            int captureCount = 0;
            HotReloadSourceSnapshotCapture capture = new HotReloadSourceSnapshotCapture(() =>
            {
                captureCount++;
                if (captureCount == 1)
                {
                    throw new InvalidOperationException("The capture failed.");
                }

                return true;
            });

            using (HotReloadServicesTestScope.BeginWithSourceSnapshotCapture(capture))
            {
                Assert.Throws<InvalidOperationException>(
                    () => HotReloadEditorStartup.CaptureSourceSnapshotBeforeServingCommands());

                HotReloadCompositionRoot.Services.SourceSnapshotCapture.EnsureCaptured(
                    HotReloadConstants.SourceSnapshotCaptureTriggerFirstUpdateTick);
            }

            Assert.That(captureCount, Is.EqualTo(2), "captures");
        }
    }
}
