using System;

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
        // "fmt2" = publicize-format generation. The cache key is assembly name + MVID only, so a
        // rule change (event backing fields stay non-public since fmt2) must move the directory —
        // otherwise a cache written under the old rule keeps poisoning shim compiles until the
        // assembly happens to recompile.
        public const string PublicizedRefsRelativeDirectory = "Library/UloopHotReload/PublicizedRefs/fmt2";

        // Worker binaries are keyed by SHA256 of every TransformWorker~/*.cs file name and content.
        public const string WorkerCacheRelativeDirectory = "Library/UloopHotReload/Worker";

        // EditMode e2e tests place edited source copies here so AssetDatabase is never provoked.
        public const string TestSourcesRelativeDirectory = "Library/UloopHotReload/TestSources";

        // Per-assembly source snapshots keyed by assembly name + Mvid; file names are SHA256 of
        // the project-relative source path (slash-normalized) so separators and MAX_PATH never
        // affect the on-disk layout. Adoption is decided at use time by PDB document checksum.
        public const string SourceSnapshotRelativeDirectory = "Library/UloopHotReload/SourceSnapshot";

        // Package-relative directory of the out-of-process transform worker sources (tilde dir = Unity-ignored).
        public const string WorkerSourcePackageRelativePath =
            "Editor/FirstPartyTools/HotReload/TransformWorker~";

        public const string WorkerDllFileName = "worker.dll";
        public const string WorkerRuntimeConfigFileName = "worker.runtimeconfig.json";
        public const string WorkerRoslynDirectorySidecarFileName = "roslyn-directory.txt";
        public const string WorkerResponseFileName = "worker.rsp";

        public const int WorkerProcessTimeoutMilliseconds = 120_000;

        public const string BurstCompileAttributeFullName = "Unity.Burst.BurstCompileAttribute";

        // Cecil AttributeType.Name values for call-site logical-owner resolution.
        public const string CompilerGeneratedAttributeTypeName = "CompilerGeneratedAttribute";
        public const string AsyncStateMachineAttributeTypeName = "AsyncStateMachineAttribute";
        public const string IteratorStateMachineAttributeTypeName = "IteratorStateMachineAttribute";

        // Why not "applied": this sentence is the cross-file / unsupported-kind hint appended
        // to NewMember compile failures. Same-file added methods are applied through the shim.
        public const string NewMemberCompileHint =
            "Same-file added methods are applied through the shim assembly. Cross-file references "
            + "and unsupported member kinds still require a real compile (uloop compile).";

        // Wire value for TransformWorkerEntryDto.patchKind when the worker emits a shim for a
        // method that exists only in the edited source. Keep in sync with PatchKinds.AddedMethod
        // in TransformWorker~/PatchKinds.cs.
        public const string PatchKindAddedMethod = "addedMethod";

        // --status Kind for rows sourced from HotReloadAddedMemberRegistry (no compiled MethodBase).
        public const string AddedMemberStatusKind = "Added";

        // Isolation retry drops callers of a failed added shim so retry does not CS0103; they
        // are not Failed (the compile error was in the added body) and must not stay silent.
        public const string IsolatedAddedMethodCallerSkipReason =
            "Calls an added method whose shim failed to compile; the caller was left unpatched. "
            + "Fix the compile error in the added method (see the Failed row in this response) and reload again, or run 'uloop compile'.";

        // Keep in sync with AddedMethodSkipReasons.UnavailableAddedCall in
        // TransformWorker~/AddedMethodSkipReasons.cs.
        public const string UnavailableAddedCallSkipReason =
            "Calls an added method that hot reload cannot emit. Run 'uloop compile'.";

        public const string SignatureChangedGateSkipReasonFormat =
            "The return type of '{0}' changed, but this hot reload does not patch every compiled call site of the old method. Applying it would leave those call sites on the old version. Run 'uloop compile'.";

        // Why drop the original trailing "Run 'uloop compile'.": the inserted sentence already
        // ends with that CTA, and keeping both would duplicate it.
        public const string SignatureChangedGateSkipReasonSameFileCallersFormat =
            "The return type of '{0}' changed, but this hot reload does not patch every compiled call site of the old method. Applying it would leave those call sites on the old version. Editing the bodies of {1} in this file and reloading again applies them together, or run 'uloop compile'.";

        public const string SignatureChangedGateSkipReasonAlreadyActiveFormat =
            "The return type of '{0}' changed against the last compile, and the replacement applied by an "
            + "earlier hot reload is still active; this run left it unchanged. Run 'uloop compile' to make it permanent.";

        public const string SignatureChangedGatedCallerSkipReason =
            "Calls a method whose signature change was not applied because unpatched compiled call sites remain; this caller was left unpatched too. Run 'uloop compile'.";

        public const string StaleSignatureCallersWarningFormat =
            "Compiled call sites of the removed signature '{0}' are not patched by this hot reload: {1}. They keep the previous behavior until 'uloop compile'.";

        public const string SignatureChangeCallersRepatchedNoticeFormat =
            "Signature change '{0}' applied because its compiled call sites were already hot-reload patched; this run re-applied them on the new signature: {1}.";

        public const string SignatureChangeCoverageLostFailureFormat =
            "Isolation excluded compiled callers of '{0}'; applying the rest would leave those callers on the old version. Run 'uloop compile'.";

        public const string DeactivatedPatchesWarningFormat =
            "This run deactivated previously active patches: {0}. They reverted to the compiled behavior; "
            + "edit and reload again to re-apply them, or run 'uloop compile'.";

        public const string DeactivatedAddedMembersWarningFormat =
            "This run deactivated previously active added members: {0}. They are no longer registered, but patches this run left active may still reach their previous shim bodies. Edit and reload again to re-apply them, or run 'uloop compile'.";

        // Wire value for TransformWorkerRemovedMemberDto.kind.
        // Keep in sync with RemovedMemberKinds in TransformWorker~/RemovedMemberKinds.cs.
        public const string RemovedMemberKindMethod = "method";

        public const string RemovedMemberKindField = "field";

        // Format: comma-separated removed member names.
        public const string RemovedMembersWarningFormat =
            "Removed members stay present in the compiled assembly until 'uloop compile'; "
            + "edited bodies no longer call them: {0}.";

        public const string AddedFieldsLifetimeWarningFormat =
            "Added field values live outside the compiled assembly and last only until the next 'uloop compile' or domain reload: {0}.";

        public const string MissingUsingCompileHint =
            "This can mean a missing using or global using (hot reload collects global usings from the edited file's assembly).";

        // Why a separate line after Compose: CS1061/CS0117/CS0103 name the missing member, but
        // not that this same run skipped it (generic add, etc.). Agents otherwise treat the
        // compile error as the root cause.
        public const string SkippedMemberCompileFailureNoteFormat =
            "'{0}' was skipped by this hot reload run, which is why this compile failed: {1}";

        // Wire value for TransformWorkerEntryDto.patchKind when the worker rewrote inaccessible
        // accesses into accessor delegates.
        public const string PatchKindDelegation = "delegation";

        // Name of the parameterless public static binder the worker emits into delegation shim
        // types. Wire contract with TransformWorker's EmitBindAccessorsMethod — the worker is a
        // standalone source file that cannot reference this constant, so keep both in sync.
        public const string ShimBindAccessorsMethodName = "__BindAccessors";

        /// <summary>
        /// Returns whether a ScriptAssemblies DLL is a project assembly that may be publicized.
        /// Engine / test-runner / system assemblies under ScriptAssemblies must stay untouched —
        /// some are not rewriteable managed images.
        /// </summary>
        public static bool IsPublicizableProjectAssemblyFileName(string fileNameWithoutExtension)
        {
            if (string.IsNullOrEmpty(fileNameWithoutExtension))
            {
                return false;
            }

            if (fileNameWithoutExtension.StartsWith("UnityEngine", StringComparison.Ordinal)
                || fileNameWithoutExtension.StartsWith("UnityEditor", StringComparison.Ordinal)
                || fileNameWithoutExtension.StartsWith("Unity.", StringComparison.Ordinal)
                || fileNameWithoutExtension.StartsWith("System.", StringComparison.Ordinal)
                || fileNameWithoutExtension == "System"
                || fileNameWithoutExtension == "mscorlib"
                || fileNameWithoutExtension == "netstandard"
                || fileNameWithoutExtension == "Mono.Security")
            {
                return false;
            }

            return true;
        }

        // Same heuristic threshold as pause point: small IL bodies may already be inlined by Mono,
        // in which case a Harmony detour on the original method will not reach existing call sites.
        public const int SmallMethodInliningRiskThresholdBytes = 32;

        public const string StaleAssemblyHint =
            "The loaded assembly no longer matches the compiled assembly on disk (a script compile "
            + "or domain reload may have happened). Hot reload is not needed — use uloop compile, "
            + "or wait for compilation to finish and retry.";

        public const string AssemblyNotLoadedHint =
            "The target assembly is not currently loaded in this AppDomain. Ensure the code path "
            + "that loads it has run, then retry.";

        // Format: file name, assembly name. Emitted per file when PDB-validated snapshot is absent.
        public const string NoVerifiedSourceSnapshotWarningFormat =
            "No verified source snapshot for {0} (assembly {1}); patching all methods. "
            + "Run uloop compile to establish a baseline for edited-method detection.";

        // Format: file name, assembly name. Emitted when syntax-method key collision disables baseline.
        public const string BaselineDisabledByDuplicateKeysWarningFormat =
            "Baseline comparison disabled for {0} (assembly {1}): the file contains methods with "
            + "colliding signature keys; patching all methods.";

        // Format: comma-separated "{id} (now line {N}: {text})" entries.
        public const string RetargetedPausePointsMessageFormat =
            "Armed pause points were re-targeted onto the hot-reload patched bodies: {0}";

        // Format: id, old line text, new line text.
        public const string RetargetLineDriftWarningFormat =
            "Pause point {0} now targets a different statement (was: \"{1}\", now: \"{2}\"). "
            + "Re-enable it at the intended line if this is not what you want.";

        // Format: comma-separated expired marker ids.
        public const string ExpiredPausePointsNotRetargetedMessageFormat =
            "Expired pause points were not re-targeted and will not fire: {0}";

        // Format: id, resolved line number, resolved line text.
        public const string RetargetedPausePointIdDetailFormat =
            "{0} (now line {1}: {2})";

        // Format: count of Methods entries that carry a LifecycleNote.
        public const string LifecycleNotesAggregatedMessageFormat =
            "{0} patched method(s) have one-shot lifecycle notes; see Methods[].LifecycleNote.";

        public const string AlreadyActiveReason =
            "Source is unchanged since the last applied hot reload; the existing patch stays active "
            + "and keeps its InvocationCount. Edit and reload again to apply new changes.";

        public const string AddedMemberNotInstrumentedReason =
            "Added-member calls are not instrumented, so InvocationCount is always 0 for this row.";

        public const string AlreadyActiveAddedMemberReason =
            "Source is unchanged since the last applied hot reload; the existing added member stays available. "
            + AddedMemberNotInstrumentedReason;

        public const string MultiWarningSingleCompileResolutionMessage =
            "A single 'uloop compile' clears all of them at once.";

        // Format: project-relative path of the source that matched a non-baseline ledger entry.
        public const string UnchangedSourceNonBaselineWarningFormat =
            "Source of '{0}' is unchanged since the last reload, but that run had Skipped or Failed "
            + "outcomes, so it is not a fully applied baseline. Hot reload processes all editable "
            + "methods again instead of reporting AlreadyActive, and unresolved Skipped reasons are "
            + "re-reported.";

        // Format: number of AlreadyActive method outcomes in this run.
        public const string AlreadyActiveApplyMessageFormat =
            "Hot reload found no source changes since the last applied reload. {0} patch(es) stay "
            + "active with their InvocationCount preserved. Edit and reload again to apply new changes.";

        public const string NoMethodsPatchedSeeSkippedOrAlreadyActiveMessage =
            "Hot reload finished with no methods patched. See Methods for Skipped and AlreadyActive reasons.";

        public const string VibeLogFileStart = "hot_reload_file_start";
        public const string VibeLogWorkerResult = "hot_reload_worker_result";
        public const string VibeLogShimCompileFailed = "hot_reload_shim_compile_failed";
        public const string VibeLogIsolationRetry = "hot_reload_isolation_retry";
        public const string VibeLogEmptyEntriesClear = "hot_reload_empty_entries_clear";
        public const string VibeLogApplySummary = "hot_reload_apply_summary";
        public const string VibeLogShimCompileStageFirstPass = "first_pass";
        public const string VibeLogShimCompileStageRetry = "retry";
        public const string VibeLogIsolationTriggerShimCompileFailure = "shim_compile_failure";
        public const string VibeLogIsolationTriggerSignatureChangeGate = "signature_change_gate";

        // Format: resolved assembly name. Unity maps not-yet-imported .asmdef scripts onto a
        // predefined assembly, so the name from GetAssemblyNameFromScriptPath often does not
        // exist in CompilationPipeline.GetAssemblies().
        public const string CompilationAssemblyNotFoundReasonFormat =
            "Resolved assembly '{0}' was not found in the compilation pipeline. Unity resolves files under a not-yet-imported .asmdef to a predefined assembly, so a brand-new .asmdef or a brand-new script cannot be hot-reloaded. Run 'uloop compile' first.";

        // Format: resolved assembly name, project-relative script path. Used when the script
        // has no imported .asmdef but an ancestor directory already has one on disk.
        public const string UnimportedAsmdefCompilationAssemblyNotFoundReasonFormat =
            "Resolved assembly '{0}' was not found in the compilation pipeline. '{1}' sits under a .asmdef that Unity has not imported yet, so hot reload cannot target it. Run 'uloop compile' first.";

        // Format: project-relative script path, compiled assembly name.
        public const string SourceFileNotInCompiledAssemblyReasonFormat =
            "'{0}' is not part of the last compiled assembly '{1}' (a newly added script). New files require a real compile; run 'uloop compile' first.";

        public const string PartialApplyRecommendedNextAction =
            "Partially applied. Fix the failed methods and rerun, run 'uloop compile' to apply every edit, or run 'uloop hot-reload --revert-all' to discard the applied patches.";

        public const string FailedWithNoApplyRecommendedNextAction =
            "Fix the failed methods and rerun, or run 'uloop compile'.";
    }
}
