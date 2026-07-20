using System.Text.RegularExpressions;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Shared path and file-extension literals for resolving compiled script assemblies.
    /// </summary>
    internal static class SourcePausePointConstants
    {
        // Shared by the variable collector (to un-mangle the field name for capture) and the
        // collection preview serializer (to detect the same fields for preview formatting).
        public static readonly Regex AutoPropertyBackingFieldPattern =
            new(@"^<([^>]+)>k__BackingField$", RegexOptions.Compiled);

        public const string ScriptAssembliesRelativeDirectory = "Library/ScriptAssemblies";
        public const string CompiledAssemblyExtension = ".dll";
        public const string DebugSymbolsExtension = ".pdb";
        public const string IsByRefLikeAttributeFullName = "System.Runtime.CompilerServices.IsByRefLikeAttribute";

        // Keeps a single hit's payload small enough for the CLI response and for the console-like
        // pause-point evidence to stay skimmable, mirroring the truncation-by-cap pattern MatchingLogs uses.
        public const int MaxCapturedVariableCount = 50;
        public const int MaxCapturedVariableValueLength = 256;
        public const int MaxCollectionPreviewElementCount = 10;
        public const int MaxCollectionPreviewValueLength = 1024;
        public const int MaxCollectionPreviewDepth = 2;

        public const string HarmonyId = "io.github.hatayama.uloop.source-pause-point";
        public const string BurstCompileAttributeFullName = "Unity.Burst.BurstCompileAttribute";

        // A heuristic threshold, not a guarantee: Mono's JIT inlining decision depends on far more
        // than IL byte count (call-site count, caller size, tiering), so this only flags methods
        // small enough that inlining is plausible, to explain a HitCount=0 symptom after the fact.
        public const int SmallMethodInliningRiskThresholdBytes = 32;

        // The only escape hatch a caller has when a method cannot be patched by file:line: the
        // hand-written marker path still works and does not depend on IL patching at all.
        public const string ManualMarkerFallbackHint =
            "This method cannot be safely patched by file:line. Add UloopPausePoint.Pause(\"id\") "
            + "directly in the source instead, then arm it with enable-pause-point --id \"id\".";

        // The manual-marker fallback would not run here either: it lives in a source file that
        // belongs to the very assembly that is not loaded yet.
        public const string AssemblyNotLoadedHint =
            "The assembly this pause point resolves to is not currently loaded in this AppDomain. "
            + "Ensure the code path that loads it (e.g. entering Play Mode) has run, then retry.";

        // A stale resolution means the assembly was recompiled after Resolve ran; re-resolving
        // against the current compiled output (rather than falling back to a manual marker) is
        // the correct next step here.
        public const string StaleAssemblyHint =
            "The loaded assembly no longer matches the compiled assembly this pause point was "
            + "resolved from (a script compile or domain reload may have happened since). Wait for "
            + "compilation/domain reload to finish, then resolve and patch again.";

        // A byref-like `this` cannot be boxed, so the patcher degrades to a null instance rather
        // than rejecting the patch outright; locals and parameters are still captured normally.
        public const string RefStructInstanceNotCapturedWarning =
            "The declaring type is a ref struct; this-instance fields are not captured "
            + "(locals and parameters are still captured normally).";

        // Unity's physics message dispatch (OnCollision*/OnTrigger*/OnParticleCollision) resolves
        // its call path once when the GameObject registers with the physics engine; a Harmony
        // patch applied after that registration does not reach the cached path, so the pause
        // point can silently miss a GameObject that already existed before this call. This is
        // informational only: the same method on a newly created GameObject patches correctly.
        public const string PhysicalCallbackMayMissExistingInstanceWarning =
            "This resolves to a Unity physics message method (OnCollision*/OnTrigger*/OnParticleCollision). "
            + "If the target GameObject already existed before this pause point was enabled, Unity's "
            + "cached message dispatch may not route through the patch and the pause point may never "
            + "hit even though the method body runs. Workarounds: destroy and recreate the GameObject "
            + "after enabling this pause point, or embed UloopPausePoint.Pause(\"id\") directly in the "
            + "method body and arm it with enable-pause-point --id instead.";

        // Surfaces the same JIT-inlining risk documented under Requirements & Safety in the skill,
        // but at enable time instead of only after a confusing HitCount=0 timeout.
        public const string SmallMethodInliningRiskWarning =
            "The target method body is very small and may be inlined by Mono's JIT into its callers; "
            + "if HitCount stays 0 while the line demonstrably runs, move the pause point into the calling method.";

        // Callers have observed captured values that look like they belong to the line after
        // ResolvedLine; this makes the pre-line snapshot timing explicit in the response itself
        // instead of leaving it documented only in the skill.
        public const string PreLineSnapshotTimingNote =
            "pre-line: variables are captured before ResolvedLine executes";

        // Release code optimization strips most sequence points and hoists/elides locals, so the
        // Resolver's PDB-driven lookup cannot reliably find a patch location; rejecting up front
        // avoids patching the wrong instruction instead of failing later in a confusing way.
        public const string ReleaseCodeOptimizationRejectionMessage =
            "Enabling a pause point by file and line requires Debug code optimization. The project "
            + "is currently set to Release; switch the Editor's Code Optimization mode to Debug "
            + "(the bug icon in the main toolbar) and recompile, then retry.";
    }
}
