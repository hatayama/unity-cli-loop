namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// How a hot-reload patch replaces the target method's body.
    /// </summary>
    internal enum HotReloadPatchShape
    {
        // The shim method's IL is copied into the target and runs inside Harmony's
        // skip-visibility DynamicMethod.
        Transplant,

        // The target body becomes "forward all arguments to the shim and return". The shim
        // JIT-compiles normally; its inaccessible accesses were rewritten to accessor delegates.
        Delegation
    }
}
