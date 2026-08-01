namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Shared literals for the hot-reload pipeline (cache paths, Harmony id, patchability heuristics).
    /// </summary>
    internal static class HotReloadConstants
    {
        public const string HarmonyId = "io.github.hatayama.uloop.hot-reload";

        public const string ScriptAssembliesRelativeDirectory = "Library/ScriptAssemblies";
        public const string CompiledAssemblyExtension = ".dll";

        // Publicized reference copies are keyed by assembly name + Mvid so a recompiled assembly
        // never reuses a stale visibility rewrite.
        public const string PublicizedRefsRelativeDirectory = "Library/UloopHotReload/PublicizedRefs";

        public const string BurstCompileAttributeFullName = "Unity.Burst.BurstCompileAttribute";

        // Same heuristic threshold as pause point: small IL bodies may already be inlined by Mono,
        // in which case a Harmony detour on the original method will not reach existing call sites.
        public const int SmallMethodInliningRiskThresholdBytes = 32;

        public const string SmallMethodInliningRiskWarning =
            "This method is small enough (or marked [AggressiveInlining]) that the Mono JIT may "
            + "already have inlined it into callers; those call sites will keep running the old "
            + "body until they are recompiled.";

        public const string StaleAssemblyHint =
            "The loaded assembly no longer matches the compiled assembly on disk (a script compile "
            + "or domain reload may have happened). Hot reload is not needed — use uloop compile, "
            + "or wait for compilation to finish and retry.";

        public const string AssemblyNotLoadedHint =
            "The target assembly is not currently loaded in this AppDomain. Ensure the code path "
            + "that loads it has run, then retry.";
    }
}
