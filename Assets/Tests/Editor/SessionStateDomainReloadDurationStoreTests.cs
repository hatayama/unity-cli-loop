using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests that the domain reload measurements survive the SessionState JSON round trip a domain reload goes through.
    /// </summary>
    [TestFixture]
    public sealed class SessionStateDomainReloadDurationStoreTests
    {
        private SessionStateDomainReloadDurationStore _store;
        private DomainReloadDurationState _originalState;

        [SetUp]
        public void SetUp()
        {
            _store = new SessionStateDomainReloadDurationStore();
            // Why keep the original: the store writes the live Editor's SessionState, whose measurements the running
            // Editor still uses after the test.
            _originalState = _store.Load();
        }

        [TearDown]
        public void TearDown()
        {
            _store.Save(_originalState);
        }

        [Test]
        public void SaveThenLoad_WithAReloadInProgress_KeepsEveryField()
        {
            // Verifies a measured reload's start written before the reload is read back intact after it, together
            // with the finished reloads, a measured one and one without a duration.
            DomainReloadDurationState saved = new DomainReloadDurationState(
                pendingStartUtcTicks: 638000000000000000L,
                pendingIsMeasured: true,
                measuredReloadCount: 12,
                baselineSeconds: 1.25d,
                slowStreak: 4,
                recentReloads: new[]
                {
                    new DomainReloadRecord(637999999990000000L, 2.75d, 1.25d, 4),
                    new DomainReloadRecord(637999999995000000L, null, null, 0)
                });

            _store.Save(saved);
            DomainReloadDurationState loaded = _store.Load();

            Assert.That(loaded.PendingStartUtcTicks, Is.EqualTo(saved.PendingStartUtcTicks));
            Assert.That(loaded.PendingIsMeasured, Is.True);
            Assert.That(loaded.MeasuredReloadCount, Is.EqualTo(12));
            Assert.That(loaded.BaselineSeconds, Is.EqualTo(1.25d));
            Assert.That(loaded.SlowStreak, Is.EqualTo(4));
            Assert.That(loaded.RecentReloads, Has.Length.EqualTo(2));
            Assert.That(loaded.RecentReloads[0].StartedAtUtcTicks, Is.EqualTo(637999999990000000L));
            Assert.That(loaded.RecentReloads[0].Seconds, Is.EqualTo(2.75d));
            Assert.That(loaded.RecentReloads[0].BaselineSeconds, Is.EqualTo(1.25d));
            Assert.That(loaded.RecentReloads[0].SlowStreak, Is.EqualTo(4));
            Assert.That(loaded.RecentReloads[1].StartedAtUtcTicks, Is.EqualTo(637999999995000000L));
            Assert.That(loaded.RecentReloads[1].Seconds.HasValue, Is.False);
        }

        [Test]
        public void SaveThenLoad_WithoutAReloadInProgress_KeepsTheMissingStart()
        {
            // Verifies no reload in progress is not read back as a measured one.
            DomainReloadDurationState saved = new DomainReloadDurationState(
                pendingStartUtcTicks: null,
                pendingIsMeasured: false,
                measuredReloadCount: 3,
                baselineSeconds: 1d,
                slowStreak: 0,
                recentReloads: System.Array.Empty<DomainReloadRecord>());

            _store.Save(saved);
            DomainReloadDurationState loaded = _store.Load();

            Assert.That(loaded.PendingStartUtcTicks.HasValue, Is.False);
            Assert.That(loaded.PendingIsMeasured, Is.False);
            Assert.That(loaded.RecentReloads, Is.Empty);
        }
    }
}
