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

        // Where one domain's introduced-type artifact assemblies are written, one directory per
        // session and artifact below it. Shared with the publicizer, which accepts an image from
        // here as well as one from ScriptAssemblies.
        public const string IntroducedTypeArtifactsRelativeDirectory = "Library/UloopHotReload/IntroducedTypes";
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

        // Package-relative sources compiled into the worker in addition to the tilde directory.
        // Why: the resident-mode line protocol, the introduced-type fingerprint and the reason
        // code the worker reports are shared verbatim between the Editor host and the worker so
        // the two ends cannot drift apart.
        public static readonly string[] WorkerSharedSourcePackageRelativePaths =
        {
            "Editor/FirstPartyTools/HotReload/Shared/TransformWorkerServeProtocol.cs",
            "Editor/FirstPartyTools/HotReload/Shared/HotReloadIntroducedTypeFingerprint.cs",
            "Editor/FirstPartyTools/HotReload/Shared/HotReloadWorkerReasonCode.cs"
        };

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

        // Every hot-reload compile path reports an unresolvable external compiler with this one
        // sentence, so a user who hits it in a shim compile and in an introduced-type compile is
        // told the same thing about the same Unity installation.
        public const string CompilerPathsUnresolvedMessage =
            "External compiler paths could not be resolved for this Unity installation.";

        // Why not "applied": this sentence is the out-of-reload / unsupported-kind hint appended
        // to NewMember compile failures. Added members declared in any file of this reload's
        // assembly group are applied through that group's shim assembly.
        public const string NewMemberCompileHint =
            "Added members declared in the edited files of the same assembly are applied through "
            + "the shim assembly. Members referenced from other assemblies, or from files that are "
            + "neither passed to this reload nor already hot-reloaded, still require a real compile "
            + "(uloop compile).";

        public const string ActiveSiblingsRebindWarningFormat =
            "Also re-applied {0} unchanged file(s) with active patches in assembly '{1}' so their "
            + "patches bind to this reload's shim: {2}.";

        public const string ActiveSiblingChangedSinceApplyWarningFormat =
            "'{0}' has active patches but its source changed since they were applied, so it was "
            + "not re-applied; pass it to hot-reload to update it.";

        // Format: retried file count, assembly name, comma-separated paths. Why apart from the
        // re-applied summary: these files held no active patch, so "so their patches bind" would
        // misstate why they came back.
        public const string RetriedSiblingsWarningFormat =
            "Also retried {0} unchanged file(s) in assembly '{1}' that an earlier reload left Skipped "
            + "or Failed, and this reload applied them: {2}.";

        // Why the reader is told it will not come back: the retry is a single one, so a reload
        // that fixes the reason elsewhere has to pass this file again for it to apply.
        public const string RetriedSiblingNotAppliedWarningFormat =
            "'{0}' was retried because an earlier reload left it Skipped or Failed, but this reload "
            + "did not apply it either; see its rows for the reasons. It is not retried again, so "
            + "pass it to hot-reload once the reason is fixed.";

        public const string RetrySiblingChangedSinceSkipWarningFormat =
            "'{0}' was left Skipped or Failed by an earlier reload and its source changed since, so "
            + "it was not retried; pass it to hot-reload to apply it.";

        // Format: companion file count, assembly name, comma-separated paths.
        public const string CompanionSiblingsWarningFormat =
            "Also brought back {0} unchanged file(s) in assembly '{1}' that an earlier reload was "
            + "given beside its changes, so this reload binds the same way: {2}.";

        public const string CompanionSiblingChangedWarningFormat =
            "'{0}' was given to an earlier reload beside its changes, but its source changed since, "
            + "so it was not brought back; pass it to hot-reload too if an added member needs it to "
            + "bind.";

        // Why a second wording: the failed-rebind sentence sends the reader to the sibling's own
        // rows, and a reload that stopped before re-applying anything wrote none. Pointing at
        // rows that do not exist reads as a lost report rather than as a run that changed nothing.
        public const string ActiveSiblingRebindSkippedWarningFormat =
            "'{0}' was pulled in to re-bind its active patches, but this reload stopped before "
            + "re-applying them, so its active patches are unchanged. Fix the refused declaration "
            + "and rerun, or run uloop compile to clear the run.";

        // Why a third wording: a sibling whose every row was Skipped did not fail, and its
        // earlier patches stay live, so the failed sentence would overstate what happened.
        public const string ActiveSiblingRebindSkippedOnlyWarningFormat =
            "'{0}' was pulled in to re-bind its active patches, but every method there was Skipped "
            + "this time; see its rows for the reasons. Any earlier patches there stay active until "
            + "uloop compile clears the run.";

        // Why a fourth wording: when the whole reload is refused, every file of the group gets the
        // same file-level Failed row, so the sibling's rows only repeat the run's refusal reason
        // and none of its active patches changed. The failed sentence would send the reader
        // looking for patches that changed.
        public const string ActiveSiblingRebindRunRefusedWarningFormat =
            "'{0}' was pulled in to re-bind its active patches, but the whole reload was refused "
            + "before re-applying them, so its active patches are unchanged; its rows repeat the "
            + "refusal reason. Fix that and rerun, or run uloop compile to clear the run.";

        public const string ActiveSiblingRebindFailedWarningFormat =
            "'{0}' was pulled in to re-bind its active patches but this reload failed for it; "
            + "see its rows for which patches changed and run uloop compile to clear the run.";

        // Wire value for TransformWorkerEntryDto.patchKind when the worker emits a shim for a
        // method that exists only in the edited source. Keep in sync with PatchKinds.AddedMethod
        // in TransformWorker~/PatchKinds.cs.
        public const string PatchKindAddedMethod = "addedMethod";

        // --status Kind for rows sourced from a generation's added members (no compiled MethodBase).
        public const string AddedMemberStatusKind = "Added";

        // --status Kind for rows sourced from a generation's added fields (live added fields).
        public const string AddedFieldKind = "AddedField";

        // Isolation retry drops callers of a failed added shim so retry does not CS0103; they
        // are not Failed (the compile error was in the added body) and must not stay silent.
        public const string IsolatedAddedMethodCallerSkipReason =
            "Calls an added method whose shim failed to compile; the caller was left unpatched. "
            + "Fix the compile error in the added method (see the Failed row in this response) and reload again, or run 'uloop compile'.";

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

        // Why a separate sentence for the members this run skipped: the ordinary wording invites
        // another reload, and another reload of the same shape skips them again. What has to
        // change first is the shape their Methods[] reason names.
        public const string DeactivatedSkippedPatchesWarningFormat =
            "This run deactivated previously active patches by skipping them: {0}. They reverted to the "
            + "compiled behavior, and reloading the same shape skips them again; change what their "
            + "Methods[].Reason names and reload, or run 'uloop compile'.";

        // Why this is reported at all: a skip that leaves an earlier patch active produces no
        // Skipped-versus-compiled difference the reader can see. The method keeps running the
        // older reload's body, which matches neither the compiled assembly nor the source on
        // disk, and nothing else in the response says so.
        public const string SkippedMethodKeepsActivePatchWarningFormat =
            "This run skipped these methods, so what runs for them is still the body an earlier hot "
            + "reload applied, which matches neither the compiled assembly nor the source on disk: {0}. "
            + "Reloading the same shape skips them again; change what their Methods[].Reason names and "
            + "reload, or run 'uloop compile'.";

        public const string DeactivatedSkippedAddedMembersWarningFormat =
            "This run deactivated previously active added members by skipping them: {0}. They are no longer "
            + "registered, but patches this run left active may still reach their previous shim bodies. "
            + "Reloading the same shape skips them again; change what their Methods[].Reason names and "
            + "reload, or run 'uloop compile'.";

        // Wire value for TransformWorkerRemovedMemberDto.kind.
        // Keep in sync with RemovedMemberKinds in TransformWorker~/RemovedMemberKinds.cs.
        public const string RemovedMemberKindMethod = "method";

        public const string RemovedMemberKindField = "field";

        // Format: comma-separated removed member names.
        public const string RemovedMembersWarningFormat =
            "Removed members stay present in the compiled assembly until 'uloop compile'; "
            + "edited bodies no longer call them: {0}.";

        // Format: count of removed members, then the comma-separated names (ordinal).
        // Why a line of its own rather than the full warning again: the full text reappears on
        // every run until 'uloop compile', and a reader who already acted on it reads the reprint
        // as news, which buries the warnings the run produced for the first time.
        public const string ContinuingRemovedMembersWarningFormat =
            "Continuing from an earlier run: the same {0} removed member(s) are still in the "
            + "compiled assembly and still uncalled by the edited bodies ({1}); 'uloop compile' "
            + "is what removes them.";

        // Reason on a Stale row: the source no longer declares the method, but the patch is still
        // installed, so compiled callers keep running the patched body.
        public const string StalePatchRemovedFromSourceReason =
            "The method was removed from the edited source, but its patch stays active until "
            + "'uloop compile', '--revert-all', or a later reload whose source restores the method "
            + "to the compiled baseline; compiled callers still run the patched body.";

        public const string AddedFieldsLifetimeWarningFormat =
            "Added field values live outside the compiled assembly and last only until the next 'uloop compile' or domain reload: {0}.";

        // Why one line naming Type.field: two types can gain a field of the same name, and the
        // recipe file is named because the reader has no other way to find it.
        public const string SerializedAddedFieldWarningFormat =
            "Added field(s) with a serialization attribute will not appear in the Inspector or "
            + "serialize until 'uloop compile': {0}. To put a value in one now, follow "
            + "references/added-field-wiring.md in the uloop-hot-reload skill.";

        // Why a warning rather than a re-run of the initializer: a stored value cannot be told
        // apart from one the edited code assigned, so re-running would overwrite live state. The
        // run reports the mismatch instead, because nothing else in the response shows it.
        public const string AddedFieldInitializerChangedWarningFormat =
            "A previous reload already added these fields, so this run's initializer for them does "
            + "not reach a value that already exists: {0}. It runs only where the field has not been "
            + "read yet; assign the value inside a patched method (for a reference type, "
            + "'if (field == null) field = ...;'), rename the field, or run 'uloop compile'.";

        public const string MissingUsingCompileHint =
            "This can mean a missing using or global using (hot reload collects global usings from the edited file's assembly).";

        // Why a line of its own: a type this reload introduced lives in the shim assembly of
        // the group that declared it, so another assembly's shim cannot see it however the using
        // directives read. The missing-using and new-member hints both point the reader at the
        // edited file instead, which is not where the answer is.
        public const string IntroducedTypeOtherAssemblyCompileHint =
            "If the missing type was introduced by this reload into a different assembly, it is "
            + "visible only inside that assembly until 'uloop compile' makes it a compiled type.";

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

        // Format: file name, assembly name. Used instead of the warning below when the file
        // declares a type hot reload introduced: such a type is only in a byte-loaded artifact,
        // never in a compiled assembly, so no compile of this project could have produced the
        // baseline the other wording asks the reader to establish. Why the last sentence: the
        // file may also hold a compiled type, and that one is still patched in full.
        public const string IntroducedTypeSourceNoBaselineWarningFormat =
            "{0} declares a type hot reload introduced (assembly {1}), so it has no compiled "
            + "baseline until 'uloop compile'. This is expected: hot reload tracks the introduced "
            + "type from its own recorded declaration. Any other type in this file has no baseline "
            + "either and is patched in full.";

        // Format: file name, assembly name. Emitted per file when PDB-validated snapshot is absent.
        public const string NoVerifiedSourceSnapshotWarningFormat =
            "No verified source snapshot for {0} (assembly {1}); patching all methods. "
            + "Run uloop compile to establish a baseline for edited-method detection.";

        // Format: file name, assembly name. Used instead of the warning above when the compiled
        // PDB lists no document for the file: a file whose code compiles to no method body (an
        // enum, an interface, fields only) is never recorded there, so no compile can produce the
        // baseline the other wording asks the reader to establish.
        public const string NoCompiledMethodBodyBaselineWarningFormat =
            "{0} (assembly {1}) has no compiled method body, so there is no baseline for "
            + "edited-method detection; patching all methods. This is expected for files that only "
            + "declare types without bodies.";

        // Format: file count, comma-separated project-relative paths. The per-kind summaries of the
        // three warnings above for files a run only re-applied; see HotReloadSiblingBaselineNotices.
        public const string SiblingIntroducedTypeNoBaselineWarningFormat =
            "{0} re-applied sibling file(s) declare a type hot reload introduced, so they have no "
            + "compiled baseline until 'uloop compile': {1}. This is expected: hot reload tracks "
            + "each introduced type from its own recorded declaration.";

        public const string SiblingNoVerifiedSourceSnapshotWarningFormat =
            "{0} re-applied sibling file(s) have no verified source snapshot, so every method in them "
            + "is treated as edited: {1}. Run uloop compile to establish a baseline for edited-method detection.";

        public const string SiblingNoCompiledMethodBodyBaselineWarningFormat =
            "{0} re-applied sibling file(s) have no compiled method body, so there is no baseline "
            + "for edited-method detection and every method in them is treated as edited: {1}. This is expected "
            + "for files that only declare types without bodies.";

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

        // Format: count of patched Methods entries that carry a LifecycleNote.
        public const string LifecycleNotesAggregatedMessageFormat =
            "{0} patched method(s) have one-shot lifecycle notes; see Methods[].LifecycleNote.";

        // Format: count of added methods whose Unity message a hot-reload proxy delivers.
        public const string ForwardedUnityMessagesAggregatedMessageFormat =
            "{0} added Unity message(s) are delivered by a hot-reload proxy; see Methods[].LifecycleNote.";

        public const string AlreadyActiveReason =
            "Source is unchanged since the last applied hot reload; the existing patch stays active "
            + "and keeps its InvocationCount. Edit and reload again to apply new changes.";

        public const string ActiveIntroducedTypeStatusKind = "Active";

        // Format: how many introduced types a revert could not take away. Reverting undoes method
        // patches, and an assembly this domain loaded can only leave it with a Domain Reload.
        public const string ActiveIntroducedTypesRevertAllNoteFormat =
            " {0} introduced type(s) stay loaded until the next Domain Reload; a revert cannot "
            + "unload the assembly that carries them.";

        // Why the revert answer says this: the hold stays armed for the types the revert left
        // behind, so a caller told the revert succeeded would otherwise wait for a refresh that
        // this session will not perform.
        public const string ActiveIntroducedTypesRevertAllHoldNote =
            " Auto Refresh stays held for them; run 'uloop compile' to release it.";

        // Format: how many types this run introduced.
        public const string IntroducedTypesOnlyApplyMessageFormat =
            "Hot reload introduced {0} type(s); no method body needed patching.";

        // Format: how many declarations this run bound from an assembly it already retained.
        public const string AlreadyActiveIntroducedTypesOnlyApplyMessageFormat =
            "Hot reload bound {0} introduced type(s) this domain already holds; no method body "
            + "needed patching.";

        // Format: how many declarations were bound from a retained assembly, and how many method
        // bodies this run patched. Why not "of them": a body-only edit of a retained type patches
        // the callers in the same group too, so the count is not a subset of the type rows.
        public const string AlreadyActiveIntroducedTypesPatchedApplyMessageFormat =
            "Hot reload bound {0} introduced type(s) this domain already holds; {1} method "
            + "body(ies) were patched.";

        // Format: how many type rows the response carries. Appended to a message that already
        // reports what the methods did.
        public const string IntroducedTypesApplyMessageSuffixFormat = " IntroducedTypes={0}.";

        public const string IntroducedTypeFailureApplyMessage =
            "Hot reload refused one or more type declarations; no method body was applied in the "
            + "files that share an assembly with a refused declaration. See IntroducedTypes.";

        public const string IntroducedTypeAndMethodFailureApplyMessage =
            "Hot reload finished with one or more Failed outcomes; no method body was applied in the "
            + "files that share an assembly with a refused declaration. See Methods and IntroducedTypes.";

        public const string AlreadyActiveIntroducedTypeReason =
            "This declaration is bound from an assembly an earlier hot reload retained, so this "
            + "reload introduced nothing for it. It stays loaded until the next Domain Reload.";

        public const string AddedMemberNotInstrumentedReason =
            "Added-member calls are not instrumented, so InvocationCount is always 0 for this row.";

        public const string AlreadyActiveAddedMemberReason =
            "Source is unchanged since the last applied hot reload; the existing added member stays available. "
            + AddedMemberNotInstrumentedReason;

        // Why: patching does not re-run calls that already finished (e.g. one-time
        // initialization); InvocationCount 0 on --status is the only runtime signal,
        // so the row itself must explain what 0 means and what makes the patch take effect.
        // The row must also say how to trigger the next call, because for
        // initialization-only methods that is the non-obvious step.
        public const string ActivePatchNeverInvokedReason =
            "Not invoked since this patch was applied. Calls that already finished before the patch (for example one-time initialization) do not re-run automatically; the patched body takes effect the next time this method is called. If this method only runs during initialization, trigger that path again — re-create the object that runs it, or run 'uloop compile' and enter Play Mode again.";

        // Format: replacement display name for an Active row whose compiled signature was
        // replaced in a later edit. Supersedes ActivePatchNeverInvokedReason when both apply.
        public const string ActivePatchSupersededReasonFormat =
            "Superseded by a new declaration of {0}: the edited source now declares a different signature. This compiled signature stays patched so existing callers keep working; it is not the entry point for new calls.";

        // Format: count of Active rows whose InvocationCount is 0.
        public const string NeverInvokedActiveAggregatedMessageFormat =
            "{0} change(s) have not been invoked since their patch was applied; see Methods[].Reason.";

        public const string MultiWarningSingleCompileResolutionMessage =
            "A single 'uloop compile' clears all of them at once when you want them gone; none of them has to be cleared before you keep working.";

        // Format: continuing file count, then comma-separated project-relative paths (ordinal).
        public const string ContinuingLineShiftWarningFormat =
            "Continuing from earlier runs: {0} file(s) still differ in line count from the last compiled source ({1}). 'enable-pause-point --line' targeting caveats from the earlier warning still apply; pass --method together with --line to pin the target.";

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
            "Hot reload finished with no methods patched. See Warnings for Skipped reasons and Methods for AlreadyActive reasons.";

        // Why it names the requested files: a run that also re-applies a sibling file reports
        // Patched or Added rows for that sibling, and a summary counting them reads as if the
        // edits the caller asked for had been applied.
        public const string RequestedFilesAllSkippedMessage =
            "Nothing from the requested file(s) was applied: every method there was Skipped (see Warnings for the reasons). Run 'uloop compile' to apply these edits.";

        // Format: leftover patches peeled because source matched compiled IL again.
        public const string StalePatchesRevertedMessageFormat =
            "{0} stale patch(es) were reverted so those methods run the compiled IL again.";

        // Format: skipped method identity, then the reason it could not be patched.
        public const string SkippedMethodWarningFormat = "Skipped {0}: {1}";

        public const string SkippedMethodsCollapsedWarningFormat = "Skipped {0} methods: {1} ({2})";

        // How a collapsed warning ends when it names only the first few of the methods that share
        // one reason. Points at Methods, which carries a row for every skipped method.
        public const string SkippedMethodsRemainderFormat = ", +{0} more (see Methods)";

        public const string VibeLogWorkerHostStarted = "hot_reload_worker_started";
        public const string VibeLogWorkerHostRestarted = "hot_reload_worker_restarted";
        public const string VibeLogWorkerHostShutdown = "hot_reload_worker_shutdown";
        public const string VibeLogWorkerHostLifecycleClosed = "hot_reload_worker_lifecycle_closed";
        public const string VibeLogWorkerHostBrokenConversation = "hot_reload_worker_broken_conversation";
        public const string VibeLogWorkerHostTempCleanupFailed = "hot_reload_worker_temp_cleanup_failed";
        public const string VibeLogWorkerHostFallbackOneShot = "hot_reload_worker_fallback_one_shot";
        public const string VibeLogFileStart = "hot_reload_file_start";
        public const string VibeLogWorkerResult = "hot_reload_worker_result";
        public const string VibeLogShimCompileFailed = "hot_reload_shim_compile_failed";
        public const string VibeLogIsolationRetry = "hot_reload_isolation_retry";
        public const string VibeLogEmptyEntriesClear = "hot_reload_empty_entries_clear";
        public const string VibeLogRevertFailed = "hot_reload_revert_failed";
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

        // Format: resolved assembly name. Used when the name is one of Unity's predefined
        // assemblies, which exist only once a script has been compiled into them, so the
        // .asmdef wording above would send the reader looking for a file that is not there.
        public const string PredefinedAssemblyNotCompiledReasonFormat =
            "Resolved assembly '{0}' does not exist yet: no script has been compiled into it, so there is no assembly to patch. Hot reload can only introduce types into an assembly that already exists. Run 'uloop compile' once; later scripts in this assembly can then be hot-reloaded.";

        // Format: resolved assembly name, project-relative script path, project-relative
        // .asmdef path. Used when the script has no imported .asmdef but an ancestor directory
        // already has one on disk. Why the resolved name comes last: it is a predefined name
        // Unity falls back to while the .asmdef is unimported, and leading with it reads as a
        // contradiction with the .asmdef the reader has to import.
        public const string UnimportedAsmdefCompilationAssemblyNotFoundReasonFormat =
            "'{1}' sits under '{2}', which Unity has not imported yet, so no compilation assembly exists for it (Unity currently maps the file to the predefined assembly '{0}'). Run 'uloop compile' first; hot reload can target the file once the .asmdef is imported.";

        // Why "declarations or methods": a run can fail on a refused type declaration alone, and
        // Methods is then empty, so a next action naming only methods would send the reader to a
        // section with nothing in it.
        public const string PartialApplyRecommendedNextAction =
            "Partially applied. Fix the failed declarations or methods and rerun, run 'uloop compile' to apply every edit, or run 'uloop hot-reload --revert-all' to discard the applied patches.";

        public const string AtomicFileSkipReason =
            "Skipped: hot reload applies each file all-or-nothing, and another method in this file failed. Nothing from this file was applied; patches from earlier reloads are untouched. Fix the failed methods and rerun, or run 'uloop compile'.";

        // Format: count of Patched + Added entries already applied in this file before a
        // Harmony patch-engine failure. Used only when that count is at least 1.
        public const string PartialApplyAfterPatchEngineFailureWarningFormat =
            "A Harmony patch failed after {0} method(s) in this file were already applied by this run; the file is partially applied. Run 'uloop hot-reload --revert-all' and re-apply your edits, or run 'uloop compile'.";

        public const string FailedWithNoApplyRecommendedNextAction =
            "Fix the failed declarations or methods and rerun, or run 'uloop compile'.";

        // Why this replaces whatever next action the run chose: those all read "run 'uloop
        // compile'", and during play that is the one thing --compile-on-skip auto declined to do,
        // so the reader has to be told the choice was theirs and how to make it.
        public const string CompileFallbackHeldForPlayModeRecommendedNextAction =
            "No compile ran because the Editor is in Play Mode and a compile would stop the Play session; rerun with --compile-on-skip on to compile anyway.";

        // The same Stop step the compile tool recommends when it refuses to compile during play
        // (CompileErrorNextActionsConstants.PlayModeStopNextAction); restated here because a tool
        // may not reference another tool's assembly.
        public const string CompileFallbackRefusedDuringPlayRecommendedNextAction =
            "No compile ran because the Editor is in Play Mode and Unity's 'Script Changes While Playing' is set to 'Recompile After Finished Playing'. Run 'uloop control-play-mode --action Stop' to leave Play Mode, then rerun 'uloop compile'.";

        // Unity's EditorPrefs entry behind "Script Changes While Playing", and its value for
        // "Recompile After Finished Playing", under which the compile tool refuses to run during
        // play. Only that one value is named because it is the only one hot reload acts on.
        public const string ScriptCompilationDuringPlayEditorPrefsKey = "ScriptCompilationDuringPlay";

        public const int ScriptCompilationDuringPlayRecompileAfterFinishedPlaying = 1;

        public const string RequestedFilesAllSkippedRecommendedNextAction =
            "Run 'uloop compile' to apply the Skipped edits, or change them into the shapes hot reload can patch (see Warnings).";

        // Why one sentence in one place: the same rule has to reach the caller from the skill, the
        // docs, and every selection response, and two wordings of it read as two rules.
        public const string NewFilesNotAutoSelectedSentence =
            "Files that have never been compiled are not selected automatically; pass them (and any "
            + "other path) with --files.";

        public const string NoCompileSnapshotsMessage =
            "No compile snapshots exist yet. Run 'uloop compile' first or pass project-relative .cs "
            + "paths with --files. " + NewFilesNotAutoSelectedSentence;

        public const string NoChangedFilesMessage =
            "No .cs files changed since the last compile were found. " + NewFilesNotAutoSelectedSentence;

        public const string PassExplicitFilesNextAction =
            "Pass project-relative .cs paths with --files (required for new files that have not been "
            + "compiled yet).";

        // Appended to the selection message so a caller reading a short list knows what it leaves out.
        public const string DefaultSelectionNewFilesNote =
            " New files that have never been compiled are not selected automatically.";

        // Opens the selection message of an omitted --files run that found no changed file but
        // still selects the files of introduced types Play entry or revert-all dropped.
        public const string DefaultSelectionNoChangedFilesPrefix =
            "--files was omitted; no file changed since the last compile.";

        // Format: {0} = count, {1} = comma-separated project-relative paths of the owner files of
        // introduced types that the Play-entry domain reload discarded, or whose later additions
        // revert-all dropped. The wording names both, because the ledger does not say which.
        public const string DefaultSelectionReselectedDroppedFilesFormat =
            " {0} new file(s) declaring a type hot reload introduced were selected again, because "
            + "entering Play Mode or 'uloop hot-reload --revert-all' dropped what earlier reloads had "
            + "applied from them: {1}.";

        // Replaces DefaultSelectionNewFilesNote when discarded new files were selected, so the
        // note does not read as if those files were left out too.
        public const string DefaultSelectionOtherNewFilesNote =
            " Other new files that have never been compiled are not selected automatically.";

        // SessionState key for the change identities (patched methods, added members, introduced
        // types) discarded by the Play-entry domain reload.
        // SessionState survives that reload and is cleared when the Editor process exits.
        public const string PlayModeEntryDropSessionStateKey =
            "io.github.hatayama.uloop.hot-reload.playModeEntryDroppedIdentities";

        // SessionState key for the owner files of introduced types discarded by the Play-entry
        // domain reload, or left loaded by revert-all with their later additions dropped, one
        // "identity<TAB>project-relative path" line per type.
        public const string PlayModeEntryDropSourcesSessionStateKey =
            "io.github.hatayama.uloop.hot-reload.playModeEntryDroppedIntroducedSources";

        // SessionState key for the unchanged files earlier reloads were given beside what they
        // applied, one "project-relative path<TAB>hash" line per file, so a Play-entry domain
        // reload does not forget them.
        public const string CompanionSourcesSessionStateKey =
            "io.github.hatayama.uloop.hot-reload.companionSources";

        // Format: remaining discarded identity count. Used only when --status active count is 0.
        public const string PlayModeEntryDropStatusMessageFormat =
            "0 change(s) currently active. {0} change(s) were discarded by the domain reload when Play Mode was entered — hot-reloaded edits that were never compiled are not in effect. Re-apply 'uloop hot-reload', or edit the files and run 'uloop compile'.";

        public const int SiblingConstDriftScanFileLimit = 50;

        // Format: scan file limit, total changed sibling count. Emitted when the scan
        // truncates so the cap is never silent.
        public const string SiblingConstDriftScanLimitedWarningFormat =
            "sibling const-drift scan limited to first {0} changed files ({1} total)";

        // Format: remaining ledger count. Appended to every validation-failure Message
        // when ActiveChangeCount is greater than 0 so callers do not treat 0 as clean.
        public const string ValidationFailureActiveChangesSuffixFormat =
            " {0} hot-reload change(s) are still active.";

        public const string ValidationFailureInspectOrRevertNextAction =
            "Run 'uloop hot-reload --status' to inspect the active changes, or 'uloop hot-reload --revert-all' to drop them.";
    }
}
