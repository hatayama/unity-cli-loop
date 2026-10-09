using System;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Captures the source snapshot of the current compile once per domain, at the end of the
    /// domain load. When that capture throws or Unity listed no compilation assembly yet, the
    /// first update tick or the hot reload apply entry captures again.
    /// </summary>
    internal sealed class HotReloadSourceSnapshotCapture
    {
        private readonly Func<bool> _capture;
        private bool _captured;

        /// <param name="capture">
        /// Captures the snapshot and returns whether Unity listed at least one compilation assembly.
        /// </param>
        internal HotReloadSourceSnapshotCapture(Func<bool> capture)
        {
            Debug.Assert(capture != null, "capture must not be null.");
            _capture = capture;
        }

        /// <summary>
        /// Runs the capture unless it already ran to completion in this domain, and records
        /// <paramref name="trigger"/> when this call completes it. Main thread only. A capture that
        /// throws or saw no compilation assembly is not marked done, so the next caller runs it again.
        /// </summary>
        internal void EnsureCaptured(string trigger)
        {
            Debug.Assert(!string.IsNullOrEmpty(trigger), "trigger must not be null or empty.");
            if (_captured)
            {
                return;
            }

            Stopwatch watch = Stopwatch.StartNew();
            // Marked only after the capture returns: a capture that threw may have left assemblies
            // without a snapshot, and marking it done first would keep every later reader from
            // capturing them.
            bool sawAssemblies = _capture();
            // Why an empty list is not done: Unity lists none while it compiles, and a domain marked
            // captured from an empty list would never be captured by any of its readers.
            if (!sawAssemblies)
            {
                return;
            }

            _captured = true;
            VibeLogger.LogInfo(
                HotReloadConstants.VibeLogSourceSnapshotCaptured,
                "Hot reload captured the source snapshot of this domain.",
                new { trigger, captureMs = watch.ElapsedMilliseconds });
        }
    }
}
