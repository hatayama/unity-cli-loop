using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class PrewarmDynamicCodeUseCaseTests
    {
        [SetUp]
        public void SetUp()
        {
            DynamicCodeStartupTelemetry.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            DynamicCodeStartupTelemetry.Reset();
        }

        [Test]
        public async Task RequestAsync_WhenSupportedAndWarmupSucceeds_ShouldRetryOnNextRequest()
        {
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(true);
            FakeDynamicCodeAutoPrewarmExecutor executor = new FakeDynamicCodeAutoPrewarmExecutor(
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true });
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(
                runtime,
                default,
                executor);

            await useCase.RequestAsync();
            await useCase.RequestAsync();

            Assert.That(executor.Requests, Has.Count.EqualTo(8));
            Assert.That(
                executor.Requests[0].Code,
                Does.Contain("UnityEngine.Debug.unityLogger.filterLogType = UnityEngine.LogType.Warning;"));
            Assert.That(
                executor.Requests[0].Code,
                Does.Contain("UnityEngine.Debug.Log(\"Unity CLI Loop dynamic code prewarm\");"));
            Assert.That(
                executor.Requests[0].Code,
                Does.Contain("return \"Unity CLI Loop dynamic code prewarm\";"));
            Assert.That(executor.Requests[0].CompileOnly, Is.False);
            Assert.That(executor.Requests[0].YieldToForegroundRequests, Is.True);
            Assert.That(executor.Requests[1].Code, Is.EqualTo(executor.Requests[0].Code));
            Assert.That(executor.Requests[2].Code, Is.EqualTo(executor.Requests[0].Code));
            Assert.That(executor.Requests[3].Code, Does.StartWith("using UnityEngine;"));
            Assert.That(executor.Requests[4].Code, Is.EqualTo(executor.Requests[0].Code));
            Assert.That(DynamicCodeStartupTelemetry.CreateTimingEntries(), Has.Member("[Perf] WarmReady: True"));
        }

        [Test]
        public async Task RequestAsync_WhenFastPathIsUnavailable_ShouldSkipExecution()
        {
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(false);
            FakeDynamicCodeAutoPrewarmExecutor executor = new FakeDynamicCodeAutoPrewarmExecutor();
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(runtime, default, executor);

            await useCase.RequestAsync();
            await useCase.RequestAsync();

            Assert.That(executor.Requests, Is.Empty);
            Assert.That(
                DynamicCodeStartupTelemetry.CreateTimingEntries(),
                Has.Member("[Perf] PrewarmDetail: fast_path_unavailable"));
        }

        [Test]
        public async Task RequestAsync_WhenExecuteDynamicCodeIsDisabled_ShouldSkipExecution()
        {
            string settingsPath = Path.Combine(McpConstants.ULOOP_DIR, McpConstants.ULOOP_TOOL_SETTINGS_FILE_NAME);
            bool hadSettingsFile = File.Exists(settingsPath);
            string originalSettingsJson = hadSettingsFile ? File.ReadAllText(settingsPath) : null;
            ToolSettings.InvalidateCache();
            ToolSettings.SetToolEnabled(McpConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE, false);

            FakePrewarmRuntime runtime = new FakePrewarmRuntime(true);
            FakeDynamicCodeAutoPrewarmExecutor executor = new FakeDynamicCodeAutoPrewarmExecutor();
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(runtime, default, executor);

            try
            {
                await useCase.RequestAsync();

                Assert.That(executor.Requests, Is.Empty);
                Assert.That(
                    DynamicCodeStartupTelemetry.CreateTimingEntries(),
                    Has.Member("[Perf] PrewarmDetail: tool_disabled"));
            }
            finally
            {
                if (hadSettingsFile)
                {
                    File.WriteAllText(settingsPath, originalSettingsJson);
                }
                else if (File.Exists(settingsPath))
                {
                    File.Delete(settingsPath);
                }

                ToolSettings.InvalidateCache();
            }
        }

        [Test]
        public async Task RequestAsync_WhenWarmupFails_ShouldRetryOnNextRequest()
        {
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(true);
            FakeDynamicCodeAutoPrewarmExecutor executor = new FakeDynamicCodeAutoPrewarmExecutor(
                new DynamicCodeAutoPrewarmResult
                {
                    Success = false,
                    ErrorMessage = "warmup failed"
                },
                new DynamicCodeAutoPrewarmResult
                {
                    Success = true
                },
                new DynamicCodeAutoPrewarmResult
                {
                    Success = true
                },
                new DynamicCodeAutoPrewarmResult
                {
                    Success = true
                },
                new DynamicCodeAutoPrewarmResult
                {
                    Success = true
                });
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(runtime, default, executor);

            await useCase.RequestAsync();
            await useCase.RequestAsync();

            Assert.That(executor.Requests, Has.Count.EqualTo(5));
        }

        [Test]
        public async Task RequestAsync_WhenRuntimeIsBusy_ShouldSkipExecution()
        {
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(true);
            FakeDynamicCodeAutoPrewarmExecutor executor = new FakeDynamicCodeAutoPrewarmExecutor(
                new DynamicCodeAutoPrewarmResult
                {
                    Success = false,
                    ErrorMessage = McpConstants.ERROR_MESSAGE_EXECUTION_IN_PROGRESS
                });
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(runtime, default, executor);

            await useCase.RequestAsync();

            Assert.That(executor.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RequestAsync_WhenLoopbackWarmupTimesOut_ShouldRetryWithinTheSameRequest()
        {
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(true);
            FakeDynamicCodeAutoPrewarmExecutor executor = new FakeDynamicCodeAutoPrewarmExecutor(
                new DynamicCodeAutoPrewarmResult
                {
                    Success = false,
                    ErrorMessage = TcpDynamicCodeAutoPrewarmExecutor.TimeoutErrorMessage
                },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true });
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(runtime, default, executor);

            await useCase.RequestAsync();

            Assert.That(executor.Requests, Has.Count.EqualTo(5));
            Assert.That(DynamicCodeStartupTelemetry.CreateTimingEntries(), Has.Member("[Perf] WarmReady: True"));
        }

        [Test]
        public async Task RequestAsync_WhenLoopbackTransportFails_ShouldRetryWithinTheSameRequest()
        {
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(true);
            FakeDynamicCodeAutoPrewarmExecutor executor = new FakeDynamicCodeAutoPrewarmExecutor(
                new DynamicCodeAutoPrewarmResult
                {
                    Success = false,
                    ErrorMessage = TcpDynamicCodeAutoPrewarmExecutor.TransportErrorMessage
                },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true });
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(runtime, default, executor);

            await useCase.RequestAsync();

            Assert.That(executor.Requests, Has.Count.EqualTo(5));
            Assert.That(DynamicCodeStartupTelemetry.CreateTimingEntries(), Has.Member("[Perf] WarmReady: True"));
        }

        [Test]
        public async Task RequestAsync_WhenTransportFailsThenExecutionStaysBusy_ShouldWaitForTheEarlierAttemptToFinish()
        {
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(true);
            FakeDynamicCodeAutoPrewarmExecutor executor = new FakeDynamicCodeAutoPrewarmExecutor(
                new DynamicCodeAutoPrewarmResult
                {
                    Success = false,
                    ErrorMessage = TcpDynamicCodeAutoPrewarmExecutor.TransportErrorMessage
                },
                new DynamicCodeAutoPrewarmResult
                {
                    Success = false,
                    ErrorMessage = McpConstants.ERROR_MESSAGE_EXECUTION_IN_PROGRESS
                },
                new DynamicCodeAutoPrewarmResult
                {
                    Success = false,
                    ErrorMessage = McpConstants.ERROR_MESSAGE_EXECUTION_IN_PROGRESS
                },
                new DynamicCodeAutoPrewarmResult
                {
                    Success = false,
                    ErrorMessage = McpConstants.ERROR_MESSAGE_EXECUTION_IN_PROGRESS
                },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true });
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(runtime, default, executor);

            await useCase.RequestAsync();

            Assert.That(executor.Requests, Has.Count.EqualTo(8));
            Assert.That(DynamicCodeStartupTelemetry.CreateTimingEntries(), Has.Member("[Perf] WarmReady: True"));
        }

        [Test]
        public async Task RequestAsync_WhenLifecycleIsCancelled_ShouldStopBeforeExecution()
        {
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(true);
            FakeDynamicCodeAutoPrewarmExecutor executor = new FakeDynamicCodeAutoPrewarmExecutor(
                new DynamicCodeAutoPrewarmResult { Success = true });
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(
                runtime,
                cancellationTokenSource.Token,
                executor);

            await useCase.RequestAsync();

            Assert.That(executor.Requests, Is.Empty);
        }

        [Test]
        public async Task RequestAsync_WhenStartupLockTokenIsAttached_ShouldDeleteStartupLockAfterCompletion()
        {
            string createdToken = ServerStartingLockService.CreateLockFile();
            Assert.That(createdToken, Is.Not.Null.And.Not.Empty);

            string lockPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Temp", "serverstarting.lock"));
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(true);
            FakeDynamicCodeAutoPrewarmExecutor executor = new FakeDynamicCodeAutoPrewarmExecutor(
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true },
                new DynamicCodeAutoPrewarmResult { Success = true });
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(
                runtime,
                default,
                executor,
                createdToken);

            try
            {
                await useCase.RequestAsync();

                Assert.That(File.Exists(lockPath), Is.False);
            }
            finally
            {
                ServerStartingLockService.DeleteOwnedLockFile(createdToken);
            }
        }

        private sealed class FakePrewarmRuntime : IDynamicCodeExecutionRuntime
        {
            private readonly bool _supportsAutoPrewarm;

            public FakePrewarmRuntime(bool supportsAutoPrewarm)
            {
                _supportsAutoPrewarm = supportsAutoPrewarm;
            }

            public bool SupportsAutoPrewarm()
            {
                return _supportsAutoPrewarm;
            }

            public Task<ExecutionResult> ExecuteAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                Assert.Fail("Prewarm should go through the execute-dynamic-code entry path instead of the runtime facade.");
                return Task.FromResult(new ExecutionResult { Success = false });
            }

            public Task<(bool Entered, ExecutionResult Result)> TryExecuteIfIdleAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                Assert.Fail("Prewarm should no longer rely on the idle-only runtime path.");
                return Task.FromResult((false, new ExecutionResult { Success = false }));
            }
        }

        private sealed class FakeDynamicCodeAutoPrewarmExecutor : IDynamicCodeAutoPrewarmExecutor
        {
            private readonly Queue<Task<DynamicCodeAutoPrewarmResult>> _resultTasks;

            public FakeDynamicCodeAutoPrewarmExecutor()
                : this(System.Array.Empty<Task<DynamicCodeAutoPrewarmResult>>())
            {
            }

            public FakeDynamicCodeAutoPrewarmExecutor(
                params DynamicCodeAutoPrewarmResult[] results)
                : this(WrapResults(results))
            {
            }

            public FakeDynamicCodeAutoPrewarmExecutor(
                params Task<DynamicCodeAutoPrewarmResult>[] resultTasks)
            {
                _resultTasks = new Queue<Task<DynamicCodeAutoPrewarmResult>>(
                    resultTasks ?? System.Array.Empty<Task<DynamicCodeAutoPrewarmResult>>());
            }

            public List<ExecuteDynamicCodeSchema> Requests { get; } = new List<ExecuteDynamicCodeSchema>();

            public Task<DynamicCodeAutoPrewarmResult> ExecuteAsync(
                ExecuteDynamicCodeSchema parameters,
                CancellationToken cancellationToken)
            {
                Requests.Add(new ExecuteDynamicCodeSchema
                {
                    Code = parameters.Code,
                    CompileOnly = parameters.CompileOnly,
                    YieldToForegroundRequests = parameters.YieldToForegroundRequests
                });

                return _resultTasks.Dequeue();
            }

            private static Task<DynamicCodeAutoPrewarmResult>[] WrapResults(
                DynamicCodeAutoPrewarmResult[] results)
            {
                if (results == null)
                {
                    return System.Array.Empty<Task<DynamicCodeAutoPrewarmResult>>();
                }

                Task<DynamicCodeAutoPrewarmResult>[] wrappedResults =
                    new Task<DynamicCodeAutoPrewarmResult>[results.Length];
                for (int index = 0; index < results.Length; index++)
                {
                    wrappedResults[index] = Task.FromResult(results[index]);
                }

                return wrappedResults;
            }
        }
    }
}
