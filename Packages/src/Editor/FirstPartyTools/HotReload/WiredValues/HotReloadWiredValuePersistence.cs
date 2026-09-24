using System.Collections.Generic;

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
    /// </remarks>
    internal sealed class HotReloadWiredValuePersistence : IHotReloadWiredValuePersistence
    {
        private const string OffMainThreadReason = "read off the main thread before any main-thread read";

        private readonly object _gate = new object();
        private readonly IHotReloadWiredValueResolver _resolver;
        private readonly HashSet<HotReloadWiredValueHostKey> _reportedFailures =
            new HashSet<HotReloadWiredValueHostKey>();

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

            HotReloadWiredValueDescriptor descriptor = _resolver.DescribeValue(value);
            lock (_gate)
            {
                Ledger.Record(new HotReloadWiredValueHostKey(identity, storeFieldKey), descriptor);
            }
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
                    Report.AddRestored();
                    return true;
                }

                value = null;
                RecordFailureOnce(key, failureReason);
                return false;
            }
        }

        /// <summary>
        /// Forgets every wired value, for a revert that removes the fields themselves.
        /// </summary>
        internal void Clear()
        {
            lock (_gate)
            {
                Ledger.Clear();
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
        /// The failures no earlier call returned, taken under the same lock that adds them, so a
        /// failure added during the drain is neither lost nor returned twice.
        /// </summary>
        internal IReadOnlyList<HotReloadWiredValueRestoreFailure> TakeUnreportedFailures()
        {
            lock (_gate)
            {
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
                return (Report.RestoredCount, new List<HotReloadWiredValueRestoreFailure>(Report.Failures));
            }
        }

        private void ResetReport()
        {
            Report.Reset();
            _reportedFailures.Clear();
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
    }
}
