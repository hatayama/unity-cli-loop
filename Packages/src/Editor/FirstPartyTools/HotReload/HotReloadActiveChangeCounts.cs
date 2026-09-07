namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The one place the hot-reload side reads how many runtime changes this domain holds.
    /// </summary>
    /// <remarks>
    /// Why a single accessor: the registry counts artifact assemblies as well as types, and a
    /// report that reached for the artifact count would total two types of one batch as one. Every
    /// consumer that wants a type count goes through here so only one call site can be wrong.
    /// </remarks>
    internal static class HotReloadActiveChangeCounts
    {
        internal static int IntroducedTypeCount => HotReloadIntroducedTypeHolder.Registry.ActiveTypeCount;

        /// <summary>
        /// How many hot-reload changes the next Domain Reload discards: patched methods, added
        /// members, and the types this domain introduced.
        /// </summary>
        /// <remarks>
        /// Why the types belong here: they live in artifact assemblies the reload retained, so a
        /// Domain Reload unloads them exactly as it unloads a patch. A run that patched no method
        /// still has something to lose, and every decision about what a reload would lose reads
        /// this total rather than counting for itself.
        /// </remarks>
        internal static int RuntimeChangeTotal => HotReloadPatcher.ActiveChangeCount + IntroducedTypeCount;
    }
}
