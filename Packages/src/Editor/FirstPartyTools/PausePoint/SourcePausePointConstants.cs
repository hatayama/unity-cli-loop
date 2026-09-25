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

        // Why name patterns instead of HotReloadCallSiteScanner's attribute index: PausePoint
        // cannot reference that assembly, and --method is a source name. Roslyn still encodes
        // async/iterator owners as nested `<Name>d__N` types and local functions as
        // `<Outer>g__Name|x_y`; lambdas (`<Outer>b__…`) have no source name and stay unmatched.
        public static readonly Regex StateMachineTypeNamePattern =
            new(@"^<([^>]+)>d__\d+$", RegexOptions.Compiled);
        public static readonly Regex LocalFunctionMethodNamePattern =
            new(@"^<[^>]+>g__([^|]+)\|\d+(?:_\d+)?$", RegexOptions.Compiled);

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

        // Why this wording: C# foreach over a multidimensional array is row-major (the last
        // dimension varies fastest), and the preview Elements array is that same flattening.
        public const string MultidimensionalArrayElementOrder = "row-major (last dimension fastest)";

        // Why three distinct notes: File/Line omission has three causes that look identical on
        // the wire without Note. NormalizeFilePath returning null covers both missing FileName
        // and a non-project path, so that null must not be labeled "outside the project".
        public const string CallerFrameDynamicMethodNote =
            "dynamic method (patched by hot reload or pause-point instrumentation); no debug symbols";
        public const string CallerFrameMissingDebugSymbolsNote =
            "no source file information; the frame's assembly has no debug symbols";
        public const string CallerFrameOutsideProjectNote =
            "source file is outside the Unity project";

        // Default nearest caller plus one more. enable-pause-point --max-caller-frames can raise
        // or disable this per marker (0 skips capture; the examine walk stays capped at 24).
        public const int MaxCallerFrames = UloopPausePointRegistry.DefaultMaxCallerFrames;
        // Walk this many raw stack frames so skipped infrastructure still leaves room for two callers.
        public const int MaxCallerStackFramesToExamine = 24;

        // Frame identity of a Harmony patch body on Mono: MonoMod assigns this declaring type
        // to every DynamicMethodDefinition-generated method, and Harmony names the patch
        // "{OriginalType}.{OriginalMethod}_Patch{N}". Both are needed to tell a real patched
        // application caller apart from genuine MonoMod infrastructure frames.
        public const string HarmonyDynamicMethodDeclaringType = "MonoMod.Utils.DynamicMethodDefinition";
        public const string HarmonyPatchNameSuffix = "_Patch";

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

        // Format: Type.Method display name, effective max-history.
        // Why name-based, not a MonoBehaviour type check: a plain C# Update is often driven
        // every frame by a MonoBehaviour delegate, and a type check would miss that case.
        // Why conditional wording: the name match is not proof this is a Unity message, so
        // "is a per-frame Unity message" would be false for non-MonoBehaviour types.
        public const string PerFrameTraceNoticeFormat =
            "'{0}' matches a per-frame Unity message name; if this line runs every frame, capture mode 'trace' can roll the history (max {1}) over within moments. Prefer --hit-when, a conditional line, or a larger --max-history.";

        // Why name-based, not a MonoBehaviour type check: a plain C# Update is often driven
        // every frame by a MonoBehaviour delegate, and a type check would miss that case.
        // Why conditional wording: the name match is not proof this is a Unity message, so
        // "is a per-frame Unity message" would be false for non-MonoBehaviour types.
        public const string PerFrameImmediateHitNoticeFormat =
            "'{0}' matches a per-frame Unity message name; if the resolved line executes unconditionally, the marker hits on the next frame, before the input or event you meant to observe arrives. Prefer a line that only executes when that event happens (inside its guarding if), or hold the input down with simulate-keyboard KeyDown before arming.";

        // Callers have observed captured values that look like they belong to the line after
        // ResolvedLine; this makes the pre-line snapshot timing explicit in the response itself
        // instead of leaving it documented only in the skill.
        public const string PreLineSnapshotTimingNote =
            "pre-line: variables are captured before ResolvedLine executes";

        public const string PostLineSnapshotTimingNote =
            "post-line: variables are captured after ResolvedLine finishes, before control leaves it";

        public const string PreLineSnapshotTimingValue = "pre-line";

        public const string PostLineSnapshotTimingValue = "post-line";

        // Reasons a parameter is left out of CapturedVariables. Capture boxes every value into
        // the snapshot, and these three shapes cannot be boxed at all, so naming the shape tells
        // the caller why a name they expected is absent and which workaround applies.
        public const string NotCapturableByRefParameterReason = "ref/out/in parameter cannot be boxed";
        public const string NotCapturablePointerReason = "pointer cannot be boxed";
        public const string NotCapturableRefStructReason = "ref struct cannot be boxed";

        // Named at enable time so the caller learns the parameter is missing before running the
        // code path, instead of reading CapturedVariables afterwards and suspecting a capture bug.
        public const string NotCapturableParametersWarningFormat =
            "Parameters not captured because they cannot be boxed: {0}. Copy the value it refers to"
            + " into a plain local (dereference a pointer, ToArray() a span), or use"
            + " --snapshot-timing post-line on the line that consumes it.";

        // Why a resolve failure rather than a silent pre-line fallback: a statement that
        // always throws has no "after", and capturing before it would report values the
        // caller explicitly asked not to see.
        public const string PostLineAlwaysThrowsMessageFormat =
            "Line {0} of {1} always throws, so there is no point after it to capture. Use --snapshot-timing pre-line or choose another line.";

        // Surfaced only when a clear actually released EditorApplication.isPaused, so the caller
        // understands the clear had a side effect on run state. Worded around the Editor pause
        // rather than Play Mode because a hit can also pause in EditMode (a marker reached through
        // execute-dynamic-code), where "resumed Play Mode" would describe a state that never
        // existed. A manual pause (control-play-mode --action Pause or the Editor pause button)
        // is left untouched by clear and never triggers this.
        public const string ClearReleasedOwnedPauseWarning =
            "This clear released the Editor pause because it was owned by a pause-point hit. "
            + "A manual pause set outside the pause-point workflow would have been left untouched.";

        // Paired with ClearReleasedOwnedPauseWarning, which only explains what happened. Callers
        // that arranged a scenario while paused lose it the moment the clear releases the pause,
        // so the recovery path (re-pause, re-arrange) and the ordering that avoids the loss next
        // time (arm the replacement marker before clearing the current one) have to be stated as
        // the next action, not left for the caller to infer from the warning. Worded around the
        // Editor pause for the same reason as that warning: a marker reached through
        // execute-dynamic-code pauses in EditMode, where naming Play Mode would be false.
        public const string ClearReleasedPauseRecommendedNextAction =
            "The Editor pause was released. If you needed the paused state, pause again "
            + "(control-play-mode --action Pause while in Play Mode), re-arrange it, and next "
            + "time arm the new marker before clearing this one.";

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
        public const string ErrorCodePatchedSourceChanged = "PAUSE_POINT_PATCHED_SOURCE_CHANGED";
        // A --line that is not in the last compiled source (added or changed since, or past the end
        // of the file) has its own code because the fix is to compile or pick a compiled line,
        // not to correct the path or the line syntax as RESOLVE_FAILED asks.
        public const string ErrorCodePausePointLineNotCompiled = "PAUSE_POINT_LINE_NOT_COMPILED";

        // Why: Debug mode is lost on every Editor restart (including uloop launch -r), so the
        // recovery steps must remind callers to re-switch after restart rather than only once.
        public const string ReleaseCodeOptimizationRecommendedNextAction =
            "Automatic Debug switch and recompile did not succeed. Confirm Code Optimization is Debug "
            + "(the bug icon in the main toolbar), run uloop compile, then retry enable-pause-point. "
            + "Note: the Debug setting reverts to the 'Code Optimization On Startup' preference "
            + "whenever the Editor restarts, including uloop launch -r. "
            + "To make Unity start in Debug permanently, run: uloop set-code-optimization debug --startup "
            + "(machine-wide: applies to every Unity project on this machine; only your project's C# script "
            + "execution slows down, mainly during Play Mode - the Unity Editor itself is not slowed).";

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

        // Why a second next action: on the edited-file path the file was already found in a compiled
        // assembly, so the path form is not the cause, and the lines in the message are already the
        // caller's. Compile helps only when the wanted statement was added after the last compile.
        public const string ResolveFailedEditedFileRecommendedNextAction =
            "Pass --line on an executable statement inside a method body, using the line numbers of the "
            + "file on disk. If that statement was added after the last compile, run 'uloop compile' "
            + "first, then retry.";

        // Why a clause put before the line advice: a --method that names no method in the file fails
        // on every line and after every compile, so the line advice alone cannot change the outcome.
        public const string ResolveFailedMethodFilterNextActionPrefix =
            "Check that --method is the simple name or 'Type.Method' of a method in --file (the match is "
            + "case-sensitive), or drop --method. ";

        // Why a next action of its own: the statement always throws, so neither the path form nor a
        // compile changes the outcome; only the timing or the line does.
        public const string PostLineAlwaysThrowsRecommendedNextAction =
            "Retry with --snapshot-timing pre-line, or pass --line for a statement that does not always throw.";

        // Format: requested edited-file line, project-relative file.
        public const string ResolveFailedNoCompiledStatementInEditedFileMessageFormat =
            "No compiled statement exists on or after line {0} of '{1}' (line numbers of the file on disk).";

        // Format: method filter, requested edited-file line, project-relative file.
        public const string ResolveFailedNoMethodNamedInEditedFileMessageFormat =
            "No method named '{0}' has a compiled statement on or after line {1} of '{2}' (line numbers of the file on disk).";

        // Why a failure text of its own: such a file has no compiled code at all, so the
        // generic advice to fix the path or recompute the line points at causes that cannot apply.
        // Format: the file as the caller passed it.
        public const string IntroducedTypeResolveFailureMessageFormat =
            "'{0}' declares a type hot reload introduced without a compile, so it has no compiled "
            + "code to resolve --line against. Pause points can bind only to methods hot reload "
            + "has patched in this file.";

        // Why a warning rather than the failure text: the file also holds compiled types, so the
        // general guidance still applies and only the lines inside the introduced type differ.
        // Format: the file as the caller passed it.
        public const string IntroducedTypeInFileWarningFormat =
            "'{0}' also declares a type hot reload introduced without a compile; lines inside that "
            + "type have no compiled code, and a pause point binds there only to a method hot "
            + "reload has patched.";

        public const string IntroducedTypeResolveFailureNextAction =
            "Edit the target method body and run 'uloop hot-reload --files <this file>' so the method "
            + "is reported Patched, then enable the pause point again on a line inside that method. "
            + "Or run 'uloop compile' to compile the type and use the normal path.";

        // Why a refusal of its own: an added method exists only in the hot reload shim, which
        // pause-point cannot arm, and the compiled code has no counterpart for it. Without
        // this the compiled resolver would arm the next compiled method or report a wrong line.
        // Format: requested line, added method name.
        public const string AddedMethodResolveFailureMessageFormat =
            "Line {0} is inside '{1}', which hot reload added; pause points cannot be armed inside "
            + "added methods until 'uloop compile', so it was refused instead of arming another method.";

        // Why a variant: when the requested line has no statement and the next statement is the
        // one inside the added method, "Line {0} is inside" would place the requested line in a
        // method it is not part of. Format: requested line, added method line, added method name.
        public const string AddedMethodNextStatementResolveFailureMessageFormat =
            "Line {0} has no compiled statement of its own, and the next statement, line {1}, is "
            + "inside '{2}', which hot reload added; pause points cannot be armed inside added "
            + "methods until 'uloop compile', so it was refused instead of arming another method.";

        // Why refuse rather than remap: the running patch maps lines of the source it was compiled
        // from, which is not kept, so a marker placed now would stop at another statement than the
        // one the response shows from the file on disk.
        public const string PatchedSourceChangedOnDiskMessageFormat =
            "'{0}' changed on disk after hot reload patched it (the last reload of this file did not "
            + "apply, or the file was edited since), so --line {1} cannot be matched: the running patch "
            + "still follows the source it was compiled from, and a marker placed now would stop at a "
            + "different statement than the one shown.";

        public const string PatchedSourceChangedOnDiskHint =
            "Run 'uloop hot-reload' on the file again (fix any compile error first) or revert the edit, "
            + "then enable the pause point.";

        public const string AddedMethodResolveFailureNextAction =
            "Run 'uloop compile', then enable the pause point on this line again.";

        // Format: method filter, requested line.
        public const string NoMethodNamedWithSequencePointMessageFormat =
            "No method named '{0}' with a sequence point on or after line {1} was found.";

        // Auto-retarget failure reasons surfaced on status Warning / SuppressedByHotReloadReason.
        public const string RetargetOntoHotReloadFailedReason =
            "The marker's line no longer resolves inside the hot-reload patched body; it will not fire "
            + "until the patch is reverted, the line exists again, or 'uloop compile' runs.";

        public const string RestoreAfterHotReloadRevertFailedReason =
            "Instrumentation could not be restored after the hot-reload patch was reverted; the line "
            + "no longer resolves in the compiled assembly. Re-enable the marker after 'uloop compile'.";

        // Why name only the edited body: --line is an edited-file line, so the edited range of
        // the patched method is the one place the caller can move the line to and still pause
        // in the running code. Format: requested line, patched method display name, edited start
        // line, edited end line.
        public const string HotReloadPatchedMethodRefusalMessageFormat =
            "Line {0} of the edited file resolves to a statement inside '{1}', which hot reload "
            + "patched, so the compiled body no longer runs. Pass --line inside the method's edited "
            + "body (lines {2}-{3}).";

        // Format: same arguments as HotReloadPatchedMethodRefusalMessageFormat.
        public const string HotReloadPatchedMethodRefusalNextActionFormat =
            "Retry with --line between {2} and {3}.";

        // Why a variant: when the requested line has no statement and the next statement is
        // inside the patched method, "Line {0} resolves to a statement inside" would place the
        // requested line in a method it is not part of. Format: requested line, statement line
        // inside the patched method, patched method display name, edited start line, edited end line.
        public const string HotReloadPatchedMethodNextStatementRefusalMessageFormat =
            "Line {0} has no compiled statement of its own, and the next statement, line {1}, is "
            + "inside '{2}', which hot reload patched, so the compiled body no longer runs. Pass "
            + "--line inside the method's edited body (lines {3}-{4}).";

        // Why a range-less variant: without the method's shim entry the edited range is unknown,
        // and a guessed range would send the caller to a line that is not in the patched body.
        // Format: requested line, patched method display name.
        public const string HotReloadPatchedMethodWithoutSpanRefusalMessageFormat =
            "Line {0} of the edited file resolves to a statement inside '{1}', which hot reload "
            + "patched, so the compiled body no longer runs. Pass --line inside the method's edited "
            + "body, or run 'uloop compile' and retry.";

        public const string HotReloadPatchedMethodWithoutSpanRefusalNextAction =
            "Pass --line inside the patched method's edited body, or run 'uloop compile' and retry.";

        // Why name the row: the method's Methods[] row in the last hot reload response gives the
        // Reason that reload did not apply it, which is what the caller has to change before a
        // reload replaces the earlier body. Format: requested line, method display name, verb
        // (HotReloadLeftBehindSkippedVerb or HotReloadLeftBehindFailedVerb), row label.
        public const string HotReloadLeftBehindMethodRefusalMessageFormat =
            "Line {0} of the edited file resolves to a statement inside '{1}', which the last hot "
            + "reload of this file {2}, so what runs for it is still the body an earlier hot reload "
            + "applied. That body matches neither the compiled assembly nor the file on disk, so no "
            + "line of this file can be armed in it; the Reason of the Methods[] row '{3}' in that "
            + "hot reload response names what to change.";

        public const string HotReloadLeftBehindSkippedVerb = "skipped";

        public const string HotReloadLeftBehindFailedVerb = "could not apply";

        public const string HotReloadLeftBehindMethodRefusalNextAction =
            "Change what that Reason names and run 'uloop hot-reload' on the file again, or run "
            + "'uloop compile'; then retry with the same --line.";

        // Why only compile: the last reload read the file as it is and left no row unapplied, so
        // reloading the same contents leaves the earlier body running. Format: requested line,
        // method display name.
        public const string HotReloadEarlierPatchRefusalMessageFormat =
            "Line {0} of the edited file resolves to a statement inside '{1}', which still runs the "
            + "body an earlier hot reload applied: the last hot reload of this file read the file on "
            + "disk as it is now and did not apply '{1}' again, so no line of this file can be armed "
            + "in it.";

        public const string HotReloadEarlierPatchRefusalNextAction =
            "Run 'uloop compile', then retry with the same --line.";

        // Why the rows and a reload: the last reload read the file as it is and left no row for
        // the method, but the rows it did leave name what kept the file from being applied, and
        // the error of a '(file)' row stops a compile too. Format: requested line, method display
        // name, the rows as the LINE_NOT_COMPILED refusal lists them.
        public const string HotReloadEarlierPatchLeftRowsRefusalMessageFormat =
            "Line {0} of the edited file resolves to a statement inside '{1}', which still runs the "
            + "body an earlier hot reload applied, so no line of this file can be armed in it: the "
            + "last hot reload of this file read the file on disk as it is now and left these "
            + "Methods[] rows unapplied: {2}.";

        public const string HotReloadEarlierPatchLeftRowsRefusalNextAction =
            "Change what those rows' Reasons name and run 'uloop hot-reload' on the file again, or run "
            + "'uloop compile'; then retry with the same --line.";

        public const string NearbyCompiledMethodsPrefix =
            " Nearby methods in the last compiled source: ";

        // Why a second prefix: on the edited-file path the spans are mapped to the file on disk, so
        // "in the last compiled source" would tell the caller to read them as compiled lines.
        public const string NearbyCompiledMethodsEditedLinesPrefix =
            " Nearby compiled methods, with line numbers of the file on disk: ";

        public const string NearbyCompiledMethodSpanFormat = "'{0}' spans lines {1}-{2}";

        // Format: patched method display name, requested line.
        // Why a refusal of its own: without the shim PDB no running-code statement can be matched
        // to the line, and arming the compiled body would pause code that no longer runs.
        public const string HotReloadPatchedMethodPdbUnavailableWarningFormat =
            "--line {1} falls inside hot-reload patched method '{0}', but this patch has no debug "
            + "symbols, so a pause point cannot be placed on the running code. Run 'uloop compile' "
            + "and re-enable.";

        public const string HotReloadPatchedMethodPdbUnavailableNextAction =
            "Run 'uloop compile' and re-enable the pause point.";

        // Format: resolved method display name, requested line, edited start line, edited end line.
        public const string HotReloadRetargetedToEditedFileWarningFormat =
            "--line {1} was resolved against the edited file because it falls inside hot-reload "
            + "patched method '{0}' (edited lines {2}-{3}). If you meant a different method, "
            + "verify ResolvedMethod, or pass a line outside patched methods' edited spans.";

        // Why this wording: agents that followed the old "read via execute-dynamic-code"
        // guidance wasted two round-trips on CS1061, since that command compiles against the
        // compiled assembly; agents told to read from a patched body instead edited code just to
        // look. The wiring entry point reads the shim's value by name from a snippet directly.
        // Format: declaring type name, added-field count, comma-separated simple field names.
        public const string HotReloadAddedFieldsNotCapturedWarningFormat =
            "Hot reload added {1} field(s) to '{0}' ({2}); they never appear in CapturedVariables, "
            + "and naming them as members in 'uloop execute-dynamic-code' fails with CS1061 "
            + "because it compiles against the compiled assembly. Read one there with "
            + "HotReloadAddedFieldWiring.TryReadInstanceField (TryReadStaticField for a static "
            + "field); references/added-field-wiring.md in the uloop-hot-reload skill shows how.";

        // Why fill on success: a successful enable currently leaves RecommendedNextAction empty,
        // so agents arm a marker and then stall instead of running the path or using --await.
        // Format: marker id.
        public const string EnableSuccessArmingRecommendedNextActionFormat =
            "Run the code path so the marker can hit, then read the outcome with: uloop pause-point-status --id \"{0}\". To block until it hits without a trigger command (e.g. waiting for physics or a multi-step action): uloop await-pause-point --id \"{0}\" --timeout-seconds <n>. To arm, trigger, and collect in one call: uloop enable-pause-point --await --resume-play --trigger \"<uloop subcommand without the leading 'uloop', e.g. simulate-keyboard --action Press --key Space>\".";

        // Why warn: Registry.Enable replaces the entry and drops CapturedVariables,
        // CapturedVariableHistory, and hit snapshots. The raw capture holder is kept on purpose.
        // Format: previous generation number.
        public const string RearmDiscardCapturedVariablesWarningFormat =
            "Generation {0} of this pause point had already hit; this re-arm discarded its CapturedVariables and CapturedVariableHistory. Read results with pause-point-status before re-arming when you need them.";

        // Why not gated on hot reload: a closing-brace line is misleading whether or not the file
        // has active hot-reload patches.
        // Format: resolved line, resolved method display name.
        public const string ClosingBraceResolvedLineWarningFormat =
            "--line resolved to the method's closing brace at line {0}. Every return path through {1} reaches this line, including early returns, so captured variables can reflect a different path than the one you meant. To observe one specific path, target a statement line inside that path.";

        // The line-not-compiled refusal messages name only the requested line and the line
        // that blocks it. Why no "the next compiled line is N" hint: suggesting another line made
        // agents retry with numbers from a different coordinate system, and the fix is to compile
        // or to pick an unchanged statement the caller can see in the editor.
        // Format: requested line, file, edited file line count.
        public const string LineNotCompiledBeyondEndOfFileMessageFormat =
            "Line {0} is beyond the end of '{1}' ({2} lines).";

        // Format: requested line, file.
        public const string LineNotCompiledNoCompiledLineAtOrAfterMessageFormat =
            "No compiled line exists at or after line {0} of '{1}'; the lines from {0} to the end of the file are not in the last compiled source.";

        // Format: requested line, file, trimmed text of the requested line.
        public const string LineNotCompiledChangedLineMessageFormat =
            "Line {0} of '{1}' ('{2}') is not in the last compiled source (added or changed after the last compile).";

        // Format: requested line, file, uncompiled statement line, its trimmed text.
        public const string LineNotCompiledNextStatementUncompiledMessageFormat =
            "Line {0} of '{1}' has no compiled statement of its own, and the next statement, line {2} ('{3}'), is not in the last compiled source (added or changed after the last compile).";

        // Format: requested line, file, trimmed text of the compiled statement the line resolved to.
        public const string LineNotCompiledStatementRemovedMessageFormat =
            "Line {0} of '{1}' resolves to the compiled statement '{2}', which no longer exists at that place in the edited file (removed or changed after the last compile).";

        public const string LineNotCompiledRecommendedNextAction =
            "Run 'uloop hot-reload' (a pause point inside a hot-reload patched method arms the edited code) or 'uloop compile', then retry with the same --line. To arm compiled code instead, pass --line for a statement that is unchanged since the last compile.";

        // Why say what the last reload did: once it read the file as it is on disk, hot reloading
        // the same contents again gives the same result, so the shared "hot-reload or compile"
        // next action would return this refusal again. Format: the unapplied rows, described.
        public const string LineNotCompiledLatestReloadLeftRowsSuffixFormat =
            " The last hot reload of this file read it as it is on disk now and left these Methods[] "
            + "rows unapplied: {0}. Hot reloading the same contents again leaves them unapplied.";

        public const string LineNotCompiledLatestReloadAppliedAllSuffix =
            " The last hot reload of this file read it as it is on disk now, so hot reloading it "
            + "again does not change this line.";

        public const string LineNotCompiledLatestReloadLeftRowsRecommendedNextAction =
            "If one of those rows covers this line (its method holds the line, or it is a '(file)' "
            + "row), change what that row's Reason names and run 'uloop hot-reload' again; otherwise, "
            + "or to run the edited code as written, run 'uloop compile'. Then retry with the same "
            + "--line. To arm compiled code instead, pass --line for a statement that is unchanged "
            + "since the last compile.";

        public const string LineNotCompiledLatestReloadAppliedAllRecommendedNextAction =
            "Run 'uloop compile', then retry with the same --line. To arm compiled code instead, pass "
            + "--line for a statement that is unchanged since the last compile.";

        // Why a next action of its own: hot reload and compile do not change how many lines the
        // file on disk has, so the shared "hot-reload or compile, then retry with the same --line"
        // would return the same refusal. Format: line count of the file on disk.
        public const string LineNotCompiledBeyondEndOfFileRecommendedNextActionFormat =
            "Pass --line between 1 and {0}; the file on disk has {0} lines. If your editor shows more "
            + "lines, save the file and retry.";

        // Why only a warning: without a verified snapshot there is nothing to map edited lines
        // against, and refusing would make pause points unusable in files such as package sources.
        // Format: file, requested line.
        public const string NoVerifiedSnapshotLineBasisWarningFormat =
            "No verified source snapshot exists for '{0}', so --line {1} was resolved against the last compiled source and ResolvedLine is a compiled line number. Run 'uloop compile' to refresh the snapshot.";
    }
}
