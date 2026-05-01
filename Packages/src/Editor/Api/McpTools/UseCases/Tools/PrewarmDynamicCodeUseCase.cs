using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace io.github.hatayama.uLoopMCP
{
    internal sealed class PrewarmDynamicCodeUseCase : IPrewarmDynamicCodeUseCase
    {
        private const int AutoPrewarmDelayFrameCount = 1;
        private const int AutoPrewarmStablePassCount = 3;
        private const int AutoPrewarmUserLikePassCount = 1;
        private const int AutoPrewarmPassCount = AutoPrewarmStablePassCount + AutoPrewarmUserLikePassCount;
        private const int AutoPrewarmMaxAttempts = 6;
        private const int AutoPrewarmBusyRetryDelayFrameCount = 1;
        // Why: startup measurements showed the first user-visible execute-dynamic-code request
        // stayed cold until the stable logging path and the default `using UnityEngine;` wrapper
        // shape had both run at least once.
        // Why not a cheaper "return null;" snippet or a dedicated prewarm class name: those warm
        // compiler registration, but they miss the default execute-dynamic-code cache key and
        // leave the first real request measurably slower.
        // Why: after a forced compile + domain reload, the first two execute-dynamic-code requests
        // still paid cold-start costs, and only the third pass reached steady-state latency in measurements.
        // Why not stop at two passes: that still leaves the first post-reload user-visible request
        // hundreds of milliseconds slower than the warmed path, which is the exact regression we need to avoid.
        // Why not warm only the stable fully-qualified snippet: that keeps startup resilient, but
        // it still leaves `using UnityEngine; Debug.Log(...)` requests paying a separate cold path.
        private const string AutoPrewarmStableCode =
            "UnityEngine.LogType previous = UnityEngine.Debug.unityLogger.filterLogType; UnityEngine.Debug.unityLogger.filterLogType = UnityEngine.LogType.Warning; try { UnityEngine.Debug.Log(\"Unity CLI Loop dynamic code prewarm\"); return \"Unity CLI Loop dynamic code prewarm\"; } finally { UnityEngine.Debug.unityLogger.filterLogType = previous; }";
        private const string AutoPrewarmUserLikeCode =
            "using UnityEngine; LogType previous = Debug.unityLogger.filterLogType; Debug.unityLogger.filterLogType = LogType.Warning; try { Debug.Log(\"Unity CLI Loop dynamic code prewarm\"); return \"Unity CLI Loop dynamic code prewarm\"; } finally { Debug.unityLogger.filterLogType = previous; }";
        private const string AutoPrewarmClassName = DynamicCodeConstants.DEFAULT_CLASS_NAME;
        private const string AutoPrewarmOperation = "dynamic_code_auto_prewarm";

        private readonly IDynamicCodeExecutionRuntime _runtime;
        private readonly IDynamicCodeAutoPrewarmExecutor _executor;
        private readonly CancellationToken _lifecycleCancellationToken;
        private string _serverStartingLockToken;
        private readonly object _autoPrewarmLock = new();
        private Task _autoPrewarmTask;

        public PrewarmDynamicCodeUseCase(
            IDynamicCodeExecutionRuntime runtime,
            CancellationToken lifecycleCancellationToken = default,
            IDynamicCodeAutoPrewarmExecutor executor = null,
            string serverStartingLockToken = null)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _executor = executor ?? new DynamicCodeAutoPrewarmExecutor();
            _lifecycleCancellationToken = lifecycleCancellationToken;
            _serverStartingLockToken = serverStartingLockToken;
        }

        internal void AttachServerStartingLockToken(string serverStartingLockToken)
        {
            if (string.IsNullOrEmpty(serverStartingLockToken))
            {
                return;
            }

            lock (_autoPrewarmLock)
            {
                if (string.IsNullOrEmpty(_serverStartingLockToken))
                {
                    _serverStartingLockToken = serverStartingLockToken;
                    return;
                }

                UnityEngine.Debug.Assert(
                    string.Equals(_serverStartingLockToken, serverStartingLockToken, StringComparison.Ordinal),
                    "serverStartingLockToken must not change once attached");
            }
        }

        internal string GetServerStartingLockTokenForTests()
        {
            lock (_autoPrewarmLock)
            {
                return _serverStartingLockToken;
            }
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

                if (!ToolSettings.IsToolEnabled(McpConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE))
                {
                    DynamicCodeStartupTelemetry.MarkPrewarmSkipped("tool_disabled");
                    VibeLogger.LogInfo(
                        AutoPrewarmOperation,
                        "Skipping dynamic code auto prewarm because execute-dynamic-code is disabled",
                        new { reason = "tool_disabled" });
                    return;
                }

                VibeLogger.LogInfo(
                    AutoPrewarmOperation,
                    "Starting dynamic code auto prewarm",
                    new
                    {
                        delay_frames = AutoPrewarmDelayFrameCount,
                        class_name = AutoPrewarmClassName,
                        pass_count = AutoPrewarmPassCount
                    });

                await EditorDelay.DelayFrame(AutoPrewarmDelayFrameCount, _lifecycleCancellationToken);
                DynamicCodeStartupTelemetry.MarkPrewarmStarted();

                int successfulPassCount = 0;
                bool sawTransientPrewarmFailure = false;
                int transientBusyRetryCount = 0;
                for (int attemptIndex = 0;
                     attemptIndex < AutoPrewarmMaxAttempts && successfulPassCount < AutoPrewarmPassCount;
                     attemptIndex++)
                {
                    string autoPrewarmCode = GetAutoPrewarmCodeForPass(successfulPassCount);
                    ExecuteDynamicCodeSchema parameters = new ExecuteDynamicCodeSchema
                    {
                        Code = autoPrewarmCode,
                        CompileOnly = false,
                        YieldToForegroundRequests = true
                    };

                    // Why: the remaining domain-reload spike only disappeared when measurements warmed the
                    // full execute-dynamic-code entry path, not just the runtime facade underneath it.
                    // Why not keep the runtime-direct prewarm: that warmed compiler startup, but it still
                    // left the first real CLI request paying extra cost in the tool/JSON-RPC layers.
                    using IDisposable diagnosticSourceScope =
                        DynamicCompilationHealthMonitor.UseConsoleDiagnosticSource(AutoPrewarmOperation);
                    DynamicCodeAutoPrewarmResult result = await _executor.ExecuteAsync(
                        parameters,
                        _lifecycleCancellationToken);

                    if (IsExecutionBusy(result))
                    {
                        if (sawTransientPrewarmFailure)
                        {
                            transientBusyRetryCount++;
                            if (transientBusyRetryCount >= AutoPrewarmMaxAttempts)
                            {
                                break;
                            }

                            VibeLogger.LogInfo(
                                AutoPrewarmOperation,
                                "Dynamic code auto prewarm is waiting for an earlier transient attempt to finish",
                                new
                                {
                                    class_name = AutoPrewarmClassName,
                                    attempt_index = attemptIndex + 1,
                                    max_attempts = AutoPrewarmMaxAttempts
                                });
                            await EditorDelay.DelayFrame(
                                AutoPrewarmBusyRetryDelayFrameCount,
                                _lifecycleCancellationToken);
                            attemptIndex--;
                            continue;
                        }

                        DynamicCodeStartupTelemetry.MarkPrewarmSkipped("runtime_busy");
                        VibeLogger.LogInfo(
                            AutoPrewarmOperation,
                            "Skipping dynamic code auto prewarm because execute-dynamic-code is busy",
                            new
                            {
                                class_name = AutoPrewarmClassName,
                                reason = "runtime_busy",
                                pass_index = successfulPassCount + 1,
                                pass_count = AutoPrewarmPassCount
                            });
                        return;
                    }

                    if (WasCancelledByForegroundRequest(result))
                    {
                        DynamicCodeStartupTelemetry.MarkPrewarmYielded("foreground_request_preempted");
                        LogForegroundPreemption();
                        return;
                    }

                    if (IsTransientTransportFailure(result))
                    {
                        sawTransientPrewarmFailure = true;
                        transientBusyRetryCount = 0;
                        VibeLogger.LogWarning(
                            AutoPrewarmOperation,
                            "Dynamic code auto prewarm hit a transient bridge failure and will retry",
                            new
                            {
                                class_name = AutoPrewarmClassName,
                                error_message = result.ErrorMessage,
                                attempt_index = attemptIndex + 1,
                                max_attempts = AutoPrewarmMaxAttempts
                            });
                        continue;
                    }

                    if (IsTransientStartupExecutionFailure(result))
                    {
                        sawTransientPrewarmFailure = true;
                        transientBusyRetryCount = 0;
                        VibeLogger.LogWarning(
                            AutoPrewarmOperation,
                            "Dynamic code auto prewarm hit a transient startup execution failure and will retry",
                            new
                            {
                                class_name = AutoPrewarmClassName,
                                error_message = result.ErrorMessage,
                                attempt_index = attemptIndex + 1,
                                max_attempts = AutoPrewarmMaxAttempts
                            });
                        continue;
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
                                pass_index = successfulPassCount + 1,
                                pass_count = AutoPrewarmPassCount
                            });
                        return;
                    }

                    sawTransientPrewarmFailure = false;
                    transientBusyRetryCount = 0;
                    successfulPassCount++;
                    VibeLogger.LogInfo(
                        AutoPrewarmOperation,
                        "Dynamic code auto prewarm pass completed successfully",
                        new
                        {
                            class_name = AutoPrewarmClassName,
                            pass_index = successfulPassCount,
                            pass_count = AutoPrewarmPassCount
                        });
                }

                if (successfulPassCount < AutoPrewarmPassCount)
                {
                    DynamicCodeStartupTelemetry.MarkPrewarmFailed(DynamicCodeAutoPrewarmExecutor.TimeoutErrorMessage);
                    VibeLogger.LogWarning(
                        AutoPrewarmOperation,
                        "Dynamic code auto prewarm exhausted its transient retries",
                        new
                        {
                            class_name = AutoPrewarmClassName,
                            successful_pass_count = successfulPassCount,
                            pass_count = AutoPrewarmPassCount,
                            max_attempts = AutoPrewarmMaxAttempts
                        });
                    return;
                }

                DynamicCodeStartupTelemetry.MarkPrewarmCompleted();
                DynamicCodeForegroundWarmupState.MarkCompletedByBackgroundWarmup();
                VibeLogger.LogInfo(
                    AutoPrewarmOperation,
                    "Dynamic code auto prewarm completed successfully",
                    new
                    {
                        class_name = AutoPrewarmClassName,
                        pass_count = AutoPrewarmPassCount
                    });
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
            finally
            {
                ServerStartingLockService.DeleteOwnedLockFile(GetServerStartingLockTokenForTests());
            }
        }

        private static bool WasCancelledByForegroundRequest(DynamicCodeAutoPrewarmResult result)
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

        private static bool IsExecutionBusy(DynamicCodeAutoPrewarmResult result)
        {
            if (result == null)
            {
                return false;
            }

            return !result.Success
                && string.Equals(
                    result.ErrorMessage,
                    McpConstants.ERROR_MESSAGE_EXECUTION_IN_PROGRESS,
                    StringComparison.Ordinal);
        }

        private static void LogForegroundPreemption()
        {
            VibeLogger.LogInfo(
                AutoPrewarmOperation,
                "Dynamic code auto prewarm yielded to a foreground execute-dynamic-code request",
                new { reason = "foreground_request_preempted" });
        }

        private static bool IsTransientTransportFailure(DynamicCodeAutoPrewarmResult result)
        {
            if (result == null)
            {
                return false;
            }

            if (result.Success)
            {
                return false;
            }

            return string.Equals(
                       result.ErrorMessage,
                       DynamicCodeAutoPrewarmExecutor.TimeoutErrorMessage,
                       StringComparison.Ordinal)
                   || string.Equals(
                       result.ErrorMessage,
                       DynamicCodeAutoPrewarmExecutor.TransportErrorMessage,
                       StringComparison.Ordinal);
        }

        private static string GetAutoPrewarmCodeForPass(int successfulPassCount)
        {
            return successfulPassCount < AutoPrewarmStablePassCount
                ? AutoPrewarmStableCode
                : AutoPrewarmUserLikeCode;
        }

        private static bool IsTransientStartupExecutionFailure(DynamicCodeAutoPrewarmResult result)
        {
            if (result == null || result.Success || string.IsNullOrEmpty(result.ErrorMessage))
            {
                return false;
            }

            string errorMessage = result.ErrorMessage;
            return errorMessage.IndexOf("Internal error", StringComparison.OrdinalIgnoreCase) >= 0
                   || errorMessage.IndexOf("PreUsingResolver.Resolve", StringComparison.OrdinalIgnoreCase) >= 0
                   || errorMessage.IndexOf("System.NullReferenceException", StringComparison.OrdinalIgnoreCase) >= 0;
        }

    }

    internal interface IDynamicCodeAutoPrewarmExecutor
    {
        Task<DynamicCodeAutoPrewarmResult> ExecuteAsync(
            ExecuteDynamicCodeSchema parameters,
            CancellationToken ct);
    }

    internal sealed class DynamicCodeAutoPrewarmResult
    {
        public bool Success { get; set; }

        public string ErrorMessage { get; set; }
    }

    internal sealed class DynamicCodeAutoPrewarmExecutor : IDynamicCodeAutoPrewarmExecutor
    {
        private const string AutoPrewarmRequestId = "dynamic-code-auto-prewarm";
        private const int PrewarmTimeoutMilliseconds = 10000;
        internal const string TimeoutErrorMessage = "dynamic code auto prewarm timed out";
        internal const string TransportErrorMessage = "dynamic code auto prewarm transport failed";
        private readonly Func<string, CancellationToken, Task<string>> _sendRequestAsync;
        private readonly int _timeoutMilliseconds;

        public DynamicCodeAutoPrewarmExecutor()
            : this(SendRequestToCurrentBridgeAsync, PrewarmTimeoutMilliseconds)
        {
        }

        internal DynamicCodeAutoPrewarmExecutor(
            Func<string, CancellationToken, Task<string>> sendRequestAsync,
            int timeoutMilliseconds = PrewarmTimeoutMilliseconds)
        {
            _sendRequestAsync = sendRequestAsync ?? throw new ArgumentNullException(nameof(sendRequestAsync));
            _timeoutMilliseconds = timeoutMilliseconds;
        }

        public async Task<DynamicCodeAutoPrewarmResult> ExecuteAsync(
            ExecuteDynamicCodeSchema parameters,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string requestJson = CreateRequestJson(parameters);
            using CancellationTokenSource timeoutCancellationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCancellationTokenSource.CancelAfter(_timeoutMilliseconds);
            try
            {
                Task<string> responseTask = _sendRequestAsync(
                    requestJson,
                    timeoutCancellationTokenSource.Token);
                string responseJson = await AwaitWithCancellationAsync(
                    responseTask,
                    timeoutCancellationTokenSource.Token);

                ct.ThrowIfCancellationRequested();
                return ParseResponse(responseJson);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new DynamicCodeAutoPrewarmResult
                {
                    Success = false,
                    ErrorMessage = TimeoutErrorMessage
                };
            }
            catch (IOException) when (!ct.IsCancellationRequested)
            {
                return new DynamicCodeAutoPrewarmResult
                {
                    Success = false,
                    ErrorMessage = TransportErrorMessage
                };
            }
            catch (JsonException) when (!ct.IsCancellationRequested)
            {
                return new DynamicCodeAutoPrewarmResult
                {
                    Success = false,
                    ErrorMessage = TransportErrorMessage
                };
            }
        }

        private static string CreateRequestJson(ExecuteDynamicCodeSchema parameters)
        {
            JsonSerializer serializer = JsonSerializer.Create(
                new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });
            JObject request = new JObject
            {
                ["jsonrpc"] = McpServerConfig.JSONRPC_VERSION,
                ["id"] = AutoPrewarmRequestId,
                ["method"] = McpConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                ["params"] = JObject.FromObject(parameters, serializer),
                ["x-uloop"] = new JObject
                {
                    ["expectedProjectRoot"] = McpEditorSettings.GetProjectRootPath(),
                    ["expectedServerSessionId"] = McpEditorSettings.GetServerSessionId()
                }
            };

            return request.ToString(Formatting.None);
        }

        private static async Task<string> SendRequestToCurrentBridgeAsync(
            string requestJson,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return await JsonRpcProcessor.ProcessRequest(requestJson, "dynamic-code-auto-prewarm");
        }

        private static async Task<T> AwaitWithCancellationAsync<T>(Task<T> task, CancellationToken ct)
        {
            System.Diagnostics.Debug.Assert(task != null, "task must not be null");

            Task cancellationTask = Task.Delay(Timeout.Infinite, ct);
            Task completedTask = await Task.WhenAny(task, cancellationTask);
            if (completedTask != task)
            {
                ct.ThrowIfCancellationRequested();
            }

            return await task;
        }

        private static DynamicCodeAutoPrewarmResult ParseResponse(string responseJson)
        {
            JObject response = JObject.Parse(responseJson);
            JToken resultToken = response["result"];
            if (resultToken != null && resultToken.Type != JTokenType.Null)
            {
                ExecuteDynamicCodeResponse executeResponse = resultToken.ToObject<ExecuteDynamicCodeResponse>();
                return new DynamicCodeAutoPrewarmResult
                {
                    Success = executeResponse?.Success == true,
                    ErrorMessage = executeResponse?.ErrorMessage
                };
            }

            JToken errorToken = response["error"];
            return new DynamicCodeAutoPrewarmResult
            {
                Success = false,
                ErrorMessage = errorToken?["message"]?.ToString() ?? "Unknown error"
            };
        }
    }
}
