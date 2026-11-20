using System;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Tracks, for one compile request, whether Unity has raised compilationStarted and how long the request
    /// has been accepted without it, and publishes that pending time to the compile status report.
    /// </summary>
    internal sealed class CompileStartPendingTracker
    {
        private readonly Func<double> _readPendingSeconds;
        private bool _compilationStarted;
        private int _pendingMs;

        internal CompileStartPendingTracker()
        {
            // Why one delegate instance: End must recognise its own publication so a stale request
            // never withdraws the reader a newer request published.
            _readPendingSeconds = ReadPendingSeconds;
        }

        internal bool HasCompilationStarted => _compilationStarted;

        /// <summary>
        /// Starts tracking a new compile request and publishes its pending-start reader.
        /// </summary>
        internal void Begin()
        {
            _compilationStarted = false;
            _pendingMs = 0;
            CompileStartPendingCoordination.ReadPendingSeconds = _readPendingSeconds;
        }

        /// <summary>
        /// Records the compilationStarted callback; from here on the request is compiling, not pending.
        /// </summary>
        internal void MarkStarted()
        {
            _compilationStarted = true;
            _pendingMs = 0;
        }

        /// <summary>
        /// Records the watchdog's latest count of main-thread-ticking milliseconds spent pending.
        /// </summary>
        internal void ReportPending(int pendingMs)
        {
            UnityEngine.Debug.Assert(pendingMs >= 0, "pendingMs must not be negative");
            _pendingMs = pendingMs;
        }

        /// <summary>
        /// Stops tracking and withdraws the published reader if it is still this tracker's.
        /// </summary>
        internal void End()
        {
            _pendingMs = 0;
            if (ReferenceEquals(CompileStartPendingCoordination.ReadPendingSeconds, _readPendingSeconds))
            {
                CompileStartPendingCoordination.ReadPendingSeconds = null;
            }
        }

        private double ReadPendingSeconds()
        {
            return _pendingMs / 1000.0;
        }
    }
}
