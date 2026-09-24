using System;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers what the wired-value editor hooks do on each Play Mode transition: name hosts a
    /// finished scene reload did not put back, and start a fresh report before the next reload.
    /// </summary>
    /// <remarks>
    /// The hooks are driven through Handle with a persistence over a fake resolver, without
    /// registering them on the real editor event.
    /// </remarks>
    public class HotReloadWiredValueEditorHooksTests
    {
        private const string HostIdentity = "scene:Main|path:Host[0]|component:Ns.Host|index:0";
        private const string FieldKey = "Ns.Host::target";

        private Func<HotReloadWiredValuePersistence> _previousProvider;
        private MissingHostResolver _resolver;
        private HotReloadWiredValuePersistence _persistence;

        [SetUp]
        public void SetUp()
        {
            _resolver = new MissingHostResolver();
            _persistence = new HotReloadWiredValuePersistence(_resolver);
            _previousProvider = HotReloadWiredValueEditorHooks.GetPersistence;
            HotReloadWiredValueEditorHooks.GetPersistence = () => _persistence;
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadWiredValueEditorHooks.GetPersistence = _previousProvider;
        }

        /// <summary>
        /// What: entering Play Mode names a wired value whose host is no longer at its place.
        /// </summary>
        [Test]
        public void Handle_EnteredPlayMode_NamesHostsNoLongerAtTheirPlace()
        {
            ArrangeMissingHost();

            HotReloadWiredValueEditorHooks.Handle(PlayModeStateChange.EnteredPlayMode);

            AssertOneHostMissingFailure();
        }

        /// <summary>
        /// What: entering Edit Mode names a wired value whose host is no longer at its place.
        /// </summary>
        [Test]
        public void Handle_EnteredEditMode_NamesHostsNoLongerAtTheirPlace()
        {
            ArrangeMissingHost();

            HotReloadWiredValueEditorHooks.Handle(PlayModeStateChange.EnteredEditMode);

            AssertOneHostMissingFailure();
        }

        /// <summary>
        /// What: leaving Play Mode starts a new session, so a host still missing after that reload
        /// is named again.
        /// </summary>
        [Test]
        public void Handle_ExitingPlayMode_StartsANewSession()
        {
            ArrangeMissingHost();
            HotReloadWiredValueEditorHooks.Handle(PlayModeStateChange.EnteredPlayMode);
            AssertOneHostMissingFailure();

            HotReloadWiredValueEditorHooks.Handle(PlayModeStateChange.ExitingPlayMode);
            HotReloadWiredValueEditorHooks.Handle(PlayModeStateChange.EnteredEditMode);

            AssertOneHostMissingFailure();
        }

        /// <summary>
        /// What: leaving Edit Mode starts a new session, so a host still missing after that reload
        /// is named again.
        /// </summary>
        [Test]
        public void Handle_ExitingEditMode_StartsANewSession()
        {
            ArrangeMissingHost();
            HotReloadWiredValueEditorHooks.Handle(PlayModeStateChange.EnteredEditMode);
            AssertOneHostMissingFailure();

            HotReloadWiredValueEditorHooks.Handle(PlayModeStateChange.ExitingEditMode);
            HotReloadWiredValueEditorHooks.Handle(PlayModeStateChange.EnteredPlayMode);

            AssertOneHostMissingFailure();
        }

        /// <summary>
        /// What: entering Edit Mode names a host wired during Play that is not at its place once,
        /// with the Play-only reason, and forgets its value.
        /// </summary>
        [Test]
        public void Handle_EnteredEditMode_ForgetsAPlayOnlyHostAfterNamingItOnce()
        {
            _resolver.IsPlayModeRunning = true;
            ArrangeMissingHost();
            _resolver.IsPlayModeRunning = false;

            HotReloadWiredValueEditorHooks.Handle(PlayModeStateChange.EnteredEditMode);

            AssertOneFailure(HotReloadWiredValuePersistence.PlayOnlyHostReason);
            Assert.That(_persistence.Ledger.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// What: entering Play Mode names a host wired during an earlier Play that is not at its
        /// place with the host-missing reason, and keeps its value.
        /// </summary>
        [Test]
        public void Handle_EnteredPlayMode_KeepsAPlayWiredHostWithTheHostMissingReason()
        {
            _resolver.IsPlayModeRunning = true;
            ArrangeMissingHost();

            HotReloadWiredValueEditorHooks.Handle(PlayModeStateChange.EnteredPlayMode);

            AssertOneHostMissingFailure();
            Assert.That(_persistence.Ledger.Count, Is.EqualTo(1));
        }

        private void ArrangeMissingHost()
        {
            _persistence.Record(new object(), FieldKey, 7);
            _resolver.MissingHosts.Add(HostIdentity);
        }

        private void AssertOneHostMissingFailure()
        {
            AssertOneFailure(HotReloadWiredValuePersistence.HostMissingReason);
        }

        private void AssertOneFailure(string reason)
        {
            IReadOnlyList<HotReloadWiredValueRestoreFailure> failures = _persistence.TakeUnreportedFailures();

            Assert.That(failures.Count, Is.EqualTo(1));
            Assert.That(failures[0].HostIdentity, Is.EqualTo(HostIdentity));
            Assert.That(failures[0].Reason, Is.EqualTo(reason));
        }

        private sealed class MissingHostResolver : IHotReloadWiredValueResolver
        {
            internal HashSet<string> MissingHosts { get; } = new HashSet<string>();

            internal HashSet<string> UnloadedSceneHosts { get; } = new HashSet<string>();

            public bool IsMainThread => true;

            public bool IsPlayModeRunning { get; set; }

            public string DescribeHost(object host) => HostIdentity;

            public bool IsHostMissing(string hostIdentity, bool unloadedSceneCountsAsMissing) =>
                MissingHosts.Contains(hostIdentity)
                || (unloadedSceneCountsAsMissing && UnloadedSceneHosts.Contains(hostIdentity));

            public HotReloadWiredValueDescriptor DescribeValue(object value) =>
                HotReloadWiredValueDescriptor.Plain(value);

            public bool TryResolve(HotReloadWiredValueDescriptor descriptor, out object value, out string failureReason)
            {
                value = null;
                failureReason = "not resolvable in this test";
                return false;
            }
        }
    }
}
