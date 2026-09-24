using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure coverage for wired-value persistence: what it records, what it gives back to the host
    /// that replaces a recorded one, and how each value that does not come back is reported once.
    /// </summary>
    /// <remarks>
    /// The resolver is a fake, so a "scene reload" here is a second host object that the fake
    /// names with the first host's identity.
    /// </remarks>
    public class HotReloadWiredValuePersistenceTests
    {
        private const string HostIdentity = "scene:Main|path:Host[0]|component:Ns.Host|index:0";
        private const string FieldKey = "Ns.Host::target";
        private const string OtherFieldKey = "Ns.Host::speed";
        private const string TargetIdentity = "scene:Main|path:Target[1]";

        private FakeResolver _resolver;
        private HotReloadWiredValuePersistence _persistence;

        [SetUp]
        public void SetUp()
        {
            _resolver = new FakeResolver();
            _persistence = new HotReloadWiredValuePersistence(_resolver);
        }

        /// <summary>
        /// What: a host the resolver cannot name (a plain C# object) is not recorded.
        /// </summary>
        [Test]
        public void Record_HostWithoutIdentity_IsNotRecorded()
        {
            _persistence.Record(new PersistenceHost(), FieldKey, 1);

            Assert.That(_persistence.Ledger.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a plain value wired into one host comes back to a different host object with the
        /// same identity, and counts as restored.
        /// </summary>
        [Test]
        public void TryRestore_PlainValue_ReturnsItToTheReplacementHost()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);

            Assert.That(_persistence.TryRestore(NamedHost(), FieldKey, out object value), Is.True);

            Assert.That(value, Is.EqualTo(7));
            Assert.That(_persistence.Report.RestoredCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a scene-object value is remembered by identity and resolved again on restore.
        /// </summary>
        [Test]
        public void TryRestore_SceneObjectValue_ResolvesThroughTheResolver()
        {
            _persistence.Record(NamedHost(), FieldKey, new SceneRef(TargetIdentity));
            object replacementTarget = new object();
            _resolver.Resolvable[TargetIdentity] = replacementTarget;

            Assert.That(_persistence.TryRestore(NamedHost(), FieldKey, out object value), Is.True);

            Assert.That(value, Is.SameAs(replacementTarget));
            Assert.That(_persistence.Report.RestoredCount, Is.EqualTo(1));
            Assert.That(_persistence.Report.Failures, Is.Empty);
        }

        /// <summary>
        /// What: a scene-object value that no longer resolves is not restored, and the failure
        /// names the host, the field, and the resolver's reason.
        /// </summary>
        [Test]
        public void TryRestore_UnresolvableSceneObject_ReturnsFalseAndReportsTheReason()
        {
            _persistence.Record(NamedHost(), FieldKey, new SceneRef(TargetIdentity));

            Assert.That(_persistence.TryRestore(NamedHost(), FieldKey, out object value), Is.False);

            Assert.That(value, Is.Null);
            Assert.That(_persistence.Report.RestoredCount, Is.EqualTo(0));
            Assert.That(_persistence.Report.Failures.Count, Is.EqualTo(1));
            Assert.That(_persistence.Report.Failures[0].HostIdentity, Is.EqualTo(HostIdentity));
            Assert.That(_persistence.Report.Failures[0].StoreFieldKey, Is.EqualTo(FieldKey));
            Assert.That(_persistence.Report.Failures[0].Reason, Is.EqualTo(FakeResolver.NotFoundReason));
        }

        /// <summary>
        /// What: a read that keeps failing for the same host and field (the read-back path creates
        /// no slot, so it asks every time) is reported once.
        /// </summary>
        [Test]
        public void TryRestore_SameFailureThreeTimes_IsReportedOnce()
        {
            _persistence.Record(NamedHost(), FieldKey, new SceneRef(TargetIdentity));

            _persistence.TryRestore(NamedHost(), FieldKey, out _);
            _persistence.TryRestore(NamedHost(), FieldKey, out _);
            _persistence.TryRestore(NamedHost(), FieldKey, out _);

            Assert.That(_persistence.Report.Failures.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a value recorded as unrestorable is not restored, and the failure carries the
        /// reason given when it was recorded.
        /// </summary>
        [Test]
        public void TryRestore_UnrestorableValue_ReturnsFalseWithTheRecordedReason()
        {
            _persistence.Record(NamedHost(), FieldKey, new RuntimeOnlyObject());

            Assert.That(_persistence.TryRestore(NamedHost(), FieldKey, out _), Is.False);

            Assert.That(_persistence.Report.Failures.Count, Is.EqualTo(1));
            Assert.That(_persistence.Report.Failures[0].Reason, Is.EqualTo(FakeResolver.RuntimeOnlyReason));
        }

        /// <summary>
        /// What: a read off the main thread of a field that has a wired value is reported once,
        /// under the host's type name, because that read gives the value up for the initializer.
        /// </summary>
        [Test]
        public void TryRestore_OffMainThreadForAWiredField_ReportsOnceByTypeName()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            _resolver.IsMainThread = false;

            Assert.That(_persistence.TryRestore(NamedHost(), FieldKey, out _), Is.False);
            _persistence.TryRestore(NamedHost(), FieldKey, out _);

            Assert.That(_persistence.Report.Failures.Count, Is.EqualTo(1));
            Assert.That(_persistence.Report.Failures[0].HostIdentity, Is.EqualTo(typeof(PersistenceHost).FullName));
            Assert.That(_persistence.Report.Failures[0].Reason, Does.Contain("main thread"));
        }

        /// <summary>
        /// What: a read off the main thread of a field nothing was wired into reports nothing.
        /// </summary>
        [Test]
        public void TryRestore_OffMainThreadForAnUnwiredField_ReportsNothing()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            _resolver.IsMainThread = false;

            Assert.That(_persistence.TryRestore(NamedHost(), OtherFieldKey, out _), Is.False);

            Assert.That(_persistence.Report.Failures, Is.Empty);
        }

        /// <summary>
        /// What: on the main thread, a host without an identity is a plain C# object that was never
        /// recorded, so reading a field another host has wired reports nothing.
        /// </summary>
        [Test]
        public void TryRestore_OnMainThreadForAHostWithoutIdentity_ReportsNothing()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);

            Assert.That(_persistence.TryRestore(new PersistenceHost(), FieldKey, out _), Is.False);

            Assert.That(_persistence.Report.Failures, Is.Empty);
        }

        /// <summary>
        /// What: a failure is handed out once for an apply, while the full list stays for a status read.
        /// </summary>
        [Test]
        public void TakeUnreportedFailures_SecondCall_IsEmpty()
        {
            _persistence.Record(NamedHost(), FieldKey, new SceneRef(TargetIdentity));
            _persistence.TryRestore(NamedHost(), FieldKey, out _);

            Assert.That(_persistence.TakeUnreportedFailures().Count, Is.EqualTo(1));
            Assert.That(_persistence.TakeUnreportedFailures(), Is.Empty);
            Assert.That(_persistence.Report.Failures.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a new scene reload session hands out a failure that recurs in it again, even though
        /// the same failure was already handed out in the session before.
        /// </summary>
        [Test]
        public void TakeUnreportedFailures_AfterANewSession_ReturnsTheRecurringFailureAgain()
        {
            _persistence.Record(NamedHost(), FieldKey, new SceneRef(TargetIdentity));
            _persistence.TryRestore(NamedHost(), FieldKey, out _);
            Assert.That(_persistence.TakeUnreportedFailures().Count, Is.EqualTo(1));

            _persistence.BeginSceneReloadSession();
            _persistence.TryRestore(NamedHost(), FieldKey, out _);

            Assert.That(_persistence.TakeUnreportedFailures().Count, Is.EqualTo(1));
        }

        /// <summary>
        /// What: Clear forgets the recorded values and empties the report, so nothing is restored
        /// and no earlier failure or restore is still listed.
        /// </summary>
        [Test]
        public void Clear_ThenTryRestore_ReturnsFalse()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            _persistence.Record(NamedHost(), OtherFieldKey, new SceneRef(TargetIdentity));
            _persistence.TryRestore(NamedHost(), FieldKey, out _);
            _persistence.TryRestore(NamedHost(), OtherFieldKey, out _);
            Assert.That(_persistence.Report.RestoredCount, Is.EqualTo(1));
            Assert.That(_persistence.Report.Failures.Count, Is.EqualTo(1));

            _persistence.Clear();

            Assert.That(_persistence.Report.Failures, Is.Empty);
            Assert.That(_persistence.Report.RestoredCount, Is.EqualTo(0));
            Assert.That(_persistence.TryRestore(NamedHost(), FieldKey, out _), Is.False);
            Assert.That(_persistence.Report.RestoredCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a new scene reload session starts with an empty report, reports a failure that
        /// recurs in it once more, and keeps the recorded value.
        /// </summary>
        [Test]
        public void BeginSceneReloadSession_SameFailureAgain_IsReportedOnceMore()
        {
            _persistence.Record(NamedHost(), FieldKey, new SceneRef(TargetIdentity));
            _persistence.Record(NamedHost(), OtherFieldKey, 7);
            _persistence.TryRestore(NamedHost(), FieldKey, out _);
            _persistence.TryRestore(NamedHost(), OtherFieldKey, out _);

            _persistence.BeginSceneReloadSession();
            Assert.That(_persistence.Report.Failures, Is.Empty);
            Assert.That(_persistence.Report.RestoredCount, Is.EqualTo(0));

            _persistence.TryRestore(NamedHost(), FieldKey, out _);
            _persistence.TryRestore(NamedHost(), FieldKey, out _);

            Assert.That(_persistence.Report.Failures.Count, Is.EqualTo(1));
            Assert.That(_persistence.Ledger.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// What: after a scene reload, a host that is no longer at its place is named once for each
        /// wired field, with the host-missing reason, and its recorded values are kept.
        /// </summary>
        [Test]
        public void ReportMissingHosts_HostNotAtItsPlace_NamesEachFieldOnce()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            _persistence.Record(NamedHost(), OtherFieldKey, new SceneRef(TargetIdentity));
            _resolver.MissingHosts.Add(HostIdentity);

            _persistence.ReportMissingHosts(false);
            IReadOnlyList<HotReloadWiredValueRestoreFailure> failures = _persistence.TakeUnreportedFailures();

            Assert.That(failures.Count, Is.EqualTo(2));
            List<string> fieldKeys = new List<string>();
            foreach (HotReloadWiredValueRestoreFailure failure in failures)
            {
                Assert.That(failure.HostIdentity, Is.EqualTo(HostIdentity));
                Assert.That(failure.Reason, Is.EqualTo(HotReloadWiredValuePersistence.HostMissingReason));
                fieldKeys.Add(failure.StoreFieldKey);
            }

            Assert.That(fieldKeys, Is.EquivalentTo(new[] { FieldKey, OtherFieldKey }));
            _persistence.ReportMissingHosts(false);
            Assert.That(_persistence.TakeUnreportedFailures(), Is.Empty);
            Assert.That(_persistence.Ledger.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a host still at its place is not named, and its value still comes back on the
        /// first read afterwards.
        /// </summary>
        [Test]
        public void ReportMissingHosts_HostStillAtItsPlace_ReportsNothingAndKeepsRestoring()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);

            _persistence.ReportMissingHosts(false);

            Assert.That(_persistence.TakeUnreportedFailures(), Is.Empty);
            Assert.That(_persistence.Report.Failures, Is.Empty);
            Assert.That(_persistence.TryRestore(NamedHost(), FieldKey, out object value), Is.True);
            Assert.That(value, Is.EqualTo(7));
        }

        /// <summary>
        /// What: a host that is still missing after the next scene reload is named again in that
        /// new session.
        /// </summary>
        [Test]
        public void ReportMissingHosts_AfterANewSession_NamesTheHostAgain()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            _persistence.Record(NamedHost(), OtherFieldKey, 3);
            _resolver.MissingHosts.Add(HostIdentity);
            _persistence.ReportMissingHosts(false);
            Assert.That(_persistence.TakeUnreportedFailures().Count, Is.EqualTo(2));

            _persistence.BeginSceneReloadSession();
            _persistence.ReportMissingHosts(false);

            Assert.That(_persistence.TakeUnreportedFailures().Count, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a host wired during Play that is not at its place once Play Mode is left is named
        /// once with the Play-only reason and forgotten, so no later session names it or restores it.
        /// </summary>
        [Test]
        public void ReportMissingHosts_PlayOnlyHostAfterLeavingPlayMode_NamesItOnceAndForgetsIt()
        {
            _resolver.IsPlayModeRunning = true;
            _persistence.Record(NamedHost(), FieldKey, 7);
            _resolver.IsPlayModeRunning = false;
            _resolver.MissingHosts.Add(HostIdentity);

            _persistence.ReportMissingHosts(true);

            IReadOnlyList<HotReloadWiredValueRestoreFailure> failures = _persistence.TakeUnreportedFailures();
            Assert.That(failures.Count, Is.EqualTo(1));
            Assert.That(failures[0].Reason, Is.EqualTo(HotReloadWiredValuePersistence.PlayOnlyHostReason));
            Assert.That(_persistence.Ledger.Count, Is.EqualTo(0));

            _persistence.BeginSceneReloadSession();
            _persistence.ReportMissingHosts(false);

            Assert.That(_persistence.TakeUnreportedFailures(), Is.Empty);
            Assert.That(_persistence.TryRestore(NamedHost(), FieldKey, out _), Is.False);
        }

        /// <summary>
        /// What: a host wired during Play that the Edit-time scene still has at its place is
        /// neither named nor forgotten when Play Mode is left.
        /// </summary>
        [Test]
        public void ReportMissingHosts_PlayOnlyHostStillAtItsPlaceAfterLeavingPlayMode_KeepsIt()
        {
            _resolver.IsPlayModeRunning = true;
            _persistence.Record(NamedHost(), FieldKey, 7);
            _resolver.IsPlayModeRunning = false;

            _persistence.ReportMissingHosts(true);

            Assert.That(_persistence.TakeUnreportedFailures(), Is.Empty);
            Assert.That(_persistence.Ledger.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a host wired during Play in a scene Edit Mode cannot read counts as missing once
        /// Play Mode is left, so it is named once with the Play-only reason and forgotten.
        /// </summary>
        [Test]
        public void ReportMissingHosts_PlayWiredHostInASceneEditModeCannotReadAfterLeavingPlayMode_NamesItOnceAndForgetsIt()
        {
            _resolver.IsPlayModeRunning = true;
            _persistence.Record(NamedHost(), FieldKey, 7);
            _resolver.IsPlayModeRunning = false;
            _resolver.UnloadedSceneHosts.Add(HostIdentity);

            _persistence.ReportMissingHosts(true);

            IReadOnlyList<HotReloadWiredValueRestoreFailure> failures = _persistence.TakeUnreportedFailures();
            Assert.That(failures.Count, Is.EqualTo(1));
            Assert.That(failures[0].Reason, Is.EqualTo(HotReloadWiredValuePersistence.PlayOnlyHostReason));
            Assert.That(_persistence.Ledger.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a host wired in Edit Mode whose scene cannot be read stays silent and kept, even
        /// when Play Mode is left, because an unread scene is out of sight, not gone.
        /// </summary>
        [Test]
        public void ReportMissingHosts_EditWiredHostInAnUnreadableScene_StaysSilentAndKept()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            _resolver.UnloadedSceneHosts.Add(HostIdentity);

            _persistence.ReportMissingHosts(true);

            Assert.That(_persistence.TakeUnreportedFailures(), Is.Empty);
            Assert.That(_persistence.Ledger.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a host wired in Edit Mode that is missing once Play Mode is left is named with the
        /// host-missing reason and kept, since putting the host back brings the value back.
        /// </summary>
        [Test]
        public void ReportMissingHosts_HostWiredInEditModeMissingAfterLeavingPlayMode_KeepsItWithTheHostMissingReason()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            _resolver.MissingHosts.Add(HostIdentity);

            _persistence.ReportMissingHosts(true);

            AssertOneFailureWithReasonAndLedgerCount(HotReloadWiredValuePersistence.HostMissingReason, 1);
        }

        /// <summary>
        /// What: a host wired during Play that is missing when Play Mode is entered again is named
        /// with the host-missing reason and kept; only leaving Play Mode forgets Play-only hosts.
        /// </summary>
        [Test]
        public void ReportMissingHosts_PlayWiredHostMissingWhenEnteringPlayMode_KeepsItWithTheHostMissingReason()
        {
            _resolver.IsPlayModeRunning = true;
            _persistence.Record(NamedHost(), FieldKey, 7);
            _resolver.MissingHosts.Add(HostIdentity);

            _persistence.ReportMissingHosts(false);

            AssertOneFailureWithReasonAndLedgerCount(HotReloadWiredValuePersistence.HostMissingReason, 1);
        }

        /// <summary>
        /// What: wiring the same host and field again in Edit Mode drops the Play-only mark, so a
        /// later absence is reported with the host-missing reason and the value is kept.
        /// </summary>
        [Test]
        public void Record_SameKeyAgainInEditMode_ClearsThePlayOnlyMark()
        {
            _resolver.IsPlayModeRunning = true;
            _persistence.Record(NamedHost(), FieldKey, 7);
            _resolver.IsPlayModeRunning = false;
            _persistence.Record(NamedHost(), FieldKey, 8);
            _resolver.MissingHosts.Add(HostIdentity);

            _persistence.ReportMissingHosts(true);

            AssertOneFailureWithReasonAndLedgerCount(HotReloadWiredValuePersistence.HostMissingReason, 1);
        }

        /// <summary>
        /// What: a value that comes back after its host was named missing drops that failure, so
        /// the report no longer lists it and a later drain hands nothing out.
        /// </summary>
        [Test]
        public void TryRestore_AfterAReportedFailure_ClearsThatFailure()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            _resolver.MissingHosts.Add(HostIdentity);
            _persistence.ReportMissingHosts(false);
            Assert.That(_persistence.TakeUnreportedFailures().Count, Is.EqualTo(1));
            _resolver.MissingHosts.Remove(HostIdentity);

            Assert.That(_persistence.TryRestore(NamedHost(), FieldKey, out _), Is.True);

            (int restoredCount, IReadOnlyList<HotReloadWiredValueRestoreFailure> failures) = _persistence.ReadReport();
            Assert.That(failures, Is.Empty);
            Assert.That(restoredCount, Is.EqualTo(1));
            Assert.That(_persistence.TakeUnreportedFailures(), Is.Empty);
        }

        /// <summary>
        /// What: a value that comes back before its failure was drained drops that failure, so the
        /// drain hands nothing out.
        /// </summary>
        [Test]
        public void TryRestore_AfterAFailureNotYetTaken_ClearsItBeforeTheDrain()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            _resolver.MissingHosts.Add(HostIdentity);
            _persistence.ReportMissingHosts(false);
            _resolver.MissingHosts.Remove(HostIdentity);

            Assert.That(_persistence.TryRestore(NamedHost(), FieldKey, out _), Is.True);

            Assert.That(_persistence.TakeUnreportedFailures(), Is.Empty);
        }

        private void AssertOneFailureWithReasonAndLedgerCount(string reason, int ledgerCount)
        {
            IReadOnlyList<HotReloadWiredValueRestoreFailure> failures = _persistence.TakeUnreportedFailures();
            Assert.That(failures.Count, Is.EqualTo(1));
            Assert.That(failures[0].Reason, Is.EqualTo(reason));
            Assert.That(_persistence.Ledger.Count, Is.EqualTo(ledgerCount));
        }

        private PersistenceHost NamedHost()
        {
            PersistenceHost host = new PersistenceHost();
            _resolver.HostIdentities[host] = HostIdentity;
            return host;
        }

        private sealed class FakeResolver : IHotReloadWiredValueResolver
        {
            internal const string NotFoundReason = "no object at that identity";
            internal const string RuntimeOnlyReason = "the value is a runtime-created object";

            internal Dictionary<object, string> HostIdentities { get; } = new Dictionary<object, string>();

            internal Dictionary<string, object> Resolvable { get; } = new Dictionary<string, object>();

            internal HashSet<string> MissingHosts { get; } = new HashSet<string>();

            internal HashSet<string> UnloadedSceneHosts { get; } = new HashSet<string>();

            public bool IsMainThread { get; set; } = true;

            public bool IsPlayModeRunning { get; set; }

            public bool IsHostMissing(string hostIdentity, bool unloadedSceneCountsAsMissing) =>
                MissingHosts.Contains(hostIdentity)
                || (unloadedSceneCountsAsMissing && UnloadedSceneHosts.Contains(hostIdentity));

            public string DescribeHost(object host)
            {
                if (!IsMainThread)
                {
                    return null;
                }

                return HostIdentities.TryGetValue(host, out string identity) ? identity : null;
            }

            public HotReloadWiredValueDescriptor DescribeValue(object value)
            {
                if (value is SceneRef sceneRef)
                {
                    return HotReloadWiredValueDescriptor.SceneObject(sceneRef.Identity, nameof(SceneRef));
                }

                if (value is RuntimeOnlyObject)
                {
                    return HotReloadWiredValueDescriptor.Unrestorable(nameof(RuntimeOnlyObject), RuntimeOnlyReason);
                }

                return HotReloadWiredValueDescriptor.Plain(value);
            }

            public bool TryResolve(HotReloadWiredValueDescriptor descriptor, out object value, out string failureReason)
            {
                failureReason = null;
                if (Resolvable.TryGetValue(descriptor.Identity, out value))
                {
                    return true;
                }

                failureReason = NotFoundReason;
                return false;
            }
        }

        private sealed class SceneRef
        {
            internal SceneRef(string identity)
            {
                Identity = identity;
            }

            internal string Identity { get; }
        }

        private sealed class RuntimeOnlyObject
        {
        }

        private sealed class PersistenceHost
        {
        }
    }
}
