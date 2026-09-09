namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Why a hot-reload manifest entry could not be resolved to a loaded MethodBase.
    /// </summary>
    internal enum HotReloadMethodMatchFailureReason
    {
        None,
        CompiledAssemblyNotFound,
        TypeNotFound,
        MethodNotFound,
        AssemblyNotLoaded,
        StaleAssembly,
    }
}
