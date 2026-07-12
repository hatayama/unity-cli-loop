using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Parameters for enabling one pause point, either by a hand-written marker id or by
    /// resolving a source file:line to a patch location. Exactly one of "Id" or "File"+"Line"
    /// must be provided.
    /// </summary>
    public class EnablePausePointSchema : UnityCliLoopToolSchema
    {
        public string Id { get; set; } = string.Empty;

        public string File { get; set; } = string.Empty;

        public int Line { get; set; }

        public int TimeoutSeconds { get; set; } = UloopPausePointRegistry.DefaultTimeoutSeconds;

        public string Mode { get; set; } = UloopPausePointCaptureMode.SingleShot;

        public int MaxHistory { get; set; } = UloopPausePointRegistry.DefaultMaxHistory;
    }

    /// <summary>
    /// Parameters for clearing one or all pause point markers.
    /// </summary>
    public class ClearPausePointSchema : UnityCliLoopToolSchema
    {
        public string Id { get; set; } = string.Empty;

        public bool All { get; set; }
    }

    /// <summary>
    /// Response shared by pause point tool commands.
    /// </summary>
    public class PausePointResponse : UnityCliLoopToolResponse
    {
        // Defaults to true so existing snapshot/clear paths keep their prior semantics.
        // Only explicit validation failures set this to false.
        public bool Success { get; set; } = true;
        public string Id { get; set; } = string.Empty;
        public int ResolvedLine { get; set; }
        public string ResolvedMethod { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public bool IsHit { get; set; }
        public int HitCount { get; set; }
        public int TimeoutSeconds { get; set; }
        public string Mode { get; set; } = string.Empty;
        public int MaxHistory { get; set; }
        public IReadOnlyList<PausePointCapturedHistoryFrame> CapturedVariableHistory { get; set; } =
            Array.Empty<PausePointCapturedHistoryFrame>();
        public int HistoryDroppedCount { get; set; }
        public bool Expired { get; set; }
        public string EnabledAtUtc { get; set; } = string.Empty;
        public long ElapsedSinceEnabledMilliseconds { get; set; }
        public long RemainingMilliseconds { get; set; }
        public int Generation { get; set; }
        public PausePointEditorState EditorState { get; set; } = new();
        public string FirstHitAtUtc { get; set; } = string.Empty;
        public string LastHitAtUtc { get; set; } = string.Empty;
        public int FirstHitSequence { get; set; }
        public int LastHitSequence { get; set; }
        public int ClearedCount { get; set; }
        public string Message { get; set; } = string.Empty;
        public string RecommendedNextAction { get; set; } = string.Empty;
        public string Warning { get; set; } = string.Empty;
        public string ClearedReason { get; set; } = string.Empty;
        public string StatusBeforeClear { get; set; } = string.Empty;
        public bool LateHitDiscardedAfterClear { get; set; }

        internal static PausePointResponse FromSnapshot(UloopPausePointSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new PausePointResponse
            {
                Id = snapshot.Id,
                Status = snapshot.Status,
                IsEnabled = snapshot.IsEnabled,
                IsHit = snapshot.IsHit,
                HitCount = snapshot.HitCount,
                TimeoutSeconds = snapshot.TimeoutSeconds,
                Mode = snapshot.Mode,
                MaxHistory = snapshot.MaxHistory,
                CapturedVariableHistory = snapshot.CapturedVariableHistory
                    .Select(PausePointCapturedHistoryFrame.FromSnapshot)
                    .ToList(),
                HistoryDroppedCount = snapshot.HistoryDroppedCount,
                Expired = snapshot.Expired,
                EnabledAtUtc = snapshot.EnabledAtUtc,
                ElapsedSinceEnabledMilliseconds = snapshot.ElapsedSinceEnabledMilliseconds,
                RemainingMilliseconds = snapshot.RemainingMilliseconds,
                Generation = snapshot.Generation,
                EditorState = PausePointEditorState.FromSnapshot(snapshot.EditorState),
                FirstHitAtUtc = snapshot.FirstHitAtUtc,
                LastHitAtUtc = snapshot.LastHitAtUtc,
                FirstHitSequence = snapshot.FirstHitSequence,
                LastHitSequence = snapshot.LastHitSequence,
                Message = snapshot.Message,
                RecommendedNextAction = snapshot.RecommendedNextAction,
                ClearedReason = snapshot.ClearedReason,
                StatusBeforeClear = snapshot.StatusBeforeClear,
                LateHitDiscardedAfterClear = snapshot.LateHitDiscardedAfterClear
            };
        }

        internal static PausePointResponse FromClearAll(UloopPausePointClearAllResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return new PausePointResponse
            {
                Status = UloopPausePointStatus.Cleared,
                ClearedCount = result.ClearedCount,
                EditorState = PausePointEditorState.FromSnapshot(result.EditorState),
                Message = result.ClearedCount == 0
                    ? "No active pause points to clear."
                    : "Pause points cleared."
            };
        }
    }

    /// <summary>
    /// One formatted capture frame included in the enable-pause-point response history.
    /// </summary>
    public class PausePointCapturedHistoryFrame
    {
        public int HitSequence { get; set; }
        public int FrameCount { get; set; }
        public string HitAtUtc { get; set; } = string.Empty;
        public IReadOnlyList<PausePointCapturedVariable> CapturedVariables { get; set; } =
            Array.Empty<PausePointCapturedVariable>();
        public bool Truncated { get; set; }

        internal static PausePointCapturedHistoryFrame FromSnapshot(
            UloopPausePointCapturedHistoryFrame snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new PausePointCapturedHistoryFrame
            {
                HitSequence = snapshot.HitSequence,
                FrameCount = snapshot.FrameCount,
                HitAtUtc = snapshot.HitAtUtc,
                CapturedVariables = snapshot.CapturedVariables
                    .Select(PausePointCapturedVariable.FromSnapshot)
                    .ToList(),
                Truncated = snapshot.Truncated
            };
        }
    }

    /// <summary>
    /// One formatted variable included in a pause point capture history frame.
    /// </summary>
    public class PausePointCapturedVariable
    {
        public string Name { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string UnityObjectKind { get; set; } = string.Empty;
        public string UnityObjectPath { get; set; } = string.Empty;
        public int UnityObjectInstanceId { get; set; }

        internal static PausePointCapturedVariable FromSnapshot(UloopCapturedVariable snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new PausePointCapturedVariable
            {
                Name = snapshot.Name,
                Scope = snapshot.Scope,
                TypeName = snapshot.TypeName,
                Value = snapshot.Value,
                UnityObjectKind = snapshot.UnityObjectKind,
                UnityObjectPath = snapshot.UnityObjectPath,
                UnityObjectInstanceId = snapshot.UnityObjectInstanceId
            };
        }
    }

    /// <summary>
    /// Unity Editor play state attached to pause point tool evidence.
    /// </summary>
    public class PausePointEditorState
    {
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public string CapturedAt { get; set; } = string.Empty;

        internal static PausePointEditorState FromSnapshot(UloopPausePointEditorStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new PausePointEditorState
            {
                IsPlaying = snapshot.IsPlaying,
                IsPaused = snapshot.IsPaused,
                CapturedAt = snapshot.CapturedAt
            };
        }
    }

    /// <summary>
    /// Exposes pause point enabling as a Unity CLI Loop tool.
    /// </summary>
    [UnityCliLoopTool]
    public class EnablePausePointTool : UnityCliLoopTool<EnablePausePointSchema, PausePointResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_ENABLE_PAUSE_POINT;

        protected override Task<PausePointResponse> ExecuteAsync(EnablePausePointSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            PausePointUseCase useCase = new();
            PausePointResponse response = useCase.Enable(parameters);
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Exposes pause point clearing as a Unity CLI Loop tool.
    /// </summary>
    [UnityCliLoopTool]
    public class ClearPausePointTool : UnityCliLoopTool<ClearPausePointSchema, PausePointResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_CLEAR_PAUSE_POINT;

        protected override Task<PausePointResponse> ExecuteAsync(ClearPausePointSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            PausePointUseCase useCase = new();
            PausePointResponse response = useCase.Clear(parameters);
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Coordinates pause point tool validation and registry updates.
    /// </summary>
    internal sealed class PausePointUseCase
    {
        public PausePointResponse Enable(EnablePausePointSchema parameters)
        {
            string captureSettingsError = ValidateCaptureSettings(parameters);
            if (captureSettingsError != null)
            {
                return CreateValidationFailure(captureSettingsError);
            }

            string modeError = ValidateEnableMode(parameters);
            if (modeError != null)
            {
                return CreateValidationFailure(modeError);
            }

            if (parameters.TimeoutSeconds <= 0)
            {
                return CreateValidationFailure("TimeoutSeconds must be greater than zero.");
            }

            if (!string.IsNullOrWhiteSpace(parameters.File))
            {
                return EnableBySourceLocation(parameters);
            }

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Enable(
                parameters.Id,
                parameters.TimeoutSeconds,
                parameters.Mode,
                parameters.MaxHistory);
            PausePointResponse response = PausePointResponse.FromSnapshot(snapshot);
            response.Warning = CreateEnableWarning();
            return response;
        }

        public PausePointResponse Clear(ClearPausePointSchema parameters)
        {
            if (parameters.All)
            {
                // Registry.ClearAll unpatches any source pause points via the hook
                // SourcePausePointPatcher wires into it; this use case never references the
                // Patcher directly.
                UloopPausePointClearAllResult clearAllResult = UloopPausePointRegistry.ClearAll();
                return PausePointResponse.FromClearAll(clearAllResult);
            }

            string idError = ValidateId(parameters.Id);
            if (idError != null)
            {
                return CreateValidationFailure(idError);
            }

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Clear(parameters.Id);
            return PausePointResponse.FromSnapshot(snapshot);
        }

        // Resolves File:Line to a patch location via the Resolver, patches it via Harmony, then
        // arms the same registry state machine the Id path uses, keyed by the derived source id.
        private static PausePointResponse EnableBySourceLocation(EnablePausePointSchema parameters)
        {
            if (CompilationPipeline.codeOptimization == CodeOptimization.Release)
            {
                return CreateValidationFailure(SourcePausePointConstants.ReleaseCodeOptimizationRejectionMessage);
            }

            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(parameters.File, parameters.Line);
            if (!resolveResult.Success)
            {
                return CreateValidationFailure(resolveResult.ErrorMessage);
            }

            string id = BuildSourcePausePointId(parameters.File, parameters.Line);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);
            if (!patchResult.Success)
            {
                return new PausePointResponse
                {
                    Success = false,
                    Message = patchResult.ErrorMessage,
                    RecommendedNextAction = patchResult.Hint
                };
            }

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Enable(
                id,
                parameters.TimeoutSeconds,
                parameters.Mode,
                parameters.MaxHistory);
            PausePointResponse response = PausePointResponse.FromSnapshot(snapshot);
            response.ResolvedLine = resolveResult.Resolution.ResolvedLine;
            response.ResolvedMethod = resolveResult.Resolution.MethodDisplayName;
            response.Warning = MergeWarnings(CreateEnableWarning(), patchResult.Warning);
            return response;
        }

        // The derived id must use the originally requested file/line (not the resolved/rounded
        // line) so repeated calls at the same requested location stay idempotent.
        private static string BuildSourcePausePointId(string file, int line)
        {
            return SourcePausePointPathNormalizer.ToForwardSlashes(file) + ":" + line;
        }

        private static string MergeWarnings(string first, string second)
        {
            if (string.IsNullOrEmpty(first))
            {
                return second;
            }

            if (string.IsNullOrEmpty(second))
            {
                return first;
            }

            return first + " " + second;
        }

        // Returns an error message when the Id/File/Line combination fails validation, or null
        // when exactly one of "Id" or "File"+"Line" is provided.
        private static string ValidateEnableMode(EnablePausePointSchema parameters)
        {
            bool hasId = !string.IsNullOrWhiteSpace(parameters.Id);
            bool hasFile = !string.IsNullOrWhiteSpace(parameters.File);
            bool hasLine = parameters.Line > 0;

            if (hasId && (hasFile || hasLine))
            {
                return "Specify either Id or File and Line, not both.";
            }

            if (!hasId && !hasFile && !hasLine)
            {
                return "Id must not be null or empty.";
            }

            if (!hasId && hasFile != hasLine)
            {
                return "File and Line must both be provided together.";
            }

            return null;
        }

        private static string ValidateCaptureSettings(EnablePausePointSchema parameters)
        {
            string[] supportedModes =
            {
                UloopPausePointCaptureMode.SingleShot,
                UloopPausePointCaptureMode.Continuous,
                UloopPausePointCaptureMode.Trace
            };
            if (!supportedModes.Contains(parameters.Mode))
            {
                return $"Mode must be one of: {string.Join(", ", supportedModes)}.";
            }

            if (parameters.MaxHistory <= 0 || parameters.MaxHistory > UloopPausePointRegistry.MaxHistoryLimit)
            {
                return $"MaxHistory must be between 1 and {UloopPausePointRegistry.MaxHistoryLimit}.";
            }

            return null;
        }

        // Returns an error message when id fails validation, or null when it is valid.
        private static string ValidateId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return "Id must not be null or empty.";
            }

            return null;
        }

        private static PausePointResponse CreateValidationFailure(string message)
        {
            return new PausePointResponse
            {
                Success = false,
                Message = message
            };
        }

        private static string CreateEnableWarning()
        {
            if (EditorApplication.isPlaying)
            {
                return string.Empty;
            }

            if (IsDomainReloadDisabledOnEnterPlayMode())
            {
                return string.Empty;
            }

            return "Pause point was enabled before PlayMode while Domain Reload is enabled. " +
                   "Entering PlayMode may clear this marker; keep Domain Reload disabled for this workflow or enable the marker after PlayMode starts.";
        }

        private static bool IsDomainReloadDisabledOnEnterPlayMode()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled)
            {
                return false;
            }

            return (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;
        }
    }
}
