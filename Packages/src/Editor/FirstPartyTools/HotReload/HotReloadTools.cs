using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

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

        public int ClearedCount { get; set; }

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
            int clearedCount = HotReloadPatcher.ActivePatchCount;
            HotReloadPatcher.RevertAll();
            return new HotReloadResponse
            {
                Success = true,
                ClearedCount = clearedCount,
                ActivePatchTotal = HotReloadPatcher.ActivePatchCount,
                Message = clearedCount == 0
                    ? "No active hot-reload patches to revert."
                    : "Reverted all active hot-reload patches."
            };
        }

        // Returns an error message when apply-mode arguments are invalid, or null when valid.
        internal static string ValidateApplyParameters(HotReloadSchema parameters)
        {
            if (parameters.Files == null || parameters.Files.Length == 0)
            {
                return "Files is required when --revert-all is not set.";
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
                        FilePath = outcome.FilePath ?? string.Empty
                    });
            }

            List<string> warnings = new List<string>(result.Warnings);
            if (result.PatchedTotal > 0)
            {
                // Always surface the pause-point interaction when any patch was applied.
                warnings.Add(HotReloadConstants.PausePointInteractionWarning);
            }

            return new HotReloadResponse
            {
                Success = !hasFailure,
                Methods = methods,
                Warnings = warnings,
                PatchedTotal = result.PatchedTotal,
                ActivePatchTotal = result.ActivePatchTotal,
                Message = BuildApplyMessage(result, hasFailure)
            };
        }

        private static string BuildApplyMessage(HotReloadOrchestratorResult result, bool hasFailure)
        {
            if (hasFailure)
            {
                return "Hot reload finished with one or more Failed method outcomes. See Methods.";
            }

            if (result.PatchedTotal == 0)
            {
                return "Hot reload finished with no methods patched. See Methods for Skipped reasons.";
            }

            return "Hot reload applied. PatchedTotal=" + result.PatchedTotal
                + ", ActivePatchTotal=" + result.ActivePatchTotal + ".";
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
