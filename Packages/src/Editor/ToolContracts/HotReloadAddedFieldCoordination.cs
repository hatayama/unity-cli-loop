namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Where the validated added-field entry point finds the added fields of the installed
    /// hot-reload domain. Null until hot reload installs a domain.
    /// </summary>
    /// <remarks>
    /// Why this is separate from <see cref="HotReloadAddedFieldStore"/>: that store is the gateway
    /// emitted shim IL calls on every field access, and the declaration lookup has nothing to do
    /// with that path.
    /// </remarks>
    public static class HotReloadAddedFieldCoordination
    {
        /// <summary>
        /// Set by the hot-reload composition root when it installs a domain, and cleared when it
        /// uninstalls one. Never points at a domain that is no longer installed.
        /// </summary>
        public static IHotReloadAddedFieldPort ActiveFields { get; set; }

        /// <summary>
        /// Where the wiring entry point records what it wrote, so the value can follow the host
        /// across a scene reload. Set and cleared by the composition root together with
        /// <see cref="ActiveFields"/>.
        /// </summary>
        public static IHotReloadWiredValuePersistence WiredValues { get; set; }
    }
}
