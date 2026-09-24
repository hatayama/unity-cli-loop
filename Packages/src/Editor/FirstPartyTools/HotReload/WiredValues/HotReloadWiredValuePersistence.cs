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
    /// </remarks>
    internal sealed class HotReloadWiredValuePersistence : IHotReloadWiredValuePersistence
    {
        private const string OffMainThreadReason = "read off the main thread before any main-thread read";

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

            Ledger.Record(new HotReloadWiredValueHostKey(identity, storeFieldKey), _resolver.DescribeValue(value));
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
            if (!Ledger.TryGet(key, out HotReloadWiredValueDescriptor descriptor))
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

            if (_resolver.TryResolve(descriptor, out value, out string failureReason))
            {
                Report.AddRestored();
                return true;
            }

            value = null;
            RecordFailureOnce(key, failureReason);
            return false;
        }

        /// <summary>
        /// Forgets every wired value, for a revert that removes the fields themselves.
        /// </summary>
        internal void Clear()
        {
            Ledger.Clear();
            BeginSceneReloadSession();
        }

        /// <summary>
        /// Starts a fresh report before a scene reload, keeping the ledger so the reload's new
        /// instances can still be restored.
        /// </summary>
        internal void BeginSceneReloadSession()
        {
            Report.Reset();
            _reportedFailures.Clear();
        }

        // Why off the main thread only: on it, a null identity means a plain C# host, which was
        // never recorded. Off it, the host cannot be named, so a wired value for the field is lost
        // once this read fills the slot with the initializer; say so rather than lose it silently.
        private void ReportOffMainThreadReadOfWiredField(object host, string storeFieldKey)
        {
            if (_resolver.IsMainThread || !Ledger.HasAnyForField(storeFieldKey))
            {
                return;
            }

            RecordFailureOnce(
                new HotReloadWiredValueHostKey(host.GetType().FullName, storeFieldKey), OffMainThreadReason);
        }

        // Why once: a failed read through TryReadInstanceField creates no slot, so the same host
        // is asked again on every read; the set keeps the report at one line per host and field.
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
