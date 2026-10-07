using System;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Captures the source snapshot of the current compile once per domain, for whichever comes
    /// first: the Editor's first update tick after a domain reload, or a reload about to read
    /// a snapshot.
    /// </summary>
    internal sealed class HotReloadSourceSnapshotCapture
    {
        private readonly Action _capture;
        private bool _captured;

        internal HotReloadSourceSnapshotCapture(Action capture)
        {
            Debug.Assert(capture != null, "capture must not be null.");
            _capture = capture;
        }

        /// <summary>
        /// Runs the capture unless it already ran to completion in this domain. Main thread
        /// only. A capture that throws is not marked done, so the next caller runs it again.
        /// </summary>
        internal void EnsureCaptured()
        {
            if (_captured)
            {
                return;
            }

            _capture();
            // Marked only after the capture returns: a capture that threw may have left assemblies
            // without a snapshot, and marking it done first would keep every later reader from
            // capturing them.
            _captured = true;
        }
    }
}
