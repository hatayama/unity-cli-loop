using System;
using System.Globalization;

using Newtonsoft.Json.Linq;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Measures the domain reloads of this Editor session and records in a compile result how long the reload
    /// after that compile took. Once reloads have stayed at twice their fastest duration for a while, the result
    /// also recommends restarting the Editor, because that slowdown grows with the reload count and only a restart
    /// resets it.
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
        private readonly Func<bool> _isInPlayMode;

        /// <param name="isInPlayMode">Reports whether the Editor is in Play Mode or entering it.</param>
        internal DomainReloadDurationMonitor(IDomainReloadDurationStore store, Func<bool> isInPlayMode)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _isInPlayMode = isInPlayMode ?? throw new ArgumentNullException(nameof(isInPlayMode));
        }

        internal static DomainReloadDurationMonitor CreateForEditor()
        {
            return new DomainReloadDurationMonitor(
                new SessionStateDomainReloadDurationStore(),
                () => EditorApplication.isPlayingOrWillChangePlaymode);
        }

        /// <summary>
        /// Starts recording a domain reload. Call it from beforeAssemblyReload.
        /// </summary>
        internal void RecordReloadStarted(DateTime utcNow)
        {
            // Why Play Mode reloads are recorded but not measured: entering Play Mode or recompiling during Play
            // also backs up and restores the play state, so their cost would skew the baseline. They are still
            // recorded so that a compile result never reports an earlier reload in their place.
            bool isMeasured = !_isInPlayMode();
            _store.Save(_store.Load().WithPendingStart(utcNow.Ticks, isMeasured));
        }

        /// <summary>
        /// Ends the reload started by <see cref="RecordReloadStarted"/>. Call it from afterAssemblyReload, which
        /// Unity raises once it has restored the Editor's objects, so the span covers the whole reload.
        /// Does nothing when no reload was started, as on the Editor's first domain load.
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
            // Why a negative span counts as not measured: the system clock moved back during the reload.
            if (!state.PendingIsMeasured || seconds < 0d)
            {
                _store.Save(AddUnmeasuredReload(state, startedAtUtcTicks));
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
        /// Settles which reload <paramref name="compileResult"/> reports. Sets DomainReloadSeconds to the latest
        /// reload's duration when that reload started at or after <paramref name="reloadStartedNotBeforeUtcTicks"/>,
        /// and to null when no reload did or the latest one was not measured. When the latest reloads have stayed
        /// slow, also appends a restart recommendation to Warning and NextActions.
        /// Returns false, changing nothing, when the result already has DomainReloadSeconds.
        /// </summary>
        internal bool AttributeReloadTo(JObject compileResult, long reloadStartedNotBeforeUtcTicks)
        {
            UnityEngine.Debug.Assert(compileResult != null, "compileResult must not be null");

            if (compileResult.ContainsKey(DomainReloadSecondsKey))
            {
                return false;
            }

            DomainReloadDurationState state = _store.Load();
            bool latestReloadQualifies = state.LastReloadStartedAtUtcTicks.HasValue &&
                state.LastReloadStartedAtUtcTicks.Value >= reloadStartedNotBeforeUtcTicks &&
                state.LastReloadSeconds.HasValue;
            if (!latestReloadQualifies)
            {
                compileResult[DomainReloadSecondsKey] = JValue.CreateNull();
                return true;
            }

            double seconds = state.LastReloadSeconds.Value;
            compileResult[DomainReloadSecondsKey] = Math.Round(seconds, 2);
            if (state.SlowStreak >= RequiredSlowStreak)
            {
                AppendRestartAdvice(compileResult, seconds, state.BaselineSeconds.Value);
            }

            return true;
        }

        /// <summary>
        /// Whether a reload, measured or not, started at or after <paramref name="utcTicks"/> and has finished. Only a
        /// finished reload becomes the latest reload; one still in progress is kept apart until it ends.
        /// </summary>
        internal bool HasReloadStartedSince(long utcTicks)
        {
            DomainReloadDurationState state = _store.Load();
            return state.LastReloadStartedAtUtcTicks.HasValue && state.LastReloadStartedAtUtcTicks.Value >= utcTicks;
        }

        /// <summary>
        /// Sets DomainReloadSeconds to null, for a compile result that caused no reload or whose reload has not
        /// started yet. Returns false, changing nothing, when the result already has DomainReloadSeconds.
        /// </summary>
        internal bool AttributeNoReloadTo(JObject compileResult)
        {
            UnityEngine.Debug.Assert(compileResult != null, "compileResult must not be null");

            if (compileResult.ContainsKey(DomainReloadSecondsKey))
            {
                return false;
            }

            compileResult[DomainReloadSecondsKey] = JValue.CreateNull();
            return true;
        }

        private static DomainReloadDurationState AddUnmeasuredReload(
            DomainReloadDurationState state,
            long startedAtUtcTicks)
        {
            // The baseline and the slow streak stay as they were: agents often enter Play Mode between compiles,
            // and resetting the streak there would keep the recommendation from ever appearing.
            return new DomainReloadDurationState(
                pendingStartUtcTicks: null,
                pendingIsMeasured: false,
                state.MeasuredReloadCount,
                state.BaselineSeconds,
                state.SlowStreak,
                startedAtUtcTicks,
                lastReloadSeconds: null);
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
                pendingIsMeasured: false,
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
