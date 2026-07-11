namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Shared path and file-extension literals for resolving compiled script assemblies.
    /// </summary>
    internal static class SourcePausePointConstants
    {
        public const string ScriptAssembliesRelativeDirectory = "Library/ScriptAssemblies";
        public const string CompiledAssemblyExtension = ".dll";
        public const string DebugSymbolsExtension = ".pdb";
        public const string IsByRefLikeAttributeFullName = "System.Runtime.CompilerServices.IsByRefLikeAttribute";

        // Keeps a single hit's payload small enough for the CLI response and for the console-like
        // pause-point evidence to stay skimmable, mirroring the truncation-by-cap pattern MatchingLogs uses.
        public const int MaxCapturedVariableCount = 50;
        public const int MaxCapturedVariableValueLength = 256;

        public const string HarmonyId = "io.github.hatayama.uloop.source-pause-point";
        public const string BurstCompileAttributeFullName = "Unity.Burst.BurstCompileAttribute";

        // The only escape hatch a caller has when a method cannot be patched by file:line: the
        // hand-written marker path still works and does not depend on IL patching at all.
        public const string ManualMarkerFallbackHint =
            "This method cannot be safely patched by file:line. Add UloopPausePoint.Pause(\"id\") "
            + "directly in the source instead, then arm it with enable-pause-point --id \"id\".";
    }
}
