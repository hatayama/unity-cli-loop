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
        /// Project-relative source file paths to hot-reload. Required when RevertAll is false.
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
        /// apply/revert outcomes.
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

        public int UnchangedTotal { get; set; }

        public int ClearedCount { get; set; }

        public string[] AddedFields { get; set; } = Array.Empty<string>();

        public string Message { get; set; } = string.Empty;
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

            if (parameters.Status)
            {
                if (parameters.RevertAll
                    || (parameters.Files != null && parameters.Files.Length > 0))
                {
                    return CreateValidationFailure(
                        "--status cannot be combined with --files or --revert-all.");
                }

                return ExecuteStatus();
            }

            if (parameters.RevertAll)
            {
                return ExecuteRevertAll();
            }

            string validationError = ValidateApplyParameters(parameters);
            if (validationError != null)
            {
                return CreateValidationFailure(validationError);
            }

            HotReloadOrchestratorResult result = await HotReloadOrchestrator
                .RunAsync(parameters.Files, contentPathOverride: null, ct)
                .ConfigureAwait(false);

            return BuildApplyResponse(result);
        }

        private static HotReloadResponse ExecuteRevertAll()
        {
            int clearedCount = HotReloadPatcher.ActiveChangeCount;
            HotReloadPatcher.RevertAll();
            return new HotReloadResponse
            {
                Success = true,
                ClearedCount = clearedCount,
                ActivePatchTotal = HotReloadPatcher.ActiveChangeCount,
                Message = clearedCount == 0
                    ? "No active hot-reload changes to revert."
                    : "Reverted all active hot-reload changes."
            };
        }

        private static HotReloadResponse ExecuteStatus()
        {
            IReadOnlyList<HotReloadActivePatchInfo> active = HotReloadPatcher.DescribeActivePatches();
            IReadOnlyList<HotReloadAddedMemberInfo> addedMembers = HotReloadAddedMemberRegistry.Describe();
            List<HotReloadMethodResult> methods =
                new List<HotReloadMethodResult>(active.Count + addedMembers.Count);
            for (int index = 0; index < active.Count; index++)
            {
                HotReloadActivePatchInfo patch = active[index];
                methods.Add(
                    new HotReloadMethodResult
                    {
                        Kind = "Active",
                        Method = patch.MethodKey,
                        FilePath = patch.FilePath,
                        InvocationCount = HotReloadInvocationRegistry.GetCount(patch.MethodKey)
                    });
            }

            for (int index = 0; index < addedMembers.Count; index++)
            {
                HotReloadAddedMemberInfo added = addedMembers[index];
                methods.Add(
                    new HotReloadMethodResult
                    {
                        Kind = HotReloadConstants.AddedMemberStatusKind,
                        Method = added.MethodKey,
                        FilePath = added.FilePath
                    });
            }

            int count = methods.Count;
            return new HotReloadResponse
            {
                Success = true,
                Methods = methods,
                ActivePatchTotal = count,
                Message = $"{count} change(s) currently active."
            };
        }

        // Returns an error message when apply-mode arguments are invalid, or null when valid.
        internal static string ValidateApplyParameters(HotReloadSchema parameters)
        {
            if (parameters.Files == null || parameters.Files.Length == 0)
            {
                return "Files is required unless --revert-all or --status is set.";
            }

            for (int index = 0; index < parameters.Files.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(parameters.Files[index]))
                {
                    return "Files must not contain null or empty paths.";
                }
            }

            return null;
        }

        internal static HotReloadResponse BuildApplyResponse(HotReloadOrchestratorResult result)
        {
            Debug.Assert(result != null, "result must not be null.");

            List<HotReloadMethodResult> methods = new List<HotReloadMethodResult>(result.Methods.Count);
            bool hasFailure = false;
            for (int index = 0; index < result.Methods.Count; index++)
            {
                HotReloadMethodOutcome outcome = result.Methods[index];
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed)
                {
                    hasFailure = true;
                }

                methods.Add(
                    new HotReloadMethodResult
                    {
                        Kind = outcome.Kind.ToString(),
                        Method = outcome.Method,
                        Reason = outcome.Reason ?? string.Empty,
                        FilePath = outcome.FilePath ?? string.Empty,
                        InvocationCount = outcome.Kind == HotReloadMethodOutcomeKind.AlreadyActive
                            ? HotReloadInvocationRegistry.GetCount(outcome.Method)
                            : 0L,
                        LifecycleNote = outcome.LifecycleNote ?? string.Empty
                    });
            }

            List<string> warnings = new List<string>(result.Warnings);
            AppendRetargetLineDriftWarnings(warnings);
            AppendExpiredNotRetargetedWarnings(warnings);

            if (result.RetargetedPausePointIds != null && result.RetargetedPausePointIds.Count > 0)
            {
                List<string> details = new List<string>(result.RetargetedPausePointIds.Count);
                for (int index = 0; index < result.RetargetedPausePointIds.Count; index++)
                {
                    details.Add(FormatRetargetedPausePointIdDetail(result.RetargetedPausePointIds[index]));
                }

                warnings.Add(
                    string.Format(
                        HotReloadConstants.RetargetedPausePointsMessageFormat,
                        string.Join(", ", details)));
            }

            if (result.SuppressedPausePointIds != null && result.SuppressedPausePointIds.Count > 0)
            {
                string ids = string.Join(", ", result.SuppressedPausePointIds);
                warnings.Add(
                    "Armed pause points could not be re-targeted and will not fire until the patch "
                    + $"is reverted or compiled for real: {ids}");
            }

            return new HotReloadResponse
            {
                Success = !hasFailure,
                Methods = methods,
                Warnings = warnings,
                PatchedTotal = result.PatchedTotal,
                ActivePatchTotal = result.ActivePatchTotal,
                UnchangedTotal = result.UnchangedTotal,
                AddedFields = result.AddedFields ?? Array.Empty<string>(),
                Message = BuildApplyMessage(result, hasFailure, warnings.Count)
            };
        }

        // What: drains retarget line-drift triples recorded by SourcePausePointPatcher into Warnings.
        private static void AppendRetargetLineDriftWarnings(List<string> warnings)
        {
            IReadOnlyList<(string Id, string OldText, string NewText)> driftWarnings =
                HotReloadPausePointCoordination.ConsumeRetargetLineDriftWarnings?.Invoke();
            if (driftWarnings == null || driftWarnings.Count == 0)
            {
                return;
            }

            for (int index = 0; index < driftWarnings.Count; index++)
            {
                (string id, string oldText, string newText) = driftWarnings[index];
                warnings.Add(
                    string.Format(
                        HotReloadConstants.RetargetLineDriftWarningFormat,
                        id,
                        oldText,
                        newText));
            }
        }

        // What: drains expired-not-retargeted ids recorded during the latest patch transition.
        private static void AppendExpiredNotRetargetedWarnings(List<string> warnings)
        {
            IReadOnlyList<string> expiredIds =
                HotReloadPausePointCoordination.ConsumeExpiredNotRetargetedMarkerIds?.Invoke();
            if (expiredIds == null || expiredIds.Count == 0)
            {
                return;
            }

            warnings.Add(
                string.Format(
                    HotReloadConstants.ExpiredPausePointsNotRetargetedMessageFormat,
                    string.Join(", ", expiredIds)));
        }

        // What: "{id} (now line {N}: {text})" from the registry values written on retarget/enable.
        private static string FormatRetargetedPausePointIdDetail(string id)
        {
            UloopPausePointSnapshot status = UloopPausePointRegistry.GetStatus(id);
            string lineText = status.ResolvedLineText ?? string.Empty;
            return string.Format(
                HotReloadConstants.RetargetedPausePointIdDetailFormat,
                id,
                status.ResolvedLine,
                lineText);
        }

        private static string BuildApplyMessage(
            HotReloadOrchestratorResult result,
            bool hasFailure,
            int warningCount)
        {
            // Why: when every method was left untouched, the empty Methods list is intentional —
            // report the unchanged count instead of the generic "no patchable bodies" message.
            if (!hasFailure && result.Methods.Count == 0 && result.UnchangedTotal > 0)
            {
                return AppendWarningCount(
                    "All " + result.UnchangedTotal
                    + " methods are unchanged since the last compile; nothing to patch.",
                    warningCount);
            }

            string message;
            int addedCount = CountAddedOutcomes(result);
            bool isAppliedBranch = false;
            if (hasFailure)
            {
                message = "Hot reload finished with one or more Failed method outcomes. See Methods.";
            }
            else if (result.Methods.Count == 0)
            {
                message = "Hot reload found no patchable method bodies in the given files; nothing was changed. "
                    + "Hot reload only replaces existing ordinary method bodies; use uloop compile for other edits.";
            }
            else if (AreAllOutcomesAlreadyActive(result))
            {
                message = string.Format(
                    HotReloadConstants.AlreadyActiveApplyMessageFormat,
                    result.Methods.Count);
            }
            else if (result.PatchedTotal == 0 && addedCount == 0)
            {
                message = HotReloadConstants.NoMethodsPatchedSeeSkippedOrAlreadyActiveMessage;
            }
            else
            {
                isAppliedBranch = true;
                message = "Hot reload applied. PatchedTotal=" + result.PatchedTotal
                    + ", ActivePatchTotal=" + result.ActivePatchTotal + ".";
                if (addedCount > 0)
                {
                    message += " Added: " + addedCount + ".";
                }
            }

            if (result.UnchangedTotal > 0)
            {
                message += " " + result.UnchangedTotal + " unchanged methods were left untouched.";
            }

            int lifecycleNoteCount = 0;
            for (int index = 0; index < result.Methods.Count; index++)
            {
                if (!string.IsNullOrEmpty(result.Methods[index].LifecycleNote))
                {
                    lifecycleNoteCount++;
                }
            }

            if (lifecycleNoteCount > 0)
            {
                // Why aggregate: per-method text already lives on Methods[].LifecycleNote;
                // dumping every note into Message repeats nearly identical paragraphs.
                message += " " + string.Format(
                    HotReloadConstants.LifecycleNotesAggregatedMessageFormat,
                    lifecycleNoteCount);
            }

            if (isAppliedBranch && HasSkippedOutcome(result))
            {
                message += " See Methods for Skipped reasons.";
            }

            return AppendWarningCount(message, warningCount);
        }

        private static bool AreAllOutcomesAlreadyActive(HotReloadOrchestratorResult result)
        {
            if (result.Methods.Count == 0)
            {
                return false;
            }

            for (int index = 0; index < result.Methods.Count; index++)
            {
                if (result.Methods[index].Kind != HotReloadMethodOutcomeKind.AlreadyActive)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasSkippedOutcome(HotReloadOrchestratorResult result)
        {
            for (int index = 0; index < result.Methods.Count; index++)
            {
                if (result.Methods[index].Kind == HotReloadMethodOutcomeKind.Skipped)
                {
                    return true;
                }
            }

            return false;
        }

        private static string AppendWarningCount(string message, int warningCount)
        {
            if (warningCount <= 0)
            {
                return message;
            }

            return message + " " + warningCount + " warning(s). See Warnings.";
        }

        private static int CountAddedOutcomes(HotReloadOrchestratorResult result)
        {
            int addedCount = 0;
            for (int index = 0; index < result.Methods.Count; index++)
            {
                if (result.Methods[index].Kind == HotReloadMethodOutcomeKind.Added)
                {
                    addedCount++;
                }
            }

            return addedCount;
        }

        private static HotReloadResponse CreateValidationFailure(string message)
        {
            return new HotReloadResponse
            {
                Success = false,
                Message = message
            };
        }
    }
}
