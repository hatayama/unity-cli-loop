namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Records values written through the wiring entry point and hands them back to the instance
    /// that replaces the recorded host after a scene reload. Implemented by hot reload, read
    /// through <see cref="HotReloadAddedFieldCoordination.WiredValues"/> and
    /// <see cref="HotReloadAddedFieldValues.Restorer"/>.
    /// </summary>
    public interface IHotReloadWiredValuePersistence
    {
        /// <summary>
        /// Remembers <paramref name="value"/> for the host's added field. Hosts it cannot identify
        /// again after a scene reload are ignored.
        /// </summary>
        void Record(object host, string storeFieldKey, object value);

        /// <summary>
        /// Looks up a remembered value for a host that has no slot yet. False when nothing was
        /// recorded for this host and field, or the recorded value cannot be resolved; the caller
        /// then runs the field's initializer.
        /// </summary>
        bool TryRestore(object host, string storeFieldKey, out object value);

        /// <summary>
        /// Retries a restore that failed before. Returns false without consulting the ledger while
        /// the restore generation equals <paramref name="lastAttemptGeneration"/>, or when called
        /// off the main thread; otherwise stores the current generation into
        /// <paramref name="lastAttemptGeneration"/> and restores like <see cref="TryRestore"/>.
        /// </summary>
        bool TryRestoreAgain(object host, string storeFieldKey, ref int lastAttemptGeneration, out object value);
    }
}
