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
    }
}
