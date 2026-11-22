using System;

using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests how domain reload durations are measured and when a compile result recommends restarting the Editor.
    /// </summary>
    [TestFixture]
    public sealed class DomainReloadDurationMonitorTests
    {
        private const string DomainReloadSecondsKey = "DomainReloadSeconds";
        private const string WarningKey = "Warning";
        private const string NextActionsKey = "NextActions";
        private const string RestartCommand = "uloop launch -r";

        private DomainReloadDurationMonitor _monitor;
        private DateTime _now;
        private bool _isInPlayMode;

        [SetUp]
        public void SetUp()
        {
            _isInPlayMode = false;
            _monitor = new DomainReloadDurationMonitor(new InMemoryDomainReloadDurationStore(), () => _isInPlayMode);
            _now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        [Test]
        public void AttributeReloadTo_WhenNoReloadWasRecorded_SetsNullSeconds()
        {
            // Verifies the Editor's initial domain load, which has no reload start, is never reported.
            _monitor.RecordReloadFinished(_now);

            JObject result = AttributeReloadToResultCompletedAt(_now.AddSeconds(-1));

            Assert.That(result[DomainReloadSecondsKey]?.Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public void AttributeReloadTo_AfterAReloadThatStartedAfterTheResult_SetsItsSeconds()
        {
            // Verifies a compile whose result was stored before its reload reports that reload's duration.
            DateTime resultCompletedAt = _now;
            RecordReloads(1, 1.5d);

            JObject result = AttributeReloadToResultCompletedAt(resultCompletedAt);

            Assert.That(result[DomainReloadSecondsKey]?.Value<double>(), Is.EqualTo(1.5d));
            Assert.That(result.ContainsKey(WarningKey), Is.False);
            Assert.That(result.ContainsKey(NextActionsKey), Is.False);
        }

        [Test]
        public void AttributeReloadTo_WhenTheLastReloadStartedBeforeTheResult_SetsNullSecondsWithoutAdvice()
        {
            // Verifies a compile that caused no reload gets neither a stale duration nor a stale restart warning.
            RecordReloads(3, 1d);
            RecordReloads(10, 2d);

            JObject result = AttributeReloadToResultCompletedAt(_now);

            Assert.That(result[DomainReloadSecondsKey]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(result.ContainsKey(WarningKey), Is.False);
        }

        [Test]
        public void AttributeReloadTo_AfterNineSlowReloadsInARow_DoesNotRecommendRestarting()
        {
            // Verifies nine reloads at twice the baseline are not yet enough to recommend a restart.
            RecordReloads(3, 1d);
            DateTime resultCompletedAt = _now;
            RecordReloads(9, 2d);

            JObject result = AttributeReloadToResultCompletedAt(resultCompletedAt);

            Assert.That(result[DomainReloadSecondsKey]?.Value<double>(), Is.EqualTo(2d));
            Assert.That(result.ContainsKey(WarningKey), Is.False);
            Assert.That(result.ContainsKey(NextActionsKey), Is.False);
        }

        [Test]
        public void AttributeReloadTo_AfterTenSlowReloadsInARow_RecommendsRestarting()
        {
            // Verifies the tenth reload in a row at twice the baseline recommends restarting with uloop launch -r.
            RecordReloads(3, 1d);
            RecordReloads(9, 2d);
            DateTime resultCompletedAt = _now;
            RecordReloads(1, 2d);

            JObject result = AttributeReloadToResultCompletedAt(resultCompletedAt);

            Assert.That(result[WarningKey]?.Value<string>(), Does.Contain(RestartCommand));
            Assert.That(result[WarningKey]?.Value<string>(), Does.Contain("unsaved"));
            Assert.That(result[NextActionsKey]?.ToObject<string[]>(), Has.Some.Contains(RestartCommand));
        }

        [Test]
        public void AttributeReloadTo_WhenAReloadBelowTwiceTheBaselineBreaksTheRun_DoesNotRecommendRestarting()
        {
            // Verifies one reload under twice the baseline restarts the count of slow reloads in a row.
            RecordReloads(3, 1d);
            RecordReloads(9, 2d);
            RecordReloads(1, 1.9d);
            RecordReloads(8, 2d);
            DateTime resultCompletedAt = _now;
            RecordReloads(1, 2d);

            JObject result = AttributeReloadToResultCompletedAt(resultCompletedAt);

            Assert.That(result.ContainsKey(WarningKey), Is.False);
        }

        [Test]
        public void AttributeReloadTo_WhenTheFirstTwoReloadsWereFast_DoesNotUseThemAsTheBaseline()
        {
            // Verifies the first two reloads after the Editor starts, which can be unusually fast, set no baseline.
            RecordReloads(2, 0.4d);
            RecordReloads(11, 1d);
            DateTime resultCompletedAt = _now;
            RecordReloads(1, 1d);

            JObject result = AttributeReloadToResultCompletedAt(resultCompletedAt);

            Assert.That(result.ContainsKey(WarningKey), Is.False);
        }

        [Test]
        public void AttributeReloadTo_WhenAFasterReloadArrives_MeasuresLaterReloadsAgainstIt()
        {
            // Verifies the baseline drops to the fastest reload, so 1.7s counts as slow against a 0.8s reload.
            RecordReloads(3, 1d);
            RecordReloads(5, 2d);
            RecordReloads(1, 0.8d);
            RecordReloads(9, 1.7d);
            DateTime resultCompletedAt = _now;
            RecordReloads(1, 1.7d);

            JObject result = AttributeReloadToResultCompletedAt(resultCompletedAt);

            Assert.That(result[WarningKey]?.Value<string>(), Does.Contain(RestartCommand));
        }

        [Test]
        public void AttributeReloadTo_WhenTheLatestReloadRanInPlayMode_SetsNullSecondsInsteadOfAnEarlierReload()
        {
            // Verifies a Play Mode reload is not measured and an earlier measured reload does not stand in for it.
            RecordReloads(3, 1d);
            RecordReloads(10, 2d);
            DateTime resultCompletedAt = _now;
            _isInPlayMode = true;
            RecordReloads(1, 3d);

            JObject result = AttributeReloadToResultCompletedAt(DateTime.MinValue);
            JObject resultAfterCompletion = AttributeReloadToResultCompletedAt(resultCompletedAt);

            Assert.That(result[DomainReloadSecondsKey]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(result.ContainsKey(WarningKey), Is.False);
            Assert.That(resultAfterCompletion[DomainReloadSecondsKey]?.Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public void AttributeReloadTo_WhenAPlayModeReloadInterruptsTheSlowRun_StillRecommendsRestarting()
        {
            // Verifies a Play Mode reload between compiles leaves the baseline and the slow streak as they were.
            RecordReloads(3, 1d);
            RecordReloads(9, 2d);
            _isInPlayMode = true;
            RecordReloads(1, 0.5d);
            _isInPlayMode = false;
            DateTime resultCompletedAt = _now;
            RecordReloads(1, 2d);

            JObject result = AttributeReloadToResultCompletedAt(resultCompletedAt);

            Assert.That(result[WarningKey]?.Value<string>(), Does.Contain(RestartCommand));
        }

        [Test]
        public void AttributeReloadTo_WhenAReloadStartedButAPlayModeReloadFollowed_DoesNotMeasureFromTheOlderStart()
        {
            // Verifies a reload start that never finished is replaced by the next start instead of being measured.
            DateTime resultCompletedAt = _now.AddSeconds(-1);
            _monitor.RecordReloadStarted(_now);
            _isInPlayMode = true;
            _monitor.RecordReloadStarted(_now.AddSeconds(2));
            _monitor.RecordReloadFinished(_now.AddSeconds(5));

            JObject result = AttributeReloadToResultCompletedAt(resultCompletedAt);

            Assert.That(result[DomainReloadSecondsKey]?.Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public void AttributeReloadTo_WhenTheResultAlreadyHasAWarningAndNextActions_AppendsToThem()
        {
            // Verifies the restart advice keeps the Play Mode warning and the corrective steps already in the result.
            RecordReloads(3, 1d);
            RecordReloads(9, 2d);
            DateTime resultCompletedAt = _now;
            RecordReloads(1, 2d);
            JObject result = new JObject
            {
                [WarningKey] = "Play Mode was stopped.",
                [NextActionsKey] = new JArray("Run get-logs.")
            };

            _monitor.AttributeReloadTo(result, resultCompletedAt.Ticks);

            Assert.That(result[WarningKey]?.Value<string>(), Does.StartWith("Play Mode was stopped. "));
            Assert.That(result[WarningKey]?.Value<string>(), Does.Contain(RestartCommand));
            string[] nextActions = result[NextActionsKey]?.ToObject<string[]>();
            Assert.That(nextActions, Has.Length.EqualTo(2));
            Assert.That(nextActions[0], Is.EqualTo("Run get-logs."));
            Assert.That(nextActions[1], Does.Contain(RestartCommand));
        }

        [Test]
        public void AttributeReloadTo_WhenTheResultAlreadyReportsAReload_LeavesItUnchanged()
        {
            // Verifies a result whose reload is settled is not given a second duration or warning.
            RecordReloads(3, 1d);
            RecordReloads(9, 2d);
            DateTime resultCompletedAt = _now;
            RecordReloads(1, 2d);
            JObject result = new JObject { [DomainReloadSecondsKey] = 3d };

            bool changed = _monitor.AttributeReloadTo(result, resultCompletedAt.Ticks);

            Assert.That(changed, Is.False);
            Assert.That(result[DomainReloadSecondsKey]?.Value<double>(), Is.EqualTo(3d));
            Assert.That(result.ContainsKey(WarningKey), Is.False);
        }

        [Test]
        public void AttributeNoReloadTo_AfterSlowReloads_SetsNullSecondsWithoutAdvice()
        {
            // Verifies a compile that cannot have reloaded reports no reload even when reloads have been slow.
            DateTime resultCompletedAt = _now;
            RecordReloads(3, 1d);
            RecordReloads(10, 2d);
            JObject result = new JObject { ["Success"] = false };

            bool changed = _monitor.AttributeNoReloadTo(result);

            Assert.That(changed, Is.True);
            Assert.That(result[DomainReloadSecondsKey]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(result.ContainsKey(WarningKey), Is.False);
            Assert.That(_monitor.AttributeReloadTo(result, resultCompletedAt.Ticks), Is.False);
        }

        private void RecordReloads(int count, double seconds)
        {
            for (int i = 0; i < count; i++)
            {
                _now = _now.AddSeconds(1);
                _monitor.RecordReloadStarted(_now);
                _now = _now.AddSeconds(seconds);
                _monitor.RecordReloadFinished(_now);
            }
        }

        private JObject AttributeReloadToResultCompletedAt(DateTime resultCompletedAt)
        {
            JObject result = new JObject { ["Success"] = true };
            _monitor.AttributeReloadTo(result, resultCompletedAt.Ticks);
            return result;
        }
    }
}
