using System.Text.RegularExpressions;

using io.github.hatayama.UnityCliLoop.Runtime;

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
        // How many discarded variable names to surface when the count cap drops extras. The exact
        // discarded count is still reported in full via TruncatedVariableCount.
        public const int MaxTruncatedVariableNamesReported = 20;
        public const int MaxCapturedVariableValueLength = 256;
        // Mirrors UloopPausePointRegistry.DefaultMaxPreviewElements (the Runtime-owned per-marker
        // default enforced at Enable time) instead of a second independent literal, so the two
        // cannot drift apart.
        public const int MaxCollectionPreviewElementCount = UloopPausePointRegistry.DefaultMaxPreviewElements;
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

        // Unity's physics message dispatch (OnCollision*/OnTrigger*/OnParticleCollision) has been
        // observed in real projects to bypass a Harmony patch applied while the GameObject already
        // existed, so the pause point can silently miss even though the method body runs. The
        // trigger condition is environment-dependent and has not been reproduced deterministically
        // (fresh sessions, fresh Editor processes, primed JIT, runtime-created instances, and
        // one-hop indirect callees all patched correctly in controlled experiments; see
        // docs/regression-harness.md). A lighter enabled-toggle workaround was investigated and
        // rejected: every local "miss" that seemed to support it was a false positive where no new
        // callback ran during the check window, so only the mechanism-sound workarounds (recreate
        // the GameObject, or a manual marker) are recommended. This is informational only.
        public const string PhysicalCallbackMayMissExistingInstanceWarning =
            "This resolves to a Unity physics message method (OnCollision*/OnTrigger*/OnParticleCollision). "
            + "If the target GameObject already existed before this pause point was enabled, Unity's "
            + "cached message dispatch may not route through the patch and the pause point may never "
            + "hit even though the method body runs. If that happens, work around it by destroying and "
            + "recreating the GameObject after enabling this pause point, or embed "
            + "UloopPausePoint.Pause(\"id\") directly in the method body and arm it with "
            + "enable-pause-point --id instead.";

        // The same cached-dispatch risk as PhysicalCallbackMayMissExistingInstanceWarning, but for a
        // method that is not itself named after a physics message method and is instead called (one
        // level deep) from one elsewhere in the same compiled assembly - the call site scan cannot
        // tell whether the calling GameObject predates patching, so the same GameObject-recreation or
        // manual-marker workaround applies.
        public const string PhysicalCallbackIndirectCallMayMissExistingInstanceWarning =
            "This method is called from a Unity physics message method (OnCollision*/OnTrigger*/OnParticleCollision) "
            + "elsewhere in the same compiled assembly. If the target GameObject already existed before this pause "
            + "point was enabled, Unity's cached message dispatch may not route through the patch and the pause "
            + "point may never hit even though the method body runs. If that happens, work around it by destroying "
            + "and recreating the GameObject after enabling this pause point, or embed "
            + "UloopPausePoint.Pause(\"id\") directly in the method body and arm it with enable-pause-point --id "
            + "instead.";

        // Values captured inside a physics callback can be mid-solver intermediates (a Rigidbody
        // velocity may capture as zero even though the body visibly moves). Verification feedback
        // showed the existing cached-dispatch warnings say nothing about value reliability, so a
        // captured zero gets misread as a physics bug; the response itself must state the
        // discrimination rule instead of leaving it documented only in the skill references.
        public const string PhysicalCallbackMidSolverValuesWarning =
            "Rigidbody velocity values captured inside a physics callback can be mid-solver "
            + "intermediates; a captured (0, 0) is not proof the body is stationary. To tell them "
            + "apart, re-read the velocity live (execute-dynamic-code) after resuming: if it is "
            + "still zero outside the callback, suspect the game's own physics setup rather than "
            + "the capture.";

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

        // Surfaced only when a clear actually resumed Play Mode, so the caller understands the
        // clear had a side effect on run state. A manual pause (control-play-mode --action Pause
        // or the Editor pause button) is left untouched by clear and never triggers this.
        public const string ClearResumedPlayModeWarning =
            "This clear resumed Play Mode because the pause was owned by a pause-point hit. "
            + "A manual pause set outside the pause-point workflow would have been left untouched.";

        // Release code optimization strips most sequence points and hoists/elides locals, so the
        // Resolver's PDB-driven lookup cannot reliably find a patch location; rejecting up front
        // avoids patching the wrong instruction instead of failing later in a confusing way.
        public const string ReleaseCodeOptimizationRejectionMessage =
            "Enabling a pause point by file and line requires Debug code optimization. Automatic "
            + "switch to Debug and recompile did not leave the Editor in Debug; switch Code "
            + "Optimization to Debug (the bug icon in the main toolbar) and recompile, then retry.";

        // Machine-readable failure codes for enable/clear validation responses. Callers branch on
        // these instead of English Message substrings; names follow the existing PAUSE_POINT_*
        // vocabulary used by the CLI error envelope.
        public const string ErrorCodeInvalidArgument = "INVALID_ARGUMENT";
        public const string ErrorCodeReleaseCodeOptimization = "PAUSE_POINT_RELEASE_CODE_OPTIMIZATION";
        public const string ErrorCodeResolveFailed = "PAUSE_POINT_RESOLVE_FAILED";
        public const string ErrorCodePatchFailed = "PAUSE_POINT_PATCH_FAILED";
        public const string ErrorCodePausePointPatchedByHotReload = "PAUSE_POINT_PATCHED_BY_HOT_RELOAD";

        // Why: Debug mode is lost on every Editor restart (including uloop launch -r), so the
        // recovery steps must remind callers to re-switch after restart rather than only once.
        public const string ReleaseCodeOptimizationRecommendedNextAction =
            "Automatic Debug switch and recompile did not succeed. Confirm Code Optimization is Debug "
            + "(the bug icon in the main toolbar), run uloop compile, then retry enable-pause-point. "
            + "Note: the Debug setting reverts to the 'Code Optimization On Startup' preference "
            + "whenever the Editor restarts, including uloop launch -r.";

        // Why fill when empty: some Expired snapshots reach the response with no next
        // action, and agents then have no recovery for a timeout that fired during setup.
        public const string ExpiredRecommendedNextAction =
            "Re-enable the marker with a longer --timeout-seconds and trigger the code path again; clearing the expired marker first is not required.";

        // Why: resolve failures have several distinct root causes (wrong path form, non-executable
        // line, stale PDBs after a Code Optimization switch); the skill troubleshooting reference
        // covers the patterns so this next-action stays short and stable.
        public const string ResolveFailedRecommendedNextAction =
            "Check that --file is the project-relative path Unity shows (Assets/... or Packages/<package-id>/...) "
            + "and that --line is on or after an executable statement inside a method body. After a code edit "
            + "or a Code Optimization switch, run uloop compile and retry. See the pause-point skill's "
            + "troubleshooting reference for specific failure patterns.";

        // Auto-retarget failure reasons surfaced on status Warning / SuppressedByHotReloadReason.
        public const string RetargetOntoHotReloadFailedReason =
            "The marker's line no longer resolves inside the hot-reload patched body; it will not fire "
            + "until the patch is reverted, the line exists again, or 'uloop compile' runs.";

        public const string RestoreAfterHotReloadRevertFailedReason =
            "Instrumentation could not be restored after the hot-reload patch was reverted; the line "
            + "no longer resolves in the compiled assembly. Re-enable the marker after 'uloop compile'.";

        // Why: unpatched methods keep the compiled line map while the editor shows the edited
        // file. Agents otherwise arm a different method with no signal (FB9).
        public const string HotReloadCompiledLineMapWarningFormat =
            "'{0}' has active hot-reload patches. For methods this reload did not patch, --line "
            + "resolves against the last compiled source, not the edited file. Verify "
            + "ResolvedMethod and ResolvedLineText, or run 'uloop compile' and re-enable.";

        // Why a separate failure string: resolve failure leaves ResolvedMethod and
        // ResolvedLineText empty, so pointing at those fields is a dead end.
        public const string HotReloadCompiledLineMapResolveFailureWarningFormat =
            "'{0}' has active hot-reload patches. --line resolves against the last compiled source, "
            + "not the edited file, so a line number taken from the edited file can miss or fail to "
            + "resolve. Recompute the line against the last compiled source, or run 'uloop compile' "
            + "and re-enable.";

        public const string HotReloadCompiledLineMapResolveFailureNextAction =
            "Pass a line number from the last compiled source (the editor shows the edited file, "
            + "which can drift after hot reload), or run 'uloop compile' and re-enable the pause point.";

        // Format: file, resolved line, compiled line text, edited line text.
        public const string HotReloadCompiledLineMapLineDriftWarningFormat =
            "'{0}' line {1} is '{2}' in the last compiled source but '{3}' in the edited file. "
            + "The marker is armed on the compiled statement. If that is not the statement you meant, "
            + "recompute --line against the last compiled source, or run 'uloop compile' and re-enable.";

        public const string HotReloadCompiledLineMapLineDriftNextAction =
            "Verify ResolvedLineText is the statement you intended. If it is not, run 'uloop compile' "
            + "and re-enable the pause point.";

        // Format: declaring type name, method name, requested line.
        public const string HotReloadPatchedLineOutsidePatchedBodyMessageFormat =
            "'{0}.{1}' is currently hot-reload patched and line {2} does not fall inside any "
            + "hot-reload patched method's current body, so the marker cannot be placed reliably. "
            + "Line numbers are resolved against the last compiled source, so a line number taken "
            + "from the edited file can land inside a different method after edits shift lines. "
            + "Either the compiled line map for this file is stale, or the method's active patch "
            + "belongs to a superseded hot-reload generation.";

        public const string HotReloadPatchedLineOutsidePatchedBodyNextAction =
            "Pick a line inside the edited method body, run 'uloop hot-reload --revert-all' to "
            + "restore compiled bodies, or run 'uloop compile' to realign line numbers.";

        public const string HotReloadPatchedCompiledMethodSpanFormat =
            " In the last compiled source, '{0}.{1}' spans lines {2}-{3}.";
    }
}
