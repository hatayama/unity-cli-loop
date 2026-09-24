using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure coverage for the refresh a status or apply runs before it reports wired values: which
    /// ledger entries it reads, and what the report says afterwards.
    /// </summary>
    /// <remarks>
    /// The resolver and the slot reader are fakes. The reader stands in for a patched method's
    /// read by calling the persistence the way the added-field store does.
    /// </remarks>
    public class HotReloadWiredValueRestoreRefreshTests
    {
        private const string HostIdentity = "scene:Main|path:Host[0]|component:Ns.Host|index:0";
        private const string FieldKey = "Ns.Host::target";
        private const string TargetIdentity = "scene:Main|path:Target[1]";
        private const string TargetGoneReason = "target is gone";

        private RefreshResolver _resolver;
        private HotReloadWiredValuePersistence _persistence;
        private RecordingReader _reader;
        private HotReloadWiredValueRestoreRefresh _refresh;

        [SetUp]
        public void SetUp()
        {
            _resolver = new RefreshResolver();
            _persistence = new HotReloadWiredValuePersistence(_resolver);
            _reader = new RecordingReader(_persistence);
            _refresh = new HotReloadWiredValueRestoreRefresh(_persistence, _resolver, _reader);
        }

        /// <summary>
        /// What: a host that is still not at its place is not read, and its host-missing row stays
        /// as it was.
        /// </summary>
        [Test]
        public void Run_HostStillMissing_LeavesTheRowAndDoesNotRead()
        {
            RecordThenLoseHost(7);
            PlaceHost();
            _resolver.MissingIdentities.Add(HostIdentity);

            _refresh.Run();

            Assert.That(_reader.Calls, Is.Empty);
            AssertSingleRow(HotReloadWiredValuePersistence.HostMissingReason);
        }

        /// <summary>
        /// What: once the host is back at its place, the refresh reads it, the value comes back,
        /// and the row goes away.
        /// </summary>
        [Test]
        public void Run_HostBackAndValueRestores_RemovesTheRowAndCountsTheRestore()
        {
            RecordThenLoseHost(7);
            PlaceHost();

            int reads = _refresh.Run();

            Assert.That(reads, Is.EqualTo(1));
            Assert.That(_persistence.ReadReport().Failures, Is.Empty);
            Assert.That(_persistence.RestoredCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a host that is back but whose value still fails keeps one row with the reason
        /// that holds now, and an apply that already took the row is not handed it again.
        /// </summary>
        [Test]
        public void Run_HostBackButValueFails_ReplacesTheReasonWithoutReportingTheRowAgain()
        {
            RecordThenLoseHost(new SceneRef(TargetIdentity));
            Assert.That(_persistence.TakeUnreportedFailures().Count, Is.EqualTo(1));
            PlaceHost();
            _resolver.FailureReason = TargetGoneReason;

            _refresh.Run();

            AssertSingleRow(TargetGoneReason);
            Assert.That(_persistence.TakeUnreportedFailures(), Is.Empty);
        }

        /// <summary>
        /// What: a read that reaches no restore (no added field matches) leaves the row and its
        /// reason, and counts nothing.
        /// </summary>
        [Test]
        public void Run_ReadDoesNotReachTheRestore_LeavesTheRow()
        {
            RecordThenLoseHost(7);
            PlaceHost();
            _reader.Mode = ReadMode.TouchNothing;

            _refresh.Run();

            Assert.That(_reader.Calls.Count, Is.EqualTo(1));
            AssertSingleRow(HotReloadWiredValuePersistence.HostMissingReason);
            Assert.That(_persistence.RestoredCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: off the main thread the refresh reads nothing and changes nothing.
        /// </summary>
        [Test]
        public void Run_OffMainThread_DoesNothing()
        {
            RecordThenLoseHost(7);
            PlaceHost();
            _resolver.IsMainThread = false;

            int reads = _refresh.Run();

            Assert.That(reads, Is.EqualTo(0));
            Assert.That(_reader.Calls, Is.Empty);
            AssertSingleRow(HotReloadWiredValuePersistence.HostMissingReason);
            Assert.That(_persistence.RestoredCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a Play-only row, whose value left the ledger when Play Mode stopped, is not read
        /// and stays.
        /// </summary>
        [Test]
        public void Run_PlayOnlyRowOutsideTheLedger_IsNotRead()
        {
            _resolver.IsPlayModeRunning = true;
            _persistence.Record(PlaceHost(), FieldKey, 7);
            _resolver.IsPlayModeRunning = false;
            _resolver.MissingIdentities.Add(HostIdentity);
            _persistence.ReportMissingHosts(leftPlayMode: true);
            _resolver.MissingIdentities.Clear();

            int reads = _refresh.Run();

            Assert.That(reads, Is.EqualTo(0));
            Assert.That(_reader.Calls, Is.Empty);
            AssertSingleRow(HotReloadWiredValuePersistence.PlayOnlyHostReason);
        }

        /// <summary>
        /// What: a pending slot, whose last retry already used the current generation, is retried
        /// by the refresh because the refresh moves the generation first.
        /// </summary>
        [Test]
        public void Run_PendingSlotAlreadyRetriedThisGeneration_RestoresAfterTheGenerationMoves()
        {
            object host = PlaceHost();
            _persistence.Record(host, FieldKey, new SceneRef(TargetIdentity));
            _resolver.FailureReason = TargetGoneReason;
            Assert.That(_persistence.TryRestoreAgain(host, FieldKey, ref _reader.LastAttemptGeneration, out _), Is.False);
            AssertSingleRow(TargetGoneReason);
            _resolver.Resolvable[TargetIdentity] = new object();
            _reader.Mode = ReadMode.RestoreAgain;

            _refresh.Run();

            Assert.That(_persistence.ReadReport().Failures, Is.Empty);
            Assert.That(_persistence.RestoredCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a restored value the field's type rejects is not counted as restored, is named
        /// once with the unreadable-value reason, and the next refresh neither counts it nor hands
        /// it to apply again.
        /// </summary>
        [Test]
        public void Run_RestoredValueTheFieldRejects_IsNamedOnceAndNotCounted()
        {
            _persistence.Record(PlaceHost(), FieldKey, 7);
            _reader.Mode = ReadMode.RestoreThenReject;

            _refresh.Run();

            Assert.That(_persistence.RestoredCount, Is.EqualTo(0));
            AssertSingleRow(HotReloadWiredValuePersistence.UnreadableValueReason);
            Assert.That(_persistence.TakeUnreportedFailures().Count, Is.EqualTo(1));

            _refresh.Run();

            Assert.That(_persistence.RestoredCount, Is.EqualTo(0));
            AssertSingleRow(HotReloadWiredValuePersistence.UnreadableValueReason);
            Assert.That(_persistence.TakeUnreportedFailures(), Is.Empty);
        }

        /// <summary>
        /// What: --status runs the refresh before it reads the report, so a host back at its place
        /// is reported as restored instead of with its host-missing row.
        /// </summary>
        [Test]
        public void ExecuteStatus_HostBackAtItsPlace_ReportsTheRestoreInsteadOfTheRow()
        {
            RecordThenLoseHost(7);
            PlaceHost();
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                HotReloadServices services = HotReloadCompositionRoot.Services;
                HotReloadStatusExecutor executor = new HotReloadStatusExecutor(
                    services.Domain,
                    services.Patcher,
                    services.UnityMessageForwarding,
                    _persistence,
                    _refresh);

                HotReloadResponse response = executor.ExecuteStatus();

                Assert.That(response.RestoredWiredValueCount, Is.EqualTo(1));
                Assert.That(response.UnrestoredWiredValues, Is.Empty);
            }
        }

        // Records the value on the host at its place, then names the host as missing after a
        // scene reload, which leaves one host-missing row.
        private void RecordThenLoseHost(object value)
        {
            _persistence.Record(PlaceHost(), FieldKey, value);
            _resolver.MissingIdentities.Add(HostIdentity);
            _persistence.ReportMissingHosts(leftPlayMode: false);
            AssertSingleRow(HotReloadWiredValuePersistence.HostMissingReason);
            _resolver.MissingIdentities.Clear();
            _resolver.HostsByIdentity.Clear();
        }

        // Puts a fresh host object at the identity's place, as a scene reload or a rename back does.
        private object PlaceHost()
        {
            RefreshHost host = new RefreshHost();
            _resolver.HostIdentities[host] = HostIdentity;
            _resolver.HostsByIdentity[HostIdentity] = host;
            return host;
        }

        private void AssertSingleRow(string reason)
        {
            IReadOnlyList<HotReloadWiredValueRestoreFailure> failures = _persistence.ReadReport().Failures;
            Assert.That(failures.Count, Is.EqualTo(1));
            Assert.That(failures[0].Reason, Is.EqualTo(reason));
        }

        private enum ReadMode
        {
            Restore,
            RestoreAgain,
            TouchNothing,
            RestoreThenReject,
        }

        private sealed class RecordingReader : IHotReloadWiredValueSlotReader
        {
            private readonly HotReloadWiredValuePersistence _persistence;

            // A field so a test can make the first failed retry through it, the way a pending slot
            // keeps its own last attempt.
            internal int LastAttemptGeneration;

            internal RecordingReader(HotReloadWiredValuePersistence persistence)
            {
                _persistence = persistence;
            }

            internal ReadMode Mode { get; set; } = ReadMode.Restore;

            internal List<(object Host, string StoreFieldKey)> Calls { get; } =
                new List<(object Host, string StoreFieldKey)>();

            public bool TryRead(object host, string storeFieldKey)
            {
                Calls.Add((host, storeFieldKey));
                switch (Mode)
                {
                    case ReadMode.Restore:
                        return _persistence.TryRestore(host, storeFieldKey, out _);
                    case ReadMode.RestoreAgain:
                        return _persistence.TryRestoreAgain(host, storeFieldKey, ref LastAttemptGeneration, out _);
                    case ReadMode.RestoreThenReject:
                        _persistence.TryRestore(host, storeFieldKey, out _);
                        return false;
                    default:
                        return false;
                }
            }
        }

        private sealed class RefreshResolver : IHotReloadWiredValueResolver
        {
            internal Dictionary<object, string> HostIdentities { get; } = new Dictionary<object, string>();

            internal Dictionary<string, object> HostsByIdentity { get; } = new Dictionary<string, object>();

            internal Dictionary<string, object> Resolvable { get; } = new Dictionary<string, object>();

            internal HashSet<string> MissingIdentities { get; } = new HashSet<string>();

            internal string FailureReason { get; set; } = "no object at that identity";

            public bool IsMainThread { get; set; } = true;

            public bool IsPlayModeRunning { get; set; }

            public string DescribeHost(object host)
            {
                if (!IsMainThread)
                {
                    return null;
                }

                return HostIdentities.TryGetValue(host, out string identity) ? identity : null;
            }

            public bool IsHostMissing(string hostIdentity, bool unloadedSceneCountsAsMissing) =>
                MissingIdentities.Contains(hostIdentity);

            public bool TryResolveHost(string hostIdentity, out object host) =>
                HostsByIdentity.TryGetValue(hostIdentity, out host);

            public HotReloadWiredValueDescriptor DescribeValue(object value) =>
                value is SceneRef sceneRef
                    ? HotReloadWiredValueDescriptor.SceneObject(sceneRef.Identity, nameof(SceneRef))
                    : HotReloadWiredValueDescriptor.Plain(value);

            public bool TryResolve(HotReloadWiredValueDescriptor descriptor, out object value, out string failureReason)
            {
                failureReason = null;
                if (Resolvable.TryGetValue(descriptor.Identity, out value))
                {
                    return true;
                }

                failureReason = FailureReason;
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

        private sealed class RefreshHost
        {
        }
    }
}
