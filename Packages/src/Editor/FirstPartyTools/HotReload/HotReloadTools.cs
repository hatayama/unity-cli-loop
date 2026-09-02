using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
        /// Project-relative source file paths to hot-reload. Omitted or empty apply values select sources changed since the last compile snapshot; --status rejects a nonempty value and --revert-all ignores it.
        /// </summary>
        public string[] Files { get; set; } = Array.Empty<string>();

        /// <summary>
        /// When true, removes every active hot-reload transplant and ignores Files.
        /// </summary>
        public bool RevertAll { get; set; }

        /// <summary>
        /// When true, lists the currently patched methods without applying or reverting anything.
        /// </summary>
        public bool Status { get; set; }
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
    }

    /// <summary>
    /// Response for the hot-reload tool: aggregated apply outcomes or revert-all status.
    /// </summary>
    public class HotReloadResponse : UnityCliLoopToolResponse
    {
        public IReadOnlyList<HotReloadMethodResult> Methods { get; set; } =
            Array.Empty<HotReloadMethodResult>();

        public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

        public int PatchedTotal { get; set; }

        public int ActivePatchTotal { get; set; }

        public int AddedFieldTotal { get; set; }

        public int UnchangedTotal { get; set; }

        public int ClearedCount { get; set; }

        public string[] AddedFields { get; set; } = Array.Empty<string>();

        public string Message { get; set; } = string.Empty;

        public string ErrorCode { get; set; } = string.Empty;

        public string[] NextActions { get; set; } = Array.Empty<string>();

        public string RecommendedNextAction { get; set; } = string.Empty;

        /// <summary>
        /// Remaining method identities discarded by the last Play-entry domain reload
        /// that have not been recovered by apply, revert-all, or a successful compile.
        /// </summary>
        public int DroppedByPlayModeEntryCount { get; set; }

        // Why omit empty: success and validation-only payloads must not grow a next-action
        // field that PausePoint-style responses leave blank on the wire.
        public bool ShouldSerializeRecommendedNextAction()
        {
            return !string.IsNullOrEmpty(RecommendedNextAction);
        }

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
    }

    /// <summary>
    /// Exposes attribute-free hot reload as a Unity CLI Loop first-party tool.
    /// </summary>
    [UnityCliLoopTool]
    public class HotReloadTool : UnityCliLoopTool<HotReloadSchema, HotReloadResponse>
    {
        // Why internal seams: compile-pipeline enumeration and apply execution cannot use planted
        // fixtures, so tests substitute them while production keeps these default implementations.
        internal static Func<HotReloadChangedFileAggregationResult> DetectChangedFilesForTesting =
            HotReloadChangedFileAggregator.Detect;

        internal static Func<IReadOnlyList<string>, CancellationToken, Task<HotReloadOrchestratorResult>>
            RunApplyAsyncForTesting = RunApplyAsync;

        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_HOT_RELOAD;

        protected override async Task<HotReloadResponse> ExecuteAsync(
            HotReloadSchema parameters,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Debug.Assert(parameters != null, "parameters must not be null.");

            if (parameters.Status)
            {
                if (parameters.RevertAll
                    || (parameters.Files != null && parameters.Files.Length > 0))
                {
                    return CreateValidationFailure(
                        new HotReloadValidationFailure(
                            "--status cannot be combined with --files or --revert-all.",
                            HotReloadValidationErrorCodes.StatusConflict,
                            new[]
                            {
                                "Run 'uloop hot-reload --status' with no other flags to inspect active patches.",
                                "To apply or revert patches, drop --status and pass --files or --revert-all."
                            }));
                }

                return HotReloadStatusExecutor.ExecuteStatus();
            }

            if (parameters.RevertAll)
            {
                return HotReloadStatusExecutor.ExecuteRevertAll();
            }

            HotReloadValidationFailure validationFailure = ValidateApplyParameters(parameters);
            if (validationFailure != null)
            {
                return CreateValidationFailure(validationFailure);
            }

            HotReloadDefaultFileSelection selection = HotReloadDefaultFileSelector.Resolve(
                parameters.Files,
                DetectChangedFilesForTesting);
            if (selection.ValidationFailure != null)
            {
                return CreateValidationFailure(selection.ValidationFailure);
            }

            HotReloadOrchestratorResult result = await RunApplyAsyncForTesting(selection.Files, ct)
                .ConfigureAwait(false);
            // Why switch back: SessionState for Play-entry drop recovery is a Unity Editor API.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            HotReloadPlayModeEntryDropRecorder.NotifyApplyRecovered(result.Methods);

            HotReloadResponse response = BuildApplyResponse(result, selection.ScanLimitWarnings);
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

        internal static HotReloadResponse BuildApplyResponse(
            HotReloadOrchestratorResult result,
            IReadOnlyList<string> additionalWarnings = null)
        {
            return HotReloadApplyResponseBuilder.Build(result, additionalWarnings);
        }

        private static Task<HotReloadOrchestratorResult> RunApplyAsync(
            IReadOnlyList<string> files,
            CancellationToken ct)
        {
            return HotReloadOrchestrator.RunAsync(files, contentPathOverride: null, ct);
        }

        private static HotReloadResponse CreateValidationFailure(HotReloadValidationFailure failure)
        {
            Debug.Assert(failure != null, "failure must not be null.");
            return new HotReloadResponse
            {
                Success = false,
                Message = failure.Message,
                ErrorCode = failure.ErrorCode,
                NextActions = failure.NextActions
            };
        }
    }
}
