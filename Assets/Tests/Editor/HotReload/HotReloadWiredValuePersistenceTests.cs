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
        /// What: a host wired during Play that the Edit-time scene still had at its place when Play
        /// Mode was left is no longer treated as Play-only, so a later leave that finds it missing
        /// names it with the host-missing reason and keeps its value.
        /// </summary>
        [Test]
        public void ReportMissingHosts_PlayWiredHostPresentOnAnEarlierLeave_IsKeptWhenLaterMissing()
        {
            _resolver.IsPlayModeRunning = true;
            _persistence.Record(NamedHost(), FieldKey, 7);
            _resolver.IsPlayModeRunning = false;
            _persistence.ReportMissingHosts(true);
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

        /// <summary>
        /// What: a scene object value that failed to resolve while its host was missing and then
        /// resolves drops that failure, so the report lists only the restore.
        /// </summary>
        [Test]
        public void TryRestore_SceneObjectResolvedAfterAReportedFailure_ClearsThatFailure()
        {
            _persistence.Record(NamedHost(), FieldKey, new SceneRef(TargetIdentity));
            _resolver.MissingHosts.Add(HostIdentity);
            _persistence.ReportMissingHosts(false);
            Assert.That(_persistence.TakeUnreportedFailures().Count, Is.EqualTo(1));
            _resolver.MissingHosts.Remove(HostIdentity);
            _resolver.Resolvable[TargetIdentity] = new object();

            Assert.That(_persistence.TryRestore(NamedHost(), FieldKey, out _), Is.True);

            (int restoredCount, IReadOnlyList<HotReloadWiredValueRestoreFailure> failures) = _persistence.ReadReport();
            Assert.That(failures, Is.Empty);
            Assert.That(restoredCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a Play-only host named on leaving Play Mode but not yet drained is still handed
        /// out after the next session starts, and only once.
        /// </summary>
        [Test]
        public void BeginSceneReloadSession_PlayOnlyFailureNotYetTaken_CarriesItIntoTheNextSessionOnce()
        {
            RecordPlayOnlyHostNamedOnLeavingPlayMode();

            _persistence.BeginSceneReloadSession();

            AssertOneFailureWithReasonAndLedgerCount(HotReloadWiredValuePersistence.PlayOnlyHostReason, 0);
            _persistence.BeginSceneReloadSession();
            Assert.That(_persistence.TakeUnreportedFailures(), Is.Empty);
        }

        /// <summary>
        /// What: a Play-only host named on leaving Play Mode that a status read already showed is
        /// not carried into the next session.
        /// </summary>
        [Test]
        public void BeginSceneReloadSession_PlayOnlyFailureAlreadyRead_DoesNotCarryIt()
        {
            RecordPlayOnlyHostNamedOnLeavingPlayMode();
            Assert.That(_persistence.ReadReport().Failures.Count, Is.EqualTo(1));

            _persistence.BeginSceneReloadSession();

            Assert.That(_persistence.ReadReport().Failures, Is.Empty);
        }

        /// <summary>
        /// What: wiring the same host and field again removes the row that named the value as not
        /// restored, because the row would otherwise outlive the wiring it describes.
        /// </summary>
        [Test]
        public void Record_SameKeyAfterAFailure_RemovesTheFailureRow()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            _resolver.MissingHosts.Add(HostIdentity);
            _persistence.ReportMissingHosts(false);
            Assert.That(_persistence.Report.Failures.Count, Is.EqualTo(1), "Precondition: the missing host must be named.");

            _persistence.Record(NamedHost(), FieldKey, 9);

            Assert.That(_persistence.Report.Failures, Is.Empty);
        }

        /// <summary>
        /// What: a retry in the same restore generation as the last attempt answers false without
        /// asking the resolver, so a pending slot read every frame costs no host lookup.
        /// </summary>
        [Test]
        public void TryRestoreAgain_SameGeneration_DoesNotConsultTheResolver()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            PersistenceHost renamed = RenamedHost();
            int lastAttemptGeneration = 0;

            Assert.That(_persistence.TryRestoreAgain(renamed, FieldKey, ref lastAttemptGeneration, out _), Is.False);
            int callsAfterFirstRetry = _resolver.DescribeHostCalls;
            Assert.That(_persistence.TryRestoreAgain(renamed, FieldKey, ref lastAttemptGeneration, out _), Is.False);

            Assert.That(callsAfterFirstRetry, Is.GreaterThan(0), "Precondition: the first retry must ask the resolver.");
            Assert.That(_resolver.DescribeHostCalls, Is.EqualTo(callsAfterFirstRetry));
        }

        /// <summary>
        /// What: wiring a value moves the restore generation, so a pending slot asks again.
        /// </summary>
        [Test]
        public void TryRestoreAgain_AfterRecord_ConsultsTheResolverAgain()
        {
            AssertRetryAsksAgainAfter(() => _persistence.Record(NamedHost(), OtherFieldKey, 3));
        }

        /// <summary>
        /// What: a note that hosts may have changed moves the restore generation, so a pending
        /// slot asks again.
        /// </summary>
        [Test]
        public void TryRestoreAgain_AfterNoteHostsMayHaveChanged_ConsultsTheResolverAgain()
        {
            AssertRetryAsksAgainAfter(() => _persistence.NoteHostsMayHaveChanged());
        }

        /// <summary>
        /// What: the missing-host check after a scene reload moves the restore generation, so a
        /// pending slot asks again.
        /// </summary>
        [Test]
        public void TryRestoreAgain_AfterReportMissingHosts_ConsultsTheResolverAgain()
        {
            AssertRetryAsksAgainAfter(() => _persistence.ReportMissingHosts(false));
        }

        /// <summary>
        /// What: a retry off the main thread answers false, keeps the caller's generation so the
        /// next main-thread read still retries, and names no failure.
        /// </summary>
        [Test]
        public void TryRestoreAgain_OffMainThread_ReturnsFalseKeepsTheGenerationAndAddsNoFailureRow()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            PersistenceHost host = NamedHost();
            _resolver.IsMainThread = false;
            int lastAttemptGeneration = 0;

            bool restored = _persistence.TryRestoreAgain(host, FieldKey, ref lastAttemptGeneration, out object value);

            Assert.That(restored, Is.False);
            Assert.That(value, Is.Null);
            Assert.That(lastAttemptGeneration, Is.EqualTo(0));
            Assert.That(_persistence.Report.Failures, Is.Empty);
        }

        /// <summary>
        /// What: once the host is back at its place, the next retry restores the value and removes
        /// the row that named it as missing.
        /// </summary>
        [Test]
        public void TryRestoreAgain_HostBackAtItsPlace_RestoresAndRemovesTheFailureRow()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            PersistenceHost host = RenamedHost();
            _resolver.MissingHosts.Add(HostIdentity);
            _persistence.ReportMissingHosts(false);
            int lastAttemptGeneration = 0;
            Assert.That(_persistence.TryRestoreAgain(host, FieldKey, ref lastAttemptGeneration, out _), Is.False);
            Assert.That(_persistence.Report.Failures.Count, Is.EqualTo(1), "Precondition: the missing host must be named.");

            _resolver.MissingHosts.Remove(HostIdentity);
            _resolver.HostIdentities[host] = HostIdentity;
            _persistence.NoteHostsMayHaveChanged();
            bool restored = _persistence.TryRestoreAgain(host, FieldKey, ref lastAttemptGeneration, out object value);

            Assert.That(restored, Is.True);
            Assert.That(value, Is.EqualTo(7));
            Assert.That(_persistence.Report.Failures, Is.Empty);
        }

        /// <summary>
        /// What: a restore on the main thread removes the row an earlier off-main-thread read of
        /// the same field added, since the value did reach the host after all.
        /// </summary>
        [Test]
        public void TryRestore_SucceedsAfterAnOffMainThreadRow_RemovesThatRow()
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            PersistenceHost host = NamedHost();
            _resolver.IsMainThread = false;
            _persistence.TryRestore(host, FieldKey, out _);
            Assert.That(_persistence.Report.Failures.Count, Is.EqualTo(1), "Precondition: the off-main read must be named.");

            _resolver.IsMainThread = true;
            bool restored = _persistence.TryRestore(host, FieldKey, out object value);

            Assert.That(restored, Is.True);
            Assert.That(value, Is.EqualTo(7));
            Assert.That(_persistence.Report.Failures, Is.Empty);
        }

        // One retry in the current generation, the trigger, then a second retry with the same
        // generation variable: only a trigger that moved the generation lets it ask the resolver.
        private void AssertRetryAsksAgainAfter(System.Action trigger)
        {
            _persistence.Record(NamedHost(), FieldKey, 7);
            PersistenceHost renamed = RenamedHost();
            int lastAttemptGeneration = 0;
            _persistence.TryRestoreAgain(renamed, FieldKey, ref lastAttemptGeneration, out _);
            int callsAfterFirstRetry = _resolver.DescribeHostCalls;

            trigger();
            int callsAfterTrigger = _resolver.DescribeHostCalls;
            _persistence.TryRestoreAgain(renamed, FieldKey, ref lastAttemptGeneration, out _);

            Assert.That(callsAfterFirstRetry, Is.GreaterThan(0), "Precondition: the first retry must ask the resolver.");
            Assert.That(_resolver.DescribeHostCalls, Is.GreaterThan(callsAfterTrigger));
        }

        // A host whose identity no longer matches the recorded one, as after a rename.
        private PersistenceHost RenamedHost()
        {
            PersistenceHost host = new PersistenceHost();
            _resolver.HostIdentities[host] = "scene:Main|path:HostRenamed[0]|component:Ns.Host|index:0";
            return host;
        }

        private void RecordPlayOnlyHostNamedOnLeavingPlayMode()
        {
            _resolver.IsPlayModeRunning = true;
            _persistence.Record(NamedHost(), FieldKey, 7);
            _resolver.IsPlayModeRunning = false;
            _resolver.MissingHosts.Add(HostIdentity);
            _persistence.ReportMissingHosts(true);
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

            internal int DescribeHostCalls { get; private set; }

            public string DescribeHost(object host)
            {
                DescribeHostCalls++;
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
