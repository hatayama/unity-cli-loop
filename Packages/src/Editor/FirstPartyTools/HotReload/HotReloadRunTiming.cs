using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Sums the milliseconds one apply run spends in each phase across all of its groups.
    /// Also sums the named steps outside the phases, in the order each was first seen. The
    /// response does not carry them; only the hot_reload_timing_detail vibe entry reads them.
    /// </summary>
    /// <remarks>
    /// Why an accumulator the run hands down: group results are per file and carry no group-level
    /// data, so the run hands this accumulator down to the group processor instead. Each run makes
    /// its own, so no long-lived object keeps the state of a run.
    /// </remarks>
    internal sealed class HotReloadRunTiming
    {
        private readonly List<KeyValuePair<string, long>> _details = new List<KeyValuePair<string, long>>();
        private long _analysisMs;
        private long _shimCompileMs;
        private long _patchMs;

        public IReadOnlyList<KeyValuePair<string, long>> Details => _details;

        public void AddAnalysis(long milliseconds)
        {
            _analysisMs += HotReloadTimingBreakdown.RequireElapsed(milliseconds, nameof(milliseconds));
        }

        public void AddShimCompile(long milliseconds)
        {
            _shimCompileMs += HotReloadTimingBreakdown.RequireElapsed(milliseconds, nameof(milliseconds));
        }

        public void AddPatch(long milliseconds)
        {
            _patchMs += HotReloadTimingBreakdown.RequireElapsed(milliseconds, nameof(milliseconds));
        }

        public void AddDetail(string step, long milliseconds)
        {
            if (string.IsNullOrEmpty(step))
            {
                throw new ArgumentException("step must not be null or empty.", nameof(step));
            }

            long elapsed = HotReloadTimingBreakdown.RequireElapsed(milliseconds, nameof(milliseconds));
            int index = _details.FindIndex(pair => pair.Key == step);
            if (index < 0)
            {
                _details.Add(new KeyValuePair<string, long>(step, elapsed));
                return;
            }

            _details[index] = new KeyValuePair<string, long>(step, _details[index].Value + elapsed);
        }

        /// <summary>
        /// Measures the wall-clock time until the returned scope is disposed and adds it to the step.
        /// </summary>
        public IDisposable MeasureDetail(string step)
        {
            return new DetailScope(this, step);
        }

        public HotReloadTimingBreakdown Complete(long totalMs)
        {
            return new HotReloadTimingBreakdown(_analysisMs, _shimCompileMs, _patchMs, totalMs);
        }

        private sealed class DetailScope : IDisposable
        {
            private readonly HotReloadRunTiming _timing;
            private readonly string _step;
            private readonly Stopwatch _watch = Stopwatch.StartNew();
            private bool _disposed;

            public DetailScope(HotReloadRunTiming timing, string step)
            {
                _timing = timing;
                _step = step;
            }

            public void Dispose()
            {
                // Why only once: a second dispose would count the same span twice.
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _watch.Stop();
                _timing.AddDetail(_step, _watch.ElapsedMilliseconds);
            }
        }
    }
}
