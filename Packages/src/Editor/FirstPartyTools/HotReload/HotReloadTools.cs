using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Parameters for applying hot reload to edited source files, or reverting every active patch.
    /// </summary>
    public class HotReloadSchema : UnityCliLoopToolSchema
    {
        /// <summary>
        /// Project-relative source file paths to hot-reload. Omitted or empty apply values select sources changed since the last compile snapshot; a file that has never been compiled has no snapshot and is not selected, so pass it explicitly. --status rejects a nonempty value and --revert-all ignores it.
        /// </summary>
        public string[] Files { get; set; } = Array.Empty<string>();

        /// <summary>
        /// When true, removes every active patch and added member and ignores Files; introduced types stay loaded until the next domain reload.
        /// </summary>
        public bool RevertAll { get; set; }

        /// <summary>
        /// When true, lists the active changes (patched methods, added members, introduced types) without applying or reverting anything.
        /// </summary>
        public bool Status { get; set; }

        /// <summary>
        /// When the apply run leaves edits in the files passed unapplied (Skipped or Failed methods, Failed type declarations; rows of a sibling pulled in to re-apply its active patches do not count), whether the CLI runs a compile in the same command: auto (default) does so in Edit Mode only and never stops a Play session, on always unless Unity holds compiles until Play ends, off never. Ignored by --status and --revert-all.
        /// </summary>
        public HotReloadCompileOnSkip CompileOnSkip { get; set; } = HotReloadCompileOnSkip.auto;
    }

    /// <summary>
    /// One per-type outcome from a hot-reload apply run, or one active type on --status.
    /// </summary>
    public class HotReloadIntroducedTypeResult
    {
        /// <summary>
        /// "Introduced", "AlreadyActive" or "Failed" on an apply run; "Active" on --status.
        /// </summary>
        public string Kind { get; set; } = string.Empty;

        public string TypeName { get; set; } = string.Empty;

        /// <summary>The compiled assembly the declaration belongs to.</summary>
        public string AssemblyName { get; set; } = string.Empty;

        /// <summary>The file that declares the type; empty when the run cannot attribute it.</summary>
        public string FilePath { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// One per-method outcome from a hot-reload apply run.
    /// </summary>
    public class HotReloadMethodResult
    {
        public string Kind { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// How many times this patched method body has run since the current patch was applied.
        /// Populated on --status Active rows and AlreadyActive apply rows; 0 for other
        /// apply/revert outcomes. Added-member AlreadyActive rows are always 0 because
        /// added-member calls are not instrumented.
        /// </summary>
        public long InvocationCount { get; set; }

        /// <summary>
        /// Optional note when the patched method is (or is only reached from) a one-shot lifecycle
        /// method. Empty when not applicable; does not change Kind.
        /// </summary>
        public string LifecycleNote { get; set; } = string.Empty;

        /// <summary>
        /// True when the row belongs to a sibling file the run pulled in to re-apply changes from
        /// earlier reloads, not to a file this run was asked about. Set whatever the Kind.
        /// </summary>
        public bool ReappliedFromSibling { get; set; }
    }

    /// <summary>
    /// Response for the hot-reload tool: aggregated apply outcomes or revert-all status.
    /// </summary>
    public class HotReloadResponse : UnityCliLoopToolResponse
    {
        public IReadOnlyList<HotReloadMethodResult> Methods { get; set; } =
            Array.Empty<HotReloadMethodResult>();

        public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

        /// <summary>
        /// The type declarations of this run, reported apart from the methods because a type is
        /// not a patched body. On --status, the types this domain holds. Always written, empty
        /// when there is none, so a reader can tell "no introduced type" from a field this
        /// version of the response does not carry.
        /// </summary>
        public IReadOnlyList<HotReloadIntroducedTypeResult> IntroducedTypes { get; set; } =
            Array.Empty<HotReloadIntroducedTypeResult>();

        /// <summary>
        /// How many introduced types this domain holds, counted as types and not as the artifact
        /// assemblies that carry them. Always written, zero when there is none.
        /// </summary>
        public int ActiveIntroducedTypeTotal { get; set; }

        public int PatchedTotal { get; set; }

        public int ActivePatchTotal { get; set; }

        public int AddedFieldTotal { get; set; }

        public int UnchangedTotal { get; set; }

        public int ClearedCount { get; set; }

        public string[] AddedFields { get; set; } = Array.Empty<string>();

        public IReadOnlyList<string> AddedConsts { get; set; } = Array.Empty<string>();

        public string Message { get; set; } = string.Empty;

        public string ErrorCode { get; set; } = string.Empty;

        public string[] NextActions { get; set; } = Array.Empty<string>();

        public string RecommendedNextAction { get; set; } = string.Empty;

        /// <summary>
        /// Remaining patched-method, added-member, and introduced-type identities discarded by
        /// the last Play-entry domain reload that have not been recovered by apply
        /// (<c>Patched</c> / <c>Added</c> methods, <c>Introduced</c> / <c>AlreadyActive</c>
        /// types), revert-all, or a successful compile.
        /// </summary>
        public int DroppedByPlayModeEntryCount { get; set; }

        /// <summary>
        /// On --status, how many wired added-field values came back to the objects the last scene
        /// reload built in place of their hosts. Omitted when zero.
        /// </summary>
        public int RestoredWiredValueCount { get; set; }

        /// <summary>
        /// On --status, the wired added-field values the last scene reload could not restore.
        /// Omitted when empty.
        /// </summary>
        public IReadOnlyList<HotReloadUnrestoredWiredValue> UnrestoredWiredValues { get; set; } =
            Array.Empty<HotReloadUnrestoredWiredValue>();

        /// <summary>
        /// True while Auto Refresh is held because at least one hot-reload patch is active.
        /// </summary>
        public bool AutoRefreshHeld { get; set; }

        // Why omit empty: success and validation-only payloads must not grow a next-action
        // field that PausePoint-style responses leave blank on the wire.
        public bool ShouldSerializeRecommendedNextAction()
        {
            return !string.IsNullOrEmpty(RecommendedNextAction);
        }

        /// <summary>
        /// Whether the CLI should run a compile after this run: NotNeeded, Requested, HeldForPlayMode
        /// (edits stayed unapplied but the Editor is in Play Mode and --compile-on-skip is auto), or
        /// Disabled (--compile-on-skip off). Always written; --status, --revert-all and validation
        /// failures answer NotNeeded.
        /// </summary>
        public string CompileFallback { get; set; } =
            HotReloadCompileFallbackDecision.NotNeeded.ToString();

        public bool ShouldSerializeErrorCode()
        {
            return !string.IsNullOrEmpty(ErrorCode);
        }

        public bool ShouldSerializeNextActions()
        {
            return NextActions != null && NextActions.Length > 0;
        }

        public bool ShouldSerializeDroppedByPlayModeEntryCount()
        {
            return DroppedByPlayModeEntryCount > 0;
        }

        public bool ShouldSerializeRestoredWiredValueCount()
        {
            return RestoredWiredValueCount > 0;
        }

        public bool ShouldSerializeUnrestoredWiredValues()
        {
            return UnrestoredWiredValues != null && UnrestoredWiredValues.Count > 0;
        }
    }

    /// <summary>
    /// Exposes attribute-free hot reload as a Unity CLI Loop first-party tool.
    /// </summary>
    [UnityCliLoopTool]
    public class HotReloadTool : UnityCliLoopTool<HotReloadSchema, HotReloadResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_HOT_RELOAD;

        protected override async Task<HotReloadResponse> ExecuteAsync(
            HotReloadSchema parameters,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Debug.Assert(parameters != null, "parameters must not be null.");

            // Read once: every branch below has to run against the same services, and a
            // replacement that closes mid-run must not move the tail of this run to another domain.
            HotReloadServices services = HotReloadCompositionRoot.Services;

            if (parameters.Status)
            {
                if (parameters.RevertAll
                    || (parameters.Files != null && parameters.Files.Length > 0))
                {
                    return CreateValidationFailure(
                        services,
                        new HotReloadValidationFailure(
                            "--status cannot be combined with --files or --revert-all.",
                            HotReloadValidationErrorCodes.StatusConflict,
                            new[]
                            {
                                "Run 'uloop hot-reload --status' with no other flags to inspect active patches.",
                                "To apply or revert patches, drop --status and pass --files or --revert-all."
                            }));
                }

                return services.StatusExecutor.ExecuteStatus();
            }

            if (parameters.RevertAll)
            {
                return services.StatusExecutor.ExecuteRevertAll();
            }

            HotReloadValidationFailure validationFailure = ValidateApplyParameters(parameters);
            if (validationFailure != null)
            {
                return CreateValidationFailure(services, validationFailure);
            }

            // Why here and not only at the run entry: the tool normalizes script paths for its own
            // selection and response rows, and PackageInfo is main-thread only, which this path is.
            services.PackageRootCapture.CaptureCurrent();
            HotReloadDefaultFileSelection selection = HotReloadDefaultFileSelector.Resolve(
                parameters.Files,
                services.ChangeDetector.Detect,
                HotReloadDroppedIntroducedSourceFiles.ListExistingOnDisk(),
                path => HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(services.PackageRootCapture, path));
            if (selection.ValidationFailure != null)
            {
                return CreateValidationFailure(services, selection.ValidationFailure);
            }

            HotReloadOrchestratorResult result = await services.Orchestrator
                .RunAsync(selection.Files, contentPathOverride: null, ct)
                .ConfigureAwait(false);
            // Why switch back: SessionState for Play-entry drop recovery is a Unity Editor API.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            IReadOnlyList<string> rewireFields = HotReloadPlayModeEntryDropRecorder.NotifyApplyRecovered(
                result.Methods,
                result.IntroducedTypes,
                result.AddedFields);
            HotReloadCompanionSourceSessionStore.Save(services.Domain.CompanionSources);
            // Why taken here and handed to Build: taking empties the unreported list, so only the
            // apply a caller runs may take it, not every path that builds an apply response.
            IReadOnlyList<HotReloadWiredValueRestoreFailure> unrestoredWiredValues =
                services.WiredValuePersistence.TakeUnreportedFailures();

            // Play Mode state and the Play Mode compile setting are read here because the switch
            // above put this path on the main thread. isPlaying is read once so the response and
            // the compile fallback decide from the same state.
            bool isPlaying = EditorApplication.isPlaying;
            HotReloadResponse response = HotReloadApplyResponseBuilder.Build(
                services,
                result,
                selection.ScanLimitWarnings,
                rewireFields,
                unrestoredWiredValues,
                isPlaying,
                EditorApplication.isPaused);
            ApplyCompileFallbackDecision(
                response,
                result,
                parameters.CompileOnSkip,
                isPlaying,
                EditorPrefs.GetInt(HotReloadConstants.ScriptCompilationDuringPlayEditorPrefsKey, 0)
                    == HotReloadConstants.ScriptCompilationDuringPlayRecompileAfterFinishedPlaying,
                new HotReloadReappliedSiblingFiles(
                    result.ActivePatchSiblingPaths,
                    path => HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(
                        services.PackageRootCapture,
                        path)));
            if (!string.IsNullOrEmpty(selection.SelectionMessage))
            {
                response.Message = selection.SelectionMessage + " " + response.Message;
            }

            return response;
        }

        // Validates supplied files so omitted files can use the compile-snapshot default path.
        internal static HotReloadValidationFailure ValidateApplyParameters(HotReloadSchema parameters)
        {
            if (parameters.Files == null)
            {
                return null;
            }

            for (int index = 0; index < parameters.Files.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(parameters.Files[index]))
                {
                    return new HotReloadValidationFailure(
                        "Files must not contain null or empty paths.",
                        HotReloadValidationErrorCodes.InvalidFiles,
                        new[]
                        {
                            "Remove null or empty entries from --files.",
                            "Pass project-relative .cs paths with --files."
                        });
                }
            }

            return null;
        }

        // Why this reads the services itself: the apply path above passes the services it read
        // at its own entry, and this shim exists only for callers that hold a result but not the
        // run that produced it.
        internal static HotReloadResponse BuildApplyResponse(
            HotReloadOrchestratorResult result,
            IReadOnlyList<string> additionalWarnings = null)
        {
            return HotReloadApplyResponseBuilder.Build(
                HotReloadCompositionRoot.Services,
                result,
                additionalWarnings,
                Array.Empty<string>(),
                Array.Empty<HotReloadWiredValueRestoreFailure>(),
                isPlaying: false,
                isPaused: false);
        }

        // Records the compile-fallback decision on an apply response. isPlaying and
        // compileRefusedDuringPlay are passed in because the decision must not depend on Editor
        // state when tests build responses off the main thread.
        internal static void ApplyCompileFallbackDecision(
            HotReloadResponse response,
            HotReloadOrchestratorResult result,
            HotReloadCompileOnSkip option,
            bool isPlaying,
            bool compileRefusedDuringPlay,
            HotReloadReappliedSiblingFiles activePatchSiblingFiles)
        {
            HotReloadCompileFallbackDecision decision = HotReloadCompileFallbackDecider.Decide(
                option,
                HotReloadCompileFallbackDecider.HasUnappliedEdit(result, activePatchSiblingFiles),
                isPlaying,
                compileRefusedDuringPlay);
            response.CompileFallback = decision.ToString();
            // Why only these decisions touch the next action: the others either run the compile
            // (the CLI then replaces the next action itself) or leave the run's own advice correct.
            if (decision != HotReloadCompileFallbackDecision.HeldForPlayMode
                && decision != HotReloadCompileFallbackDecision.BlockedByPlayModeSetting)
            {
                return;
            }

            // Why the setting picks the sentence: suggesting --compile-on-skip on when the Editor
            // refuses compiles during play sends the reader into a compile that is refused.
            string reason = compileRefusedDuringPlay
                ? HotReloadConstants.CompileFallbackRefusedDuringPlayRecommendedNextAction
                : HotReloadConstants.CompileFallbackHeldForPlayModeRecommendedNextAction;

            // Why append and not replace: the run's own advice names what to do about the
            // partially applied or failed edits, and losing it would leave the caller with only
            // the reason no compile ran.
            string existingNextAction = response.RecommendedNextAction;
            response.RecommendedNextAction = string.IsNullOrEmpty(existingNextAction)
                ? reason
                : existingNextAction + " " + reason;
        }

        private static HotReloadResponse CreateValidationFailure(
            HotReloadServices services,
            HotReloadValidationFailure failure)
        {
            Debug.Assert(services != null, "services must not be null.");
            Debug.Assert(failure != null, "failure must not be null.");
            // Why two numbers from one read: the suffix warns that the refusal left something
            // live, and an introduced type is live even when nothing is patched. ActivePatchTotal
            // counts patched methods and added members, which is what callers read it against
            // PatchedTotal for; the runtime total adds the introduced types on top.
            HotReloadActiveChangeSnapshot snapshot = services.Domain.CountActiveChanges();
            int activePatchTotal = snapshot.PatchAndAddedMemberCount;
            int runtimeChangeTotal = snapshot.RuntimeChangeTotal;
            string message = failure.Message;
            string[] nextActions = failure.NextActions;
            if (runtimeChangeTotal > 0)
            {
                message += string.Format(
                    HotReloadConstants.ValidationFailureActiveChangesSuffixFormat,
                    runtimeChangeTotal);
                nextActions = AppendNextAction(
                    nextActions,
                    HotReloadConstants.ValidationFailureInspectOrRevertNextAction);
            }

            return new HotReloadResponse
            {
                Success = false,
                Message = message,
                ErrorCode = failure.ErrorCode,
                NextActions = nextActions,
                ActivePatchTotal = activePatchTotal
            };
        }

        private static string[] AppendNextAction(string[] nextActions, string extraAction)
        {
            Debug.Assert(nextActions != null, "nextActions must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(extraAction), "extraAction must not be empty.");
            string[] combined = new string[nextActions.Length + 1];
            Array.Copy(nextActions, combined, nextActions.Length);
            combined[nextActions.Length] = extraAction;
            return combined;
        }
    }
}
