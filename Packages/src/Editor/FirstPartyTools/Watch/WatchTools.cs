using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Parameters for registering one C# watch expression.
    /// </summary>
    public sealed class EnableWatchSchema : UnityCliLoopToolSchema
    {
        public string Id { get; set; } = string.Empty;
        public string Expression { get; set; } = string.Empty;
        public int MaxHistory { get; set; } = WatchExpressionRegistry.DefaultMaxHistory;
    }

    /// <summary>
    /// Parameters for clearing one or all watch expressions.
    /// </summary>
    public sealed class ClearWatchSchema : UnityCliLoopToolSchema
    {
        public string Id { get; set; } = string.Empty;
        public bool All { get; set; }
    }

    /// <summary>
    /// Parameters for reading one or all watch expression histories.
    /// </summary>
    public sealed class GetWatchValuesSchema : UnityCliLoopToolSchema
    {
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>
    /// Shared response for watch registration, clearing, and value retrieval.
    /// </summary>
    public sealed class WatchResponse : UnityCliLoopToolResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Expression { get; set; } = string.Empty;
        public int MaxHistory { get; set; }
        public int HistoryDroppedCount { get; set; }
        public int ClearedCount { get; set; }
        public string Message { get; set; } = string.Empty;
        public IReadOnlyList<WatchEntryResponse> Watches { get; set; } =
            Array.Empty<WatchEntryResponse>();
        public IReadOnlyList<WatchCompilationErrorResponse> CompilationErrors { get; set; } =
            Array.Empty<WatchCompilationErrorResponse>();
    }

    /// <summary>
    /// One registered watch expression and its bounded evaluation history.
    /// </summary>
    public sealed class WatchEntryResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Expression { get; set; } = string.Empty;
        public int MaxHistory { get; set; }
        public int HistoryDroppedCount { get; set; }
        public IReadOnlyList<WatchHistoryResponse> History { get; set; } =
            Array.Empty<WatchHistoryResponse>();

        /// <summary>
        /// Non-empty when recent evaluations returned the same value, suggesting the linked
        /// pause point has not been hit again since watches only refresh on a changed, paused frame.
        /// </summary>
        public string ValueFrozenHint { get; set; } = string.Empty;

        internal static WatchEntryResponse FromEntry(WatchExpressionEntry entry)
        {
            List<WatchHistoryResponse> history = entry.CreateHistorySnapshot()
                .Select(WatchHistoryResponse.FromEntry)
                .ToList();
            return new WatchEntryResponse
            {
                Id = entry.Id,
                Expression = entry.Expression,
                MaxHistory = entry.MaxHistory,
                HistoryDroppedCount = entry.HistoryDroppedCount,
                History = history,
                ValueFrozenHint = WatchValueFreezeHintEvaluator.EvaluateFreezeHint(history)
            };
        }
    }

    /// <summary>
    /// One watch evaluation result attached to an Editor frame.
    /// </summary>
    public sealed class WatchHistoryResponse
    {
        public int FrameCount { get; set; }
        public string EvaluatedAtUtc { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Value { get; set; } = string.Empty;
        public string ErrorTypeName { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;

        internal static WatchHistoryResponse FromEntry(WatchExpressionHistoryEntry entry)
        {
            WatchEvaluationResult result = entry.Result;
            return new WatchHistoryResponse
            {
                FrameCount = entry.FrameCount,
                EvaluatedAtUtc = entry.EvaluatedAtUtc.ToString("O"),
                Success = result.Success,
                Value = result.Success ? FormatSuccessfulValue(result.Value) : string.Empty,
                ErrorTypeName = result.ErrorTypeName,
                ErrorMessage = result.ErrorMessage
            };
        }

        // Why: watch Value used plain ToString(), so collections collapsed to type names and
        // looked frozen even when contents changed; reuse CapturedVariables preview rules.
        private static string FormatSuccessfulValue(object value)
        {
            if (value == null)
            {
                return "null";
            }

            bool truncated = false;
            if (SourcePausePointCollectionPreviewSerializer.TrySerialize(
                    value,
                    SourcePausePointConstants.MaxCollectionPreviewElementCount,
                    ref truncated,
                    out string preview))
            {
                return preview;
            }

            return value.ToString();
        }
    }

    /// <summary>
    /// One compile diagnostic returned when a watch expression cannot be registered.
    /// </summary>
    public sealed class WatchCompilationErrorResponse
    {
        public int Line { get; set; }
        public int Column { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;

        internal static WatchCompilationErrorResponse FromCompilationError(CompilationError error)
        {
            return new WatchCompilationErrorResponse
            {
                Line = error.Line,
                Column = error.Column,
                Message = error.Message,
                ErrorCode = error.ErrorCode
            };
        }
    }

    /// <summary>
    /// Exposes watch expression registration through the Unity CLI Loop tool catalog.
    /// </summary>
    [UnityCliLoopTool]
    public sealed class EnableWatchTool : UnityCliLoopTool<EnableWatchSchema, WatchResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_ENABLE_WATCH;

        protected override Task<WatchResponse> ExecuteAsync(EnableWatchSchema parameters, CancellationToken ct)
        {
            return WatchUseCase.EnableAsync(parameters, ct);
        }
    }

    /// <summary>
    /// Exposes watch expression clearing through the Unity CLI Loop tool catalog.
    /// </summary>
    [UnityCliLoopTool]
    public sealed class ClearWatchTool : UnityCliLoopTool<ClearWatchSchema, WatchResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_CLEAR_WATCH;

        protected override Task<WatchResponse> ExecuteAsync(ClearWatchSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(WatchUseCase.Clear(parameters));
        }
    }

    /// <summary>
    /// Exposes watch expression histories through the Unity CLI Loop tool catalog.
    /// </summary>
    [UnityCliLoopTool]
    public sealed class GetWatchValuesTool : UnityCliLoopTool<GetWatchValuesSchema, WatchResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_GET_WATCH_VALUES;

        protected override Task<WatchResponse> ExecuteAsync(GetWatchValuesSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(WatchUseCase.GetValues(parameters));
        }
    }

    /// <summary>
    /// Owns watch tool validation, compilation, registration, and response shaping.
    /// </summary>
    internal static class WatchUseCase
    {
        public static async Task<WatchResponse> EnableAsync(EnableWatchSchema parameters, CancellationToken ct)
        {
            string validationError = ValidateEnable(parameters);
            if (validationError != null)
            {
                return CreateFailure(validationError);
            }

            WatchCompilationResult compilationResult = await WatchExpressionServices.Compiler
                .CompileAsync(parameters.Expression, ct).ConfigureAwait(false);
            // Why switch back: CompileAsync resumes off-thread, but Registry/EnsureMonitorStarted
            // touch EditorApplication.update and must run on the Unity main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            if (!compilationResult.Success)
            {
                return CreateCompilationFailure(compilationResult);
            }

            WatchRegistrationResult registrationResult = WatchExpressionServices.Registry.Register(
                parameters.Id,
                parameters.Expression,
                compilationResult.Evaluator,
                parameters.MaxHistory);
            if (!registrationResult.Success)
            {
                return CreateFailure(registrationResult.ErrorMessage);
            }

            WatchExpressionServices.EnsureMonitorStarted();
            WatchExpressionEntry entry = WatchExpressionServices.Registry.GetEntries()
                .Single(candidate => candidate.Id == parameters.Id);
            return WatchResponseFromEntry(entry, "Watch expression enabled.");
        }

        public static WatchResponse Clear(ClearWatchSchema parameters)
        {
            if (parameters.All)
            {
                return new WatchResponse
                {
                    ClearedCount = WatchExpressionServices.Registry.ClearAll(),
                    Message = "Watch expressions cleared."
                };
            }

            if (string.IsNullOrWhiteSpace(parameters.Id))
            {
                return CreateFailure("Id must not be null or empty unless All is true.");
            }

            bool cleared = WatchExpressionServices.Registry.Clear(parameters.Id);
            return cleared
                ? new WatchResponse { Id = parameters.Id, Message = "Watch expression cleared." }
                : CreateFailure($"Watch expression '{parameters.Id}' was not found.");
        }

        public static WatchResponse GetValues(GetWatchValuesSchema parameters)
        {
            IReadOnlyList<WatchExpressionEntry> entries = WatchExpressionServices.Registry.GetEntries();
            if (!string.IsNullOrWhiteSpace(parameters.Id))
            {
                entries = entries.Where(entry => entry.Id == parameters.Id).ToList();
                if (entries.Count == 0)
                {
                    return CreateFailure($"Watch expression '{parameters.Id}' was not found.");
                }
            }

            return new WatchResponse
            {
                Watches = entries.Select(WatchEntryResponse.FromEntry).ToList(),
                Message = entries.Count == 0 ? "No watch expressions are registered." : "Watch values retrieved."
            };
        }

        private static string ValidateEnable(EnableWatchSchema parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters.Id))
            {
                return "Id must not be null or empty.";
            }

            if (string.IsNullOrWhiteSpace(parameters.Expression))
            {
                return "Expression must not be null or empty.";
            }

            if (parameters.MaxHistory <= 0 || parameters.MaxHistory > WatchExpressionRegistry.MaxHistoryLimit)
            {
                return $"MaxHistory must be between 1 and {WatchExpressionRegistry.MaxHistoryLimit}.";
            }

            return null;
        }

        private static WatchResponse CreateCompilationFailure(WatchCompilationResult result)
        {
            return new WatchResponse
            {
                Success = false,
                Message = result.ErrorMessage,
                CompilationErrors = result.CompilationErrors
                    .Select(WatchCompilationErrorResponse.FromCompilationError)
                    .ToList()
            };
        }

        private static WatchResponse CreateFailure(string message)
        {
            return new WatchResponse
            {
                Success = false,
                Message = message
            };
        }

        private static WatchResponse WatchResponseFromEntry(WatchExpressionEntry entry, string message)
        {
            WatchEntryResponse entryResponse = WatchEntryResponse.FromEntry(entry);
            return new WatchResponse
            {
                Id = entryResponse.Id,
                Expression = entryResponse.Expression,
                MaxHistory = entryResponse.MaxHistory,
                HistoryDroppedCount = entryResponse.HistoryDroppedCount,
                Watches = new[] { entryResponse },
                Message = message
            };
        }
    }

    /// <summary>
    /// Holds the domain-scoped watch registry, compiler, and Editor update monitor.
    /// </summary>
    internal static class WatchExpressionServices
    {
        private static readonly UnityWatchEditorStateProvider StateProvider = new();
        private static readonly WatchExpressionRegistry RegistryValue = new(StateProvider);
        private static readonly WatchExpressionCompiler CompilerValue = new(new DynamicCodeCompiler());
        private static readonly WatchExpressionStepMonitor Monitor = new(RegistryValue);

        public static WatchExpressionRegistry Registry => RegistryValue;
        public static WatchExpressionCompiler Compiler => CompilerValue;

        public static void EnsureMonitorStarted()
        {
            Monitor.Start();
        }
    }
}
