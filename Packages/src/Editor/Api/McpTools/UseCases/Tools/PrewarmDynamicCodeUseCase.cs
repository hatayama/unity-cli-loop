using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    internal sealed class PrewarmDynamicCodeUseCase : IPrewarmDynamicCodeUseCase
    {
        private const int AutoPrewarmDelayFrameCount = 1;
        private const string AutoPrewarmCode = "return null;";
        private const string AutoPrewarmClassName = "DynamicCodeAutoPrewarmCommand";
        private const string AutoPrewarmOperation = "dynamic_code_auto_prewarm";

        private readonly IDynamicCodeExecutionRuntime _runtime;
        private readonly CancellationToken _lifecycleCancellationToken;
        private readonly object _autoPrewarmLock = new();
        private Task _autoPrewarmTask;

        public PrewarmDynamicCodeUseCase(
            IDynamicCodeExecutionRuntime runtime,
            CancellationToken lifecycleCancellationToken = default)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _lifecycleCancellationToken = lifecycleCancellationToken;
        }

        public void Request()
        {
            RequestAsync().Forget();
        }

        public Task RequestAsync()
        {
            lock (_autoPrewarmLock)
            {
                if (_autoPrewarmTask != null && !_autoPrewarmTask.IsCompleted)
                {
                    return _autoPrewarmTask;
                }

                DynamicCodeStartupTelemetry.MarkPrewarmQueued();
                _autoPrewarmTask = RunAsync();
                return _autoPrewarmTask;
            }
        }

        private async Task RunAsync()
        {
            try
            {
                _lifecycleCancellationToken.ThrowIfCancellationRequested();
                if (!_runtime.SupportsAutoPrewarm())
                {
                    DynamicCodeStartupTelemetry.MarkPrewarmSkipped("fast_path_unavailable");
                    VibeLogger.LogInfo(
                        AutoPrewarmOperation,
                        "Skipping dynamic code auto prewarm because the fast path is unavailable",
                        new { reason = "fast_path_unavailable" });

                    return;
                }

                VibeLogger.LogInfo(
                    AutoPrewarmOperation,
                    "Starting dynamic code auto prewarm",
                    new { delay_frames = AutoPrewarmDelayFrameCount, class_name = AutoPrewarmClassName });

                await EditorDelay.DelayFrame(AutoPrewarmDelayFrameCount, _lifecycleCancellationToken);
                DynamicCodeStartupTelemetry.MarkPrewarmStarted();

                DynamicCodeExecutionRequest request = new DynamicCodeExecutionRequest
                {
                    Code = AutoPrewarmCode,
                    ClassName = AutoPrewarmClassName,
                    SecurityLevel = ULoopSettings.GetDynamicCodeSecurityLevel(),
                    CompileOnly = false,
                    YieldToForegroundRequests = true
                };

                Task<(bool Entered, ExecutionResult Result)> executionTask = _runtime.TryExecuteIfIdleAsync(
                    request,
                    _lifecycleCancellationToken);
                await Task.WhenAny(executionTask);

                if (executionTask.IsCanceled)
                {
                    if (_lifecycleCancellationToken.IsCancellationRequested)
                    {
                        DynamicCodeStartupTelemetry.MarkPrewarmSkipped("lifecycle_cancelled");
                        return;
                    }

                    DynamicCodeStartupTelemetry.MarkPrewarmYielded("foreground_request_preempted");
                    LogForegroundPreemption();
                    return;
                }

                if (executionTask.IsFaulted)
                {
                    Exception exception = executionTask.Exception?.InnerException ?? executionTask.Exception;
                    if (IsLifecycleCancellation(exception))
                    {
                        DynamicCodeStartupTelemetry.MarkPrewarmSkipped("lifecycle_cancelled");
                        return;
                    }

                    if (exception is OperationCanceledException)
                    {
                        DynamicCodeStartupTelemetry.MarkPrewarmYielded("foreground_request_preempted");
                        LogForegroundPreemption();
                        return;
                    }

                    ExceptionDispatchInfo.Capture(exception ?? executionTask.Exception).Throw();
                }

                (bool entered, ExecutionResult result) = executionTask.Result;

                if (!entered)
                {
                    DynamicCodeStartupTelemetry.MarkPrewarmSkipped("runtime_busy");
                    VibeLogger.LogInfo(
                        AutoPrewarmOperation,
                        "Skipping dynamic code auto prewarm because execute-dynamic-code is busy",
                        new
                        {
                            class_name = AutoPrewarmClassName,
                            reason = "runtime_busy"
                        });
                    return;
                }

                if (WasCancelledByForegroundRequest(result))
                {
                    DynamicCodeStartupTelemetry.MarkPrewarmYielded("foreground_request_preempted");
                    LogForegroundPreemption();
                    return;
                }

                if (!result.Success)
                {
                    DynamicCodeStartupTelemetry.MarkPrewarmFailed(result.ErrorMessage);
                    VibeLogger.LogWarning(
                        AutoPrewarmOperation,
                        "Dynamic code auto prewarm failed",
                        new
                        {
                            class_name = AutoPrewarmClassName,
                            error_message = result.ErrorMessage,
                            logs = result.Logs
                        });
                    return;
                }

                DynamicCodeStartupTelemetry.MarkPrewarmCompleted();
                VibeLogger.LogInfo(
                    AutoPrewarmOperation,
                    "Dynamic code auto prewarm completed successfully",
                    new { class_name = AutoPrewarmClassName });
                return;
            }
            catch (OperationCanceledException) when (_lifecycleCancellationToken.IsCancellationRequested)
            {
                DynamicCodeStartupTelemetry.MarkPrewarmSkipped("lifecycle_cancelled");
                return;
            }
            catch (ObjectDisposedException) when (_lifecycleCancellationToken.IsCancellationRequested)
            {
                DynamicCodeStartupTelemetry.MarkPrewarmSkipped("lifecycle_cancelled");
                return;
            }
        }

        private static bool WasCancelledByForegroundRequest(ExecutionResult result)
        {
            if (result == null)
            {
                return false;
            }

            return !result.Success
                && string.Equals(
                    result.ErrorMessage,
                    McpConstants.ERROR_MESSAGE_EXECUTION_CANCELLED,
                    StringComparison.Ordinal);
        }

        private static void LogForegroundPreemption()
        {
            VibeLogger.LogInfo(
                AutoPrewarmOperation,
                "Dynamic code auto prewarm yielded to a foreground execute-dynamic-code request",
                new { reason = "foreground_request_preempted" });
        }

        private bool IsLifecycleCancellation(Exception exception)
        {
            if (!_lifecycleCancellationToken.IsCancellationRequested)
            {
                return false;
            }

            return exception is OperationCanceledException
                || exception is ObjectDisposedException;
        }
    }
}
