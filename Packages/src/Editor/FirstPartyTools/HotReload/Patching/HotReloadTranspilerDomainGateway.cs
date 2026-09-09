namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The fixed entry point the Harmony transpilers reach the installed hot-reload domain
    /// through.
    /// </summary>
    /// <remarks>
    /// Why a static slot: Harmony resolves a transpiler as a static method with no arguments of
    /// ours, so the transpiler cannot be handed the domain and has to read it from somewhere
    /// fixed. It also runs outside an apply — patching or unpatching the same method from
    /// pause-point makes Harmony re-run every registered transpiler, including this tool's — so
    /// the slot has to outlive an apply scope. The composition root installs and clears it.
    /// </remarks>
    internal static class HotReloadTranspilerDomainGateway
    {
        /// <summary>
        /// The installed domain, or null while none is. Set by the composition root only.
        /// </summary>
        internal static HotReloadDomain Current { get; set; }
    }
}
