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

        // Why a failure text of its own: such a file has no compiled line map at all, so the
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
        // pause-point cannot arm, and the compiled line map has no counterpart for it. Without
        // this the compiled resolver would arm the next compiled method or report a wrong line.
        // Format: requested line, added method name.
        public const string AddedMethodResolveFailureMessageFormat =
            "Line {0} is inside '{1}', which hot reload added; pause points cannot be armed inside "
            + "added methods until 'uloop compile', so it was refused instead of arming another method.";

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

        // Why: unpatched methods keep the compiled line map while the editor shows the edited
        // file. Naming the resolved method tells agents the marker is on an unpatched method
        // without a ResolvedMethod comparison (FB9).
        // Why conclusion first: the first sentence is the conclusion; usability rounds
        // showed readers stop at sentence one, so do not restore the explanation-first order.
        // Format: file, resolved method display name.
        public const string HotReloadCompiledLineMapWarningFormat =
            "--line resolved against the last compiled source, not the edited file: '{0}' has "
            + "active hot-reload patches and the resolved method '{1}' is not patched by this "
            + "reload. Verify ResolvedLineText matches the statement you meant, or run "
            + "'uloop compile' and re-enable.";

        // Why a distinct sentence: comparison already proved the resolved statement is identical,
        // so asking the agent to Verify ResolvedLineText by hand is leftover work.
        // Why conclusion first: the first sentence is the conclusion; usability rounds
        // showed readers stop at sentence one, so do not restore the explanation-first order.
        // Format: file, resolved method display name.
        public const string HotReloadCompiledLineMapMatchedWarningFormat =
            "No drift is visible at this line: the statement text at the resolved line is "
            + "identical in the edited file. '{0}' has active hot-reload patches and the "
            + "resolved method '{1}' is not patched by this reload, so --line resolved against "
            + "the last compiled source, not the edited file.";

        // Format: file, resolved line, compiled line text, edited line text.
        public const string HotReloadCompiledLineMapLineDriftWarningFormat =
            "'{0}' line {1} is '{2}' in the last compiled source but '{3}' in the edited file. "
            + "The marker is armed on the compiled statement. If that is not the statement you meant, "
            + "recompute --line against the last compiled source, or run 'uloop compile' and re-enable.";

        // Format: file, resolved line, compiled line text.
        // Why a distinct sentence: quoting an empty edited line as '' looks like a missing field.
        public const string HotReloadCompiledLineMapBlankEditedLineDriftWarningFormat =
            "'{0}' line {1} is '{2}' in the last compiled source but blank in the edited file. "
            + "The marker is armed on the compiled statement. If that is not the statement you meant, "
            + "recompute --line against the last compiled source, or run 'uloop compile' and re-enable.";

        // Format: file, resolved line, resolved method, patched method.
        // Why no text comparison: the marker sits in an unpatched method's compiled line, so a
        // line number the edited file places inside a patched method proves the file drifted.
        public const string HotReloadCompiledLineMapPatchedSpanDriftWarningFormat =
            "'{0}' line {1} is inside '{2}' in the last compiled source, but in the edited file that line "
            + "number now falls inside '{3}', which is hot-reload patched. The edited file no longer "
            + "follows compiled line numbers here, so the marker is armed in '{2}', not where the edited "
            + "file shows line {1}. To pause inside '{3}', pass a line inside its edited body. To pause "
            + "inside '{2}', pass --method naming it together with the edited line.";

        // Format: file, requested line, requested edited text, resolved line, resolved method.
        public const string HotReloadCompiledLineSnapDisclosureFormat =
            "'{0}' --line {1} is '{2}' in the edited file, but the marker snapped forward to line {3} in '{4}'.";

        // Format: file, requested line, resolved line, resolved method.
        public const string HotReloadCompiledLineSnapDisclosureBlankRequestedLineFormat =
            "'{0}' --line {1} is blank in the edited file, but the marker snapped forward to line {2} in '{3}'.";

        // Format: file, requested line, resolved line, resolved method.
        // Why omit edited text: a failed read is not the same as a blank line.
        public const string HotReloadCompiledLineSnapDisclosureWithoutEditedTextFormat =
            "'{0}' --line {1} snapped forward to line {2} in '{3}'.";

        public const string HotReloadCompiledLineMapLineDriftNextAction =
            "Verify ResolvedLineText is the statement you intended. If it is not, run 'uloop compile' "
            + "and re-enable the pause point.";

        // Format: declaring type name, method name, requested line.
        public const string HotReloadPatchedLineOutsidePatchedBodyMessageFormat =
            "'{0}.{1}' is currently hot-reload patched and line {2} does not fall inside any "
            + "hot-reload patched method's current body, so the marker cannot be placed reliably. "
            + "Patched methods resolve against the edited file; methods this reload did not patch "
            + "resolve against the last compiled source. "
            + "Either the compiled line map for this file is stale, or the method's active patch "
            + "belongs to a superseded hot-reload generation.";

        public const string HotReloadPatchedLineOutsidePatchedBodyNextAction =
            "Pick a line inside the edited method body, run 'uloop hot-reload --revert-all' to "
            + "restore compiled bodies, or run 'uloop compile' to realign line numbers.";

        // Format: declaring type name, method name, requested line, resolved compiled line.
        public const string HotReloadPatchedLineMapsIntoPatchedBodyMessageFormat =
            "Line {2} of the edited file lies outside every hot-reload patched method body, but in "
            + "the last compiled source of this file it maps to line {3}, inside '{0}.{1}', which is "
            + "hot-reload patched, so the marker cannot be placed there reliably. Methods this "
            + "reload did not patch resolve against the last compiled line numbers, which the "
            + "edited file no longer follows, so a line that belongs to an unpatched method, or a "
            + "blank or comment line, can map into the patched one.";

        // Format: requested line.
        public const string HotReloadPatchedLineMapsIntoPatchedBodyNextAction =
            "If line {0} is inside a method this reload did not patch, pass --method <Type.Method> "
            + "naming that method together with --line {0}: the line is then matched by its text "
            + "inside that method's compiled span or on its declaration lines. If line {0} is blank "
            + "or a comment, pick a statement line instead. To pause inside the patched method "
            + "itself, pick a line inside its edited body. 'uloop compile' realigns line numbers but "
            + "ends the active patches; 'uloop hot-reload --revert-all' also ends them.";

        public const string HotReloadPatchedCompiledMethodSpanFormat =
            " In the last compiled source, '{0}.{1}' spans lines {2}-{3}.";

        // Format: resolved method display name, compiled start line, compiled end line.
        public const string HotReloadCompiledMethodSpanInLastCompiledSourceFormat =
            " In the last compiled source, '{0}' spans lines {1}-{2}.";

        // Why cap 3: a longer match list turns the enable warning into another line-number puzzle.
        public const int CompiledLineDriftCandidateMatchLimit = 3;

        // Format: 1-based compiled line number, optionally annotated with its containing compiled
        // method. Why "Candidate": this is a search hit, not a guarantee that re-enabling there
        // is the intended statement.
        public const string HotReloadCompiledLineDriftCandidateSingleFormat =
            " Candidate: the edited line's text appears at line {0} in the last compiled source.";

        // Format: comma-separated 1-based compiled line numbers, each optionally annotated with
        // its containing compiled method, with an optional truncation note.
        public const string HotReloadCompiledLineDriftCandidateMultipleFormat =
            " Candidate: the edited line's text appears at lines {0} in the last compiled source.";

        // Format: requested --line, then a 1-based compiled line number optionally annotated with
        // its containing compiled method.
        public const string HotReloadCompiledLineDriftRequestedLineCandidateSingleFormat =
            " Candidate: the text at --line {0} in the edited file appears at line {1} in the last compiled source.";

        // Format: requested --line, then comma-separated 1-based compiled line numbers each
        // optionally annotated with their containing compiled method, with an optional truncation note.
        public const string HotReloadCompiledLineDriftRequestedLineCandidateMultipleFormat =
            " Candidate: the text at --line {0} in the edited file appears at lines {1} in the last compiled source.";

        // Why format from CompiledLineDriftCandidateMatchLimit: a hard-coded "3" would lie
        // if the cap changed.
        public const string HotReloadCompiledLineDriftCandidateTruncatedMatchesSuffixFormat =
            " (first {0} matches)";

        // Format: compiled method display name. Kept separate from Candidate sentence formats so
        // their established wording stays unchanged while each matching line can name its method.
        public const string HotReloadCompiledLineDriftCandidateMethodAnnotationFormat =
            " (in '{0}')";

        public const string NearbyCompiledMethodsPrefix =
            " Nearby methods in the last compiled source: ";

        public const string NearbyCompiledMethodSpanFormat = "'{0}' spans lines {1}-{2}";

        // Format: patched method display name, requested line.
        // Why a dedicated string: a patched method with no shim PDB still falls through to the
        // compiled line map, so the generic "patched methods use the edited file" sentence would
        // be a lie on that path.
        public const string HotReloadPatchedMethodPdbUnavailableWarningFormat =
            "--line {1} falls inside hot-reload patched method '{0}', but this patch has no debug "
            + "symbols. Line numbers are therefore resolved against the last compiled source, not "
            + "the edited file. Run 'uloop compile' and re-enable.";

        public const string HotReloadPatchedMethodPdbUnavailableNextAction =
            "Run 'uloop compile' and re-enable the pause point.";

        // Format: resolved method display name, requested line, edited start line, edited end line.
        public const string HotReloadRetargetedToEditedFileWarningFormat =
            "--line {1} was resolved against the edited file because it falls inside hot-reload "
            + "patched method '{0}' (edited lines {2}-{3}). Methods not patched by hot reload "
            + "resolve against the last compiled source instead. If you meant a different method, "
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

        // Why a new ungated path: existing compiled-line drift warnings only fire when hot-reload
        // patches are active, but a closing-brace line is misleading even on compiled source.
        // Format: resolved line, resolved method display name.
        public const string ClosingBraceResolvedLineWarningFormat =
            "--line resolved to the method's closing brace at line {0}. Every return path through {1} reaches this line, including early returns, so captured variables can reflect a different path than the one you meant. To observe one specific path, target a statement line inside that path.";

        // Format: original --line, --method name, remapped compiled line.
        public const string EditedLineRemapWarningFormat =
            "--line {0} in method '{1}' was matched by its text to line {2} in the last compiled source, so the marker was placed at line {2}, not at line {0}. Verify ResolvedLocation, or run 'uloop compile' and re-enable to use edited-file line numbers.";

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

        // Why only a warning: without a verified snapshot there is nothing to map edited lines
        // against, and refusing would make pause points unusable in files such as package sources.
        // Format: file, requested line.
        public const string NoVerifiedSnapshotLineBasisWarningFormat =
            "No verified source snapshot exists for '{0}', so --line {1} was resolved against the last compiled source and ResolvedLine is a compiled line number. Run 'uloop compile' to refresh the snapshot.";
    }
}
