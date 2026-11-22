using System;
using System.Globalization;

using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Measures every domain reload of this Editor session and adds the reload a compile caused to that compile's
    /// result. Once reloads have stayed at twice their fastest duration for a while, the result also recommends
    /// restarting the Editor, because that slowdown grows with the reload count and only a restart resets it.
    /// </summary>
    internal sealed class DomainReloadDurationMonitor
    {
        // Why skip the first reloads: right after the Editor starts, a reload can finish before the Editor has
        // objects to restore and runs far faster than any later one, which would make every later reload look slow.
        internal const int WarmUpReloadCount = 2;
        internal const double SlowFactor = 2d;
        // Why ten in a row: a busy machine (another Editor compiling, file indexing) can slow a few reloads in a
        // row, while the slowdown this looks for only grows, so a long run costs a few reloads of delay and keeps
        // a passing slowdown from recommending a restart.
        internal const int RequiredSlowStreak = 10;

        private const string DomainReloadSecondsKey = "DomainReloadSeconds";
        private const string WarningKey = "Warning";
        private const string NextActionsKey = "NextActions";

        private readonly IDomainReloadDurationStore _store;

        internal DomainReloadDurationMonitor(IDomainReloadDurationStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        internal static DomainReloadDurationMonitor CreateForEditor()
        {
            return new DomainReloadDurationMonitor(new SessionStateDomainReloadDurationStore());
        }

        /// <summary>
        /// Starts measuring a domain reload. Call it from beforeAssemblyReload.
        /// </summary>
        /// <param name="entersPlayMode">True when the reload enters Play Mode. That reload is not measured.</param>
        internal void RecordReloadStarted(DateTime utcNow, bool entersPlayMode)
        {
            // Why not measure Play Mode entry: that reload also backs up and restores the open scenes, so its cost
            // is not comparable with the reloads a compile causes and would skew the baseline.
            long? pendingStartUtcTicks = entersPlayMode ? (long?)null : utcNow.Ticks;
            _store.Save(_store.Load().WithPendingStart(pendingStartUtcTicks));
        }

        /// <summary>
        /// Ends the measurement started by <see cref="RecordReloadStarted"/>. Call it from afterAssemblyReload,
        /// which Unity raises once it has restored the Editor's objects, so the span covers the whole reload.
        /// Does nothing when no measured reload was started, as on the Editor's first domain load.
        /// </summary>
        internal void RecordReloadFinished(DateTime utcNow)
        {
            DomainReloadDurationState state = _store.Load();
            if (!state.PendingStartUtcTicks.HasValue)
            {
                return;
            }

            long startedAtUtcTicks = state.PendingStartUtcTicks.Value;
            double seconds = (utcNow.Ticks - startedAtUtcTicks) / (double)TimeSpan.TicksPerSecond;
            if (seconds < 0d)
            {
                // Why drop it: the system clock moved back during the reload, so the span means nothing.
                _store.Save(state.WithPendingStart(null));
                return;
            }

            DomainReloadDurationState measured = AddMeasuredReload(state, startedAtUtcTicks, seconds);
            _store.Save(measured);
            VibeLogger.LogInfo(
                "domain_reload_duration_measured",
                "Measured a domain reload.",
                new
                {
                    seconds,
                    baseline_seconds = measured.BaselineSeconds,
                    slow_streak = measured.SlowStreak,
                    measured_reload_count = measured.MeasuredReloadCount
                });
        }

        /// <summary>
        /// Adds the latest measured reload to <paramref name="compileResult"/> as DomainReloadSeconds when it started
        /// at or after <paramref name="resultCompletedAtUtcTicks"/>, which means the compile caused it. When the
        /// latest reloads have stayed slow, also appends a restart recommendation to Warning and NextActions.
        /// Leaves a result that already reports a reload unchanged.
        /// </summary>
        internal void AddLatestReloadTo(JObject compileResult, long resultCompletedAtUtcTicks)
        {
            UnityEngine.Debug.Assert(compileResult != null, "compileResult must not be null");

            if (compileResult.ContainsKey(DomainReloadSecondsKey))
            {
                return;
            }

            DomainReloadDurationState state = _store.Load();
            if (!state.LastReloadStartedAtUtcTicks.HasValue ||
                state.LastReloadStartedAtUtcTicks.Value < resultCompletedAtUtcTicks)
            {
                return;
            }

            double seconds = state.LastReloadSeconds.Value;
            compileResult[DomainReloadSecondsKey] = Math.Round(seconds, 2);
            if (state.SlowStreak < RequiredSlowStreak)
            {
                return;
            }

            AppendRestartAdvice(compileResult, seconds, state.BaselineSeconds.Value);
        }

        private static DomainReloadDurationState AddMeasuredReload(
            DomainReloadDurationState state,
            long startedAtUtcTicks,
            double seconds)
        {
            int measuredReloadCount = state.MeasuredReloadCount + 1;
            double? baselineSeconds = state.BaselineSeconds;
            int slowStreak = 0;
            if (measuredReloadCount > WarmUpReloadCount)
            {
                if (baselineSeconds.HasValue)
                {
                    // Compared before the baseline moves, so a new fastest reload ends the run instead of joining it.
                    slowStreak = seconds >= SlowFactor * baselineSeconds.Value ? state.SlowStreak + 1 : 0;
                    baselineSeconds = Math.Min(baselineSeconds.Value, seconds);
                }
                else
                {
                    baselineSeconds = seconds;
                }
            }

            return new DomainReloadDurationState(
                pendingStartUtcTicks: null,
                measuredReloadCount,
                baselineSeconds,
                slowStreak,
                startedAtUtcTicks,
                seconds);
        }

        private static void AppendRestartAdvice(JObject compileResult, double seconds, double baselineSeconds)
        {
            string latest = FormatSeconds(seconds);
            string baseline = FormatSeconds(baselineSeconds);
            string warning =
                $"Domain reloads in this Editor have taken at least twice their fastest {baseline}s for the last " +
                $"{RequiredSlowStreak} reloads (this one: {latest}s), and they keep growing until the Editor restarts. " +
                "Restarting it with `uloop launch -r` brings them back down, but discards unsaved scene changes, " +
                "so save them or ask the user first.";
            string existingWarning = compileResult[WarningKey]?.Type == JTokenType.String
                ? compileResult[WarningKey].Value<string>()
                : null;
            compileResult[WarningKey] = string.IsNullOrEmpty(existingWarning)
                ? warning
                : existingWarning + " " + warning;

            string nextAction =
                $"Restart the Editor with `uloop launch -r` to bring domain reloads back to about {baseline}s. " +
                "It discards unsaved scene changes: save them or ask the user first.";
            if (compileResult[NextActionsKey] is JArray nextActions)
            {
                nextActions.Add(nextAction);
                return;
            }

            compileResult[NextActionsKey] = new JArray(nextAction);
        }

        private static string FormatSeconds(double seconds)
        {
            return seconds.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
