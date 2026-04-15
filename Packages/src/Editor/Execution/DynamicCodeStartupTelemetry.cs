using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.uLoopMCP
{
    internal static class DynamicCodeStartupTelemetry
    {
        private static readonly object SyncRoot = new();

        private static long _serverReadyTimestamp;
        private static long _prewarmStateTimestamp;
        private static long _prewarmStartedTimestamp;
        private static long _prewarmFinishedTimestamp;
        private static string _prewarmState = "NotRequested";
        private static string _prewarmDetail = string.Empty;

        public static void MarkServerReady()
        {
            lock (SyncRoot)
            {
                _serverReadyTimestamp = Stopwatch.GetTimestamp();
                _prewarmStateTimestamp = 0;
                _prewarmStartedTimestamp = 0;
                _prewarmFinishedTimestamp = 0;
                _prewarmState = "NotRequested";
                _prewarmDetail = string.Empty;
            }
        }

        public static void MarkPrewarmQueued()
        {
            UpdatePrewarmState("Queued", string.Empty, false);
        }

        public static void MarkPrewarmStarted()
        {
            lock (SyncRoot)
            {
                _prewarmStartedTimestamp = Stopwatch.GetTimestamp();
            }

            UpdatePrewarmState("Running", string.Empty, false);
        }

        public static void MarkPrewarmCompleted()
        {
            UpdatePrewarmState("Completed", string.Empty, true);
        }

        public static void MarkPrewarmSkipped(string detail)
        {
            UpdatePrewarmState("Skipped", detail, true);
        }

        public static void MarkPrewarmYielded(string detail)
        {
            UpdatePrewarmState("Yielded", detail, true);
        }

        public static void MarkPrewarmFailed(string detail)
        {
            UpdatePrewarmState("Failed", detail, true);
        }

        public static List<string> CreateTimingEntries()
        {
            long serverReadyTimestamp;
            long prewarmStateTimestamp;
            long prewarmStartedTimestamp;
            long prewarmFinishedTimestamp;
            string prewarmState;
            string prewarmDetail;

            lock (SyncRoot)
            {
                serverReadyTimestamp = _serverReadyTimestamp;
                prewarmStateTimestamp = _prewarmStateTimestamp;
                prewarmStartedTimestamp = _prewarmStartedTimestamp;
                prewarmFinishedTimestamp = _prewarmFinishedTimestamp;
                prewarmState = _prewarmState;
                prewarmDetail = _prewarmDetail;
            }

            List<string> entries = new List<string>();
            if (serverReadyTimestamp > 0)
            {
                entries.Add($"[Perf] ServerReadyAge: {ToMilliseconds(serverReadyTimestamp, Stopwatch.GetTimestamp()):F1}ms");
            }

            entries.Add($"[Perf] WarmReady: {string.Equals(prewarmState, "Completed")}");
            entries.Add($"[Perf] PrewarmState: {prewarmState}");

            if (prewarmStateTimestamp > 0)
            {
                entries.Add($"[Perf] PrewarmStateAge: {ToMilliseconds(prewarmStateTimestamp, Stopwatch.GetTimestamp()):F1}ms");
            }

            if (prewarmStartedTimestamp > 0 && prewarmFinishedTimestamp > 0)
            {
                entries.Add($"[Perf] PrewarmDuration: {ToMilliseconds(prewarmStartedTimestamp, prewarmFinishedTimestamp):F1}ms");
            }

            if (!string.IsNullOrEmpty(prewarmDetail))
            {
                entries.Add($"[Perf] PrewarmDetail: {prewarmDetail}");
            }

            return entries;
        }

        internal static void Reset()
        {
            lock (SyncRoot)
            {
                _serverReadyTimestamp = 0;
                _prewarmStateTimestamp = 0;
                _prewarmStartedTimestamp = 0;
                _prewarmFinishedTimestamp = 0;
                _prewarmState = "NotRequested";
                _prewarmDetail = string.Empty;
            }
        }

        private static void UpdatePrewarmState(string state, string detail, bool terminal)
        {
            lock (SyncRoot)
            {
                _prewarmState = state;
                _prewarmDetail = detail ?? string.Empty;
                _prewarmStateTimestamp = Stopwatch.GetTimestamp();
                if (terminal)
                {
                    _prewarmFinishedTimestamp = _prewarmStateTimestamp;
                    return;
                }

                _prewarmFinishedTimestamp = 0;
            }
        }

        private static double ToMilliseconds(long startTimestamp, long endTimestamp)
        {
            long elapsedTicks = endTimestamp - startTimestamp;
            return elapsedTicks * 1000d / Stopwatch.Frequency;
        }
    }
}
