using System.Collections.Generic;
using System.Threading;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps values wired through the wiring entry point and gives them back to the instance that
    /// replaces their host after a scene reload, recording what came back and what did not.
    /// </summary>
    /// <remarks>
    /// Why restore lazily, on the first read of a slot, rather than when play mode is entered: a
    /// scene reload runs Awake and OnEnable before the play mode state change is raised, so a
    /// restore driven by that event would miss the values those callbacks read.
    /// Why one lock around the ledger and the report: a slot can be read off the main thread while
    /// the main thread records, restores or drains, and the dictionary, the dedup set and the
    /// report's drain index are not safe to change concurrently. The resolver is called outside the
    /// lock because it only answers on the main thread and touches none of this state.
    /// Why a Play-only failure outlives the session reset until it is read: its value is already
    /// forgotten, so that row is the only place it is ever named. The report is read only by an
    /// apply and by a status call, and an agent may do neither between stopping Play and starting
    /// it again, which resets the report.
    /// </remarks>
    internal sealed class HotReloadWiredValuePersistence : IHotReloadWiredValuePersistence
    {
        private const string OffMainThreadReason =
            "read off the main thread before any main-thread read; wire it again from the main thread";

        internal const string HostMissingReason =
            "no object is at the host's place now, so nothing reads this value until one is back there; "
            + "put the host back (it was renamed, moved, or removed, or a sibling before it was), or wire "
            + "the value into the object that replaced it";

        internal const string PlayOnlyHostReason =
            "the host was wired while Play Mode ran and the Edit-time scene has nothing at its place "
            + "(a runtime-created object, or one renamed or moved during Play), so the value left with "
            + "Play Mode and is no longer kept; wire it again once the object exists in the next Play session";

        private readonly object _gate = new object();
        private readonly IHotReloadWiredValueResolver _resolver;
        private readonly HashSet<HotReloadWiredValueHostKey> _reportedFailures =
            new HashSet<HotReloadWiredValueHostKey>();
        private readonly HashSet<HotReloadWiredValueHostKey> _undeliveredPlayOnlyFailures =
            new HashSet<HotReloadWiredValueHostKey>();

        // Starts at 1 so it never equals the 0 a slot starts with, and a slot's first retry runs.
        private int _restoreGeneration = 1;

        internal HotReloadWiredValuePersistence(IHotReloadWiredValueResolver resolver)
        {
            Debug.Assert(resolver != null, "resolver must not be null.");
            _resolver = resolver;
        }

        internal HotReloadWiredValueLedger Ledger { get; } = new HotReloadWiredValueLedger();

        internal HotReloadWiredValueRestoreReport Report { get; } = new HotReloadWiredValueRestoreReport();

        public void Record(object host, string storeFieldKey, object value)
        {
            Debug.Assert(host != null, "host must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(storeFieldKey), "storeFieldKey must not be empty.");

            string identity = _resolver.DescribeHost(host);
            if (identity == null)
            {
                // A plain C# host has nothing a replacement could be matched by.
                return;
            }

            bool wiredWhilePlaying = _resolver.IsPlayModeRunning;
            HotReloadWiredValueDescriptor descriptor = _resolver.DescribeValue(value);
            HotReloadWiredValueHostKey key = new HotReloadWiredValueHostKey(identity, storeFieldKey);
            lock (_gate)
            {
                Ledger.Record(key, descriptor, wiredWhilePlaying);
                // A value wired again into a host that is back at its place is no longer
                // unrestored; the row would otherwise outlive the wiring it describes.
                ForgetFailure(key);
            }

            NoteHostsMayHaveChanged();
        }

        public bool TryRestore(object host, string storeFieldKey, out object value)
        {
            Debug.Assert(host != null, "host must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(storeFieldKey), "storeFieldKey must not be empty.");

            value = null;
            string identity = _resolver.DescribeHost(host);
            if (identity == null)
            {
                ReportOffMainThreadReadOfWiredField(host, storeFieldKey);
                return false;
            }

            HotReloadWiredValueHostKey key = new HotReloadWiredValueHostKey(identity, storeFieldKey);
            HotReloadWiredValueDescriptor descriptor;
            lock (_gate)
            {
                if (!Ledger.TryGet(key, out descriptor))
                {
                    return false;
                }

                if (descriptor.Kind == HotReloadWiredValueKind.Plain)
                {
                    value = descriptor.PlainValue;
                    ForgetRestoredFailures(host, key);
                    Report.AddRestored();
                    return true;
                }

                if (descriptor.Kind == HotReloadWiredValueKind.Unrestorable)
                {
                    RecordFailureOnce(key, descriptor.UnrestorableReason);
                    return false;
                }
            }

            bool resolved = _resolver.TryResolve(descriptor, out value, out string failureReason);
            lock (_gate)
            {
                if (resolved)
                {
                    ForgetRestoredFailures(host, key);
                    Report.AddRestored();
                    return true;
                }

                value = null;
                RecordFailureOnce(key, failureReason);
                return false;
            }
        }

        public bool TryRestoreAgain(object host, string storeFieldKey, ref int lastAttemptGeneration, out object value)
        {
            value = null;
            // Why not consume the generation off the main thread: the host cannot be named there,
            // so the attempt proves nothing, and the next main-thread read must still retry.
            if (!_resolver.IsMainThread)
            {
                return false;
            }

            int generation = Volatile.Read(ref _restoreGeneration);
            if (generation == lastAttemptGeneration)
            {
                return false;
            }

            lastAttemptGeneration = generation;
            return TryRestore(host, storeFieldKey, out value);
        }

        /// <summary>
        /// Moves the restore generation, so every slot whose restore failed asks once more on its
        /// next read.
        /// </summary>
        internal void NoteHostsMayHaveChanged()
        {
            Interlocked.Increment(ref _restoreGeneration);
        }

        /// <summary>
        /// Forgets every wired value, for a revert that removes the fields themselves.
        /// </summary>
        internal void Clear()
        {
            lock (_gate)
            {
                Ledger.Clear();
                // A revert removes the fields themselves, so no Play-only row is worth carrying.
                _undeliveredPlayOnlyFailures.Clear();
                ResetReport();
            }
        }

        /// <summary>
        /// Starts a fresh report before a scene reload, keeping the ledger so the reload's new
        /// instances can still be restored.
        /// </summary>
        internal void BeginSceneReloadSession()
        {
            lock (_gate)
            {
                ResetReport();
            }
        }

        /// <summary>
        /// Names, once per host and field, every recorded value whose host a finished scene reload
        /// did not put back at its place. The values stay recorded, except that on leaving Play
        /// Mode a value wired during Play whose host the Edit-time scene lacks is named with its
        /// own reason and forgotten.
        /// </summary>
        /// <remarks>
        /// Why after the reload, not in TryRestore: the replacement host reads under a different
        /// identity, so the ledger miss on that read cannot tell a never-wired field from a host
        /// that moved.
        /// Why the resolver is asked outside the lock: it reads the scene, and a slot read off the
        /// main thread would otherwise wait on it. Each identity is asked once per way of asking,
        /// however many fields it has.
        /// Why forget a Play-only host: no later reload can put it back, and a host is matched by
        /// its place alone, so keeping it would restore the value onto whatever object the next
        /// Play session creates at that place, and name it again on every transition until then.
        /// Why a Play-wired host found at its place on leaving Play loses its mark: the Edit-time
        /// scene has that place, so the host is not Play-only, and a later rename must keep the
        /// value until the host is put back rather than forget it.
        /// Why an unreadable scene counts as missing only for those: a host wired during Play may
        /// live in a scene only Play loads (an additive scene, DontDestroyOnLoad), while a host
        /// wired in Edit Mode is merely out of sight until its scene is opened again.
        /// </remarks>
        internal void ReportMissingHosts(bool leftPlayMode)
        {
            // Moved first so the early return below cannot skip it: a finished scene reload may
            // have put a host back even when none is missing.
            NoteHostsMayHaveChanged();

            List<(HotReloadWiredValueHostKey Key, bool PlayOnly)> entries =
                new List<(HotReloadWiredValueHostKey Key, bool PlayOnly)>();
            lock (_gate)
            {
                foreach (HotReloadWiredValueHostKey key in Ledger.SnapshotKeys())
                {
                    entries.Add((key, leftPlayMode && Ledger.WasWiredWhilePlaying(key)));
                }
            }

            Dictionary<(string Identity, bool PlayOnly), bool> missingByProbe =
                new Dictionary<(string Identity, bool PlayOnly), bool>();
            foreach ((HotReloadWiredValueHostKey key, bool playOnly) in entries)
            {
                (string Identity, bool PlayOnly) probe = (key.Identity, playOnly);
                if (!missingByProbe.ContainsKey(probe))
                {
                    missingByProbe[probe] = _resolver.IsHostMissing(key.Identity, playOnly);
                }
            }

            if (!leftPlayMode && !missingByProbe.ContainsValue(true))
            {
                return;
            }

            lock (_gate)
            {
                foreach ((HotReloadWiredValueHostKey key, bool playOnly) in entries)
                {
                    // A revert may have cleared the ledger while the lock was released.
                    if (!Ledger.TryGet(key, out _))
                    {
                        continue;
                    }

                    if (!missingByProbe[(key.Identity, playOnly)])
                    {
                        if (playOnly)
                        {
                            Ledger.ClearPlayMark(key);
                        }

                        continue;
                    }

                    if (!playOnly)
                    {
                        RecordFailureOnce(key, HostMissingReason);
                        continue;
                    }

                    // Named before it is removed: the failure row outlives the ledger entry.
                    RecordFailureOnce(key, PlayOnlyHostReason);
                    _undeliveredPlayOnlyFailures.Add(key);
                    Ledger.Remove(key);
                }
            }
        }

        /// <summary>
        /// The failures no earlier call returned, taken under the same lock that adds them, so a
        /// failure added during the drain is neither lost nor returned twice.
        /// </summary>
        internal IReadOnlyList<HotReloadWiredValueRestoreFailure> TakeUnreportedFailures()
        {
            lock (_gate)
            {
                _undeliveredPlayOnlyFailures.Clear();
                return Report.TakeUnreported();
            }
        }

        /// <summary>
        /// How many values came back and every failure so far in this session, copied under the
        /// lock so a status read sees one consistent state.
        /// </summary>
        internal (int RestoredCount, IReadOnlyList<HotReloadWiredValueRestoreFailure> Failures) ReadReport()
        {
            lock (_gate)
            {
                _undeliveredPlayOnlyFailures.Clear();
                return (Report.RestoredCount, new List<HotReloadWiredValueRestoreFailure>(Report.Failures));
            }
        }

        // Callers hold _gate.
        private void ResetReport()
        {
            Report.Reset();
            _reportedFailures.Clear();
            foreach (HotReloadWiredValueHostKey key in _undeliveredPlayOnlyFailures)
            {
                RecordFailureOnce(key, PlayOnlyHostReason);
            }
        }

        // Why off the main thread only: on it, a null identity means a plain C# host, which was
        // never recorded. Off it, the host cannot be named, so a wired value for the field is lost
        // once this read fills the slot with the initializer; say so rather than lose it silently.
        private void ReportOffMainThreadReadOfWiredField(object host, string storeFieldKey)
        {
            if (_resolver.IsMainThread)
            {
                return;
            }

            lock (_gate)
            {
                if (!Ledger.HasAnyForField(storeFieldKey))
                {
                    return;
                }

                RecordFailureOnce(
                    new HotReloadWiredValueHostKey(host.GetType().FullName, storeFieldKey), OffMainThreadReason);
            }
        }

        // Why once: a failed read through TryReadInstanceField creates no slot, so the same host
        // is asked again on every read; the set keeps the report at one line per host and field.
        // Callers hold _gate.
        private void RecordFailureOnce(HotReloadWiredValueHostKey key, string reason)
        {
            if (!_reportedFailures.Add(key))
            {
                return;
            }

            Report.AddFailure(key.Identity, key.StoreFieldKey, reason);
        }

        // Why the off-main-thread row too: a slot first read off the main thread keeps asking, so a
        // later main-thread read restores the value that row said was lost. Callers hold _gate.
        private void ForgetRestoredFailures(object host, HotReloadWiredValueHostKey key)
        {
            ForgetFailure(key);
            ForgetFailure(new HotReloadWiredValueHostKey(host.GetType().FullName, key.StoreFieldKey));
        }

        // Why: a value named as not restored that comes back later in the same session would
        // otherwise be listed as restored and unrestored at once. Callers hold _gate.
        private void ForgetFailure(HotReloadWiredValueHostKey key)
        {
            _undeliveredPlayOnlyFailures.Remove(key);
            if (_reportedFailures.Remove(key))
            {
                Report.RemoveFailure(key.Identity, key.StoreFieldKey);
            }
        }
    }
}
