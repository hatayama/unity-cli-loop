using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Run Tests Use Case behavior.
    /// </summary>
    public class RunTestsUseCaseTests
    {
        [Test]
        public async Task ExecuteAsync_WithInvalidExecutionState_ShouldFailFastWithoutRunningTests()
        {
            // Verifies validation failures remain command failures without pretending tests failed.
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(
                ValidationResult.Failure("EditMode tests cannot run during play mode. Use control-play-mode --action Stop to exit play mode, then rerun the tests."));
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new()
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                SaveBeforeRun = true
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Status, Is.EqualTo(RunTestsExecutionStatus.ExecutionFailed));
            Assert.That(response.HasFailures, Is.False);
            Assert.That(response.NoTestsFound, Is.False);
            Assert.That(response.NoTestsFoundExplanation, Is.Empty);
            Assert.That(response.Message, Is.EqualTo("EditMode tests cannot run during play mode. Use control-play-mode --action Stop to exit play mode, then rerun the tests."));
            Assert.That(response.CompletedAt, Is.Not.Empty);
            Assert.That(response.TestCount, Is.EqualTo(0));
            Assert.That(response.PassedCount, Is.EqualTo(0));
            Assert.That(response.FailedCount, Is.EqualTo(0));
            Assert.That(response.SkippedCount, Is.EqualTo(0));
            Assert.That(executionService.WasCalled, Is.False);
            Assert.That(validationService.SaveBeforeRun, Is.True);
        }

        [Test]
        public async Task ExecuteAsync_WithNonPositiveTimeoutSeconds_ShouldFailFastWithoutRunningTests()
        {
            // Verifies invalid TimeoutSeconds is rejected before validation or Test Runner work.
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new()
            {
                TimeoutSeconds = 0
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Status, Is.EqualTo(RunTestsExecutionStatus.ExecutionFailed));
            Assert.That(response.Message, Does.Contain("greater than zero"));
            Assert.That(executionService.WasCalled, Is.False);
            Assert.That(validationService.WasCalled, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_WithTimeoutSecondsAboveMax_ShouldFailFastWithoutRunningTests()
        {
            // Verifies TimeoutSeconds above MaxTimeoutSeconds fail before arming CancelAfter.
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new()
            {
                TimeoutSeconds = RunTestsExecutionTimeout.MaxTimeoutSeconds + 1
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Status, Is.EqualTo(RunTestsExecutionStatus.ExecutionFailed));
            Assert.That(response.Message, Does.Contain(RunTestsExecutionTimeout.MaxTimeoutSeconds.ToString()));
            Assert.That(executionService.WasCalled, Is.False);
            Assert.That(validationService.WasCalled, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_WithUnknownTestMode_ShouldFailFastWithoutRunningTests()
        {
            // Verifies unknown enum values do not bypass the EditMode play-state guard.
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new()
            {
                TestMode = (UnityCliLoopTestMode)999
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Does.Contain("Unsupported test mode"));
            Assert.That(executionService.WasCalled, Is.False);
            Assert.That(validationService.WasCalled, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_WithDefaultRequest_ShouldSaveBeforeRun()
        {
            // Verifies default use-case requests preserve the run-tests auto-save behavior.
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new();

            await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(validationService.WasCalled, Is.True);
            Assert.That(validationService.SaveBeforeRun, Is.True);
            Assert.That(executionService.WasCalled, Is.True);
        }

        [Test]
        public async Task ExecuteAsync_WhenTestFrameworkUnavailable_ShouldFailFastWithoutValidation()
        {
            // Verifies missing Unity Test Framework is distinct from a failed test suite.
            StubTestExecutionService executionService = new()
            {
                TestFrameworkAvailable = false
            };
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new()
            {
                TestMode = UnityCliLoopTestMode.PlayMode,
                SaveBeforeRun = true
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Status, Is.EqualTo(RunTestsExecutionStatus.ExecutionFailed));
            Assert.That(response.HasFailures, Is.False);
            Assert.That(response.NoTestsFound, Is.False);
            Assert.That(response.NoTestsFoundExplanation, Is.Empty);
            Assert.That(response.Message, Is.EqualTo(RunTestsResponse.TestFrameworkUnavailableMessage));
            Assert.That(response.TestCount, Is.EqualTo(0));
            Assert.That(executionService.WasCalled, Is.False);
            Assert.That(validationService.WasCalled, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_WhenNoTestsWereFound_ShouldExposeNoTestsFoundState()
        {
            // Verifies UseCase responses expose zero-discovery as its own machine-readable state.
            StubTestExecutionService executionService = new()
            {
                NextResult = new SerializableTestResult
                {
                    success = false,
                    status = RunTestsExecutionStatus.NoTestsFound,
                    hasFailures = false,
                    noTestsFound = true,
                    noTestsFoundExplanation = RunTestsResponse.NoTestsFoundExplanationText,
                    message = RunTestsResponse.NoTestsFoundMessage,
                    completedAt = "2026-01-01T00:00:00.0000000Z",
                    testCount = 0,
                    passedCount = 0,
                    failedCount = 0,
                    skippedCount = 0,
                    xmlPath = null
                }
            };
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new()
            {
                TestMode = UnityCliLoopTestMode.PlayMode,
                FilterType = TestFilterType.exact,
                FilterValue = "MissingTest"
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Status, Is.EqualTo(RunTestsExecutionStatus.NoTestsFound));
            Assert.That(response.HasFailures, Is.False);
            Assert.That(response.NoTestsFound, Is.True);
            Assert.That(response.NoTestsFoundExplanation, Does.Contain("not a test failure"));
            Assert.That(response.Message, Is.EqualTo(RunTestsResponse.NoTestsFoundMessage));
            Assert.That(response.TestCount, Is.EqualTo(0));
            Assert.That(response.FailedCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ExecuteAsync_WithUnsupportedFilterType_ShouldFailFastWithoutRunningTests()
        {
            // Verifies invalid filter-type enum values surface as a Success=false response instead of a JSON-RPC error.
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new()
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                FilterType = (TestFilterType)999,
                FilterValue = "value"
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Status, Is.EqualTo(RunTestsExecutionStatus.ExecutionFailed));
            Assert.That(response.Message, Does.Contain("Unsupported filter type"));
            Assert.That(executionService.WasCalled, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_AfterTestExecution_ShouldWaitForCleanup()
        {
            // Verifies run-tests waits for Unity Test Runner cleanup before responding.
            int cleanupWaitCount = 0;
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    cleanupWaitCount++;
                    return Task.CompletedTask;
                }
            );
            RunTestsSchema parameters = new();

            await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(cleanupWaitCount, Is.EqualTo(1));
            Assert.That(executionService.WasCalled, Is.True);
        }

        [Test]
        public async Task ExecuteAsync_WhenValidationFails_ShouldNotWaitForCleanup()
        {
            // Verifies fail-fast validation errors do not wait for cleanup that was never scheduled.
            int cleanupWaitCount = 0;
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(
                ValidationResult.Failure("EditMode tests cannot run during play mode. Use control-play-mode --action Stop to exit play mode, then rerun the tests."));
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    cleanupWaitCount++;
                    return Task.CompletedTask;
                }
            );
            RunTestsSchema parameters = new();

            await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(cleanupWaitCount, Is.EqualTo(0));
            Assert.That(executionService.WasCalled, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_WhenTestFrameworkUnavailable_ShouldNotWaitForCleanup()
        {
            // Verifies missing Test Framework returns before any Test Runner cleanup wait.
            int cleanupWaitCount = 0;
            StubTestExecutionService executionService = new()
            {
                TestFrameworkAvailable = false
            };
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    cleanupWaitCount++;
                    return Task.CompletedTask;
                }
            );
            RunTestsSchema parameters = new();

            await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(cleanupWaitCount, Is.EqualTo(0));
            Assert.That(executionService.WasCalled, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_AfterValidationPasses_ShouldClearActivePausePoints()
        {
            // Verifies pause points are cleared after validation succeeds but before test execution.
            bool clearCalled = false;
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                clearActivePausePoints: () =>
                {
                    clearCalled = true;
                    return new[] { "file.cs:10" };
                },
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new();

            await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(clearCalled, Is.True);
            Assert.That(executionService.WasCalled, Is.True);
        }

        [Test]
        public async Task ExecuteAsync_WhenValidationFails_ShouldNotClearPausePoints()
        {
            // Verifies rejected calls produce zero side effects on pause point state.
            bool clearCalled = false;
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(
                ValidationResult.Failure("EditMode tests cannot run during play mode. Use control-play-mode --action Stop to exit play mode, then rerun the tests."));
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                clearActivePausePoints: () =>
                {
                    clearCalled = true;
                    return new[] { "file.cs:10" };
                },
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new();

            await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(clearCalled, Is.False);
            Assert.That(executionService.WasCalled, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_WhenPausePointsCleared_ShouldReportClearedIdsInResponse()
        {
            // Verifies cleared pause point IDs appear in the response for agent transparency.
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                clearActivePausePoints: () => new[] { "Assets/Scripts/Foo.cs:42", "my-custom-marker" },
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new();

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.ClearedPausePointIds, Is.Not.Null);
            Assert.That(response.ClearedPausePointIds, Is.EqualTo(new[] { "Assets/Scripts/Foo.cs:42", "my-custom-marker" }));
        }

        [Test]
        public async Task ExecuteAsync_WhenNoPausePointsActive_ShouldNotReportClearedIds()
        {
            // Verifies ClearedPausePointIds stays null when no markers were armed.
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                clearActivePausePoints: () => null,
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new();

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.ClearedPausePointIds, Is.Null);
        }

        /// <summary>
        /// What: FailedTests from the execution result is copied onto the response.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenTestsFail_CopiesFailedTestDetailsOntoResponse()
        {
            SerializableTestResult.FailedTestDetail detail = new SerializableTestResult.FailedTestDetail
            {
                FullName = "Example.Tests.FailingTest",
                Message = "Expected 2 But was: 1",
                File = "Assets/Tests/FailingTest.cs",
                Line = 42
            };
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = new SerializableTestResult
                {
                    success = false,
                    status = RunTestsExecutionStatus.Failed,
                    hasFailures = true,
                    noTestsFound = false,
                    noTestsFoundExplanation = string.Empty,
                    message = "Test execution completed with status: Failed",
                    completedAt = "2026-01-01T00:00:00.0000000Z",
                    testCount = 1,
                    passedCount = 0,
                    failedCount = 1,
                    skippedCount = 0,
                    xmlPath = "TestResults/example.xml",
                    failedTests = new[] { detail }
                }
            };
            StubTestExecutionStateValidationService validationService =
                new StubTestExecutionStateValidationService(ValidationResult.Success());
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: NoCleanupWait);
            RunTestsSchema parameters = new RunTestsSchema();

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.FailedTests, Is.Not.Null);
            Assert.That(response.FailedTests.Length, Is.EqualTo(1));
            Assert.That(response.FailedTests[0].FullName, Is.EqualTo("Example.Tests.FailingTest"));
            Assert.That(response.FailedTests[0].Message, Is.EqualTo("Expected 2 But was: 1"));
            Assert.That(response.FailedTests[0].File, Is.EqualTo("Assets/Tests/FailingTest.cs"));
            Assert.That(response.FailedTests[0].Line, Is.EqualTo(42));
            Assert.That(
                response.Message,
                Is.EqualTo("Test execution completed with status: Failed"));
        }

        /// <summary>
        /// What: FailedCount above 10 appends the fixed-literal truncation note to Message.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenElevenTestsFail_AppendsTruncationNoteToMessage()
        {
            SerializableTestResult.FailedTestDetail[] details = new SerializableTestResult.FailedTestDetail[10];
            for (int index = 0; index < 10; index++)
            {
                details[index] = new SerializableTestResult.FailedTestDetail
                {
                    FullName = "Example.Tests.FailingTest" + index,
                    Message = "boom"
                };
            }

            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = new SerializableTestResult
                {
                    success = false,
                    status = RunTestsExecutionStatus.Failed,
                    hasFailures = true,
                    noTestsFound = false,
                    noTestsFoundExplanation = string.Empty,
                    message = "Test execution completed with status: Failed",
                    completedAt = "2026-01-01T00:00:00.0000000Z",
                    testCount = 11,
                    passedCount = 0,
                    failedCount = 11,
                    skippedCount = 0,
                    xmlPath = "TestResults/example.xml",
                    failedTests = details
                }
            };
            StubTestExecutionStateValidationService validationService =
                new StubTestExecutionStateValidationService(ValidationResult.Success());
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: NoCleanupWait);
            RunTestsSchema parameters = new RunTestsSchema();

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.FailedTests.Length, Is.EqualTo(10));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Test execution completed with status: Failed first 10 of 11 failures listed; see XmlPath for full results."));
        }

        /// <summary>
        /// What: FailedCount equal to 10 leaves Message as the untruncated Failed status.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenExactlyTenTestsFail_LeavesMessageWithoutTruncationNote()
        {
            SerializableTestResult.FailedTestDetail[] details = new SerializableTestResult.FailedTestDetail[10];
            for (int index = 0; index < 10; index++)
            {
                details[index] = new SerializableTestResult.FailedTestDetail
                {
                    FullName = "Example.Tests.FailingTest" + index,
                    Message = "boom"
                };
            }

            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = new SerializableTestResult
                {
                    success = false,
                    status = RunTestsExecutionStatus.Failed,
                    hasFailures = true,
                    noTestsFound = false,
                    noTestsFoundExplanation = string.Empty,
                    message = "Test execution completed with status: Failed",
                    completedAt = "2026-01-01T00:00:00.0000000Z",
                    testCount = 10,
                    passedCount = 0,
                    failedCount = 10,
                    skippedCount = 0,
                    xmlPath = "TestResults/example.xml",
                    failedTests = details
                }
            };
            StubTestExecutionStateValidationService validationService =
                new StubTestExecutionStateValidationService(ValidationResult.Success());
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: NoCleanupWait);
            RunTestsSchema parameters = new RunTestsSchema();

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.FailedTests.Length, Is.EqualTo(10));
            Assert.That(
                response.Message,
                Is.EqualTo("Test execution completed with status: Failed"));
        }

        /// <summary>
        /// What: a passing run leaves FailedTests null so JSON omits the field.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenNoTestsFail_LeavesFailedTestsNull()
        {
            StubTestExecutionService executionService = new StubTestExecutionService();
            StubTestExecutionStateValidationService validationService =
                new StubTestExecutionStateValidationService(ValidationResult.Success());
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: NoCleanupWait);
            RunTestsSchema parameters = new RunTestsSchema();

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.FailedTests, Is.Null);
        }

        [Test]
        public async Task ExecuteAsync_WithUnsupportedFilterType_ShouldNotClearPausePoints()
        {
            // Verifies filter creation failure does not silently clear active pause points.
            bool clearCalled = false;
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                clearActivePausePoints: () =>
                {
                    clearCalled = true;
                    return new[] { "file.cs:10" };
                },
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new()
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                FilterType = (TestFilterType)999,
                FilterValue = "value"
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(clearCalled, Is.False);
            Assert.That(response.Success, Is.False);
            Assert.That(response.ClearedPausePointIds, Is.Null);
        }

        /// <summary>
        /// What: after cleanup is proven off-thread, no-tests diagnostics still run on the main thread.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenCleanupResumesOffThread_RunsNoTestsDiagnosticsOnMainThread()
        {
            OffThreadCleanupResume cleanupResume = new OffThreadCleanupResume();
            RecordingNoTestsDiagnosticCapture diagnosticCapture = new RecordingNoTestsDiagnosticCapture();
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = new SerializableTestResult
                {
                    success = false,
                    status = RunTestsExecutionStatus.NoTestsFound,
                    hasFailures = false,
                    noTestsFound = true,
                    noTestsFoundExplanation = RunTestsResponse.NoTestsFoundExplanationText,
                    message = RunTestsResponse.NoTestsFoundMessage,
                    completedAt = "2026-01-01T00:00:00.0000000Z",
                    testCount = 0,
                    passedCount = 0,
                    failedCount = 0,
                    skippedCount = 0,
                    xmlPath = null
                }
            };
            StubTestExecutionStateValidationService validationService =
                new StubTestExecutionStateValidationService(ValidationResult.Success());
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                validationService,
                waitForTestRunnerCleanupAsync: cleanupResume.ResumeAsync,
                appendNoTestsDiagnostics: diagnosticCapture.Append);
            RunTestsSchema parameters = new RunTestsSchema
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                FilterType = TestFilterType.all
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.NoTestsFound, Is.True);
            Assert.That(cleanupResume.ObservedIsMainThread, Is.False);
            Assert.That(diagnosticCapture.AppendCalled, Is.True);
            Assert.That(diagnosticCapture.ObservedIsMainThread, Is.True);
        }

        private static Task NoCleanupWait(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class StubTestExecutionStateValidationService : TestExecutionStateValidationService
        {
            private readonly ValidationResult _result;

            public bool SaveBeforeRun { get; private set; }
            public bool WasCalled { get; private set; }

            public StubTestExecutionStateValidationService(ValidationResult result)
            {
                _result = result;
            }

            public override ValidationResult Validate(UnityCliLoopTestMode testMode, bool saveBeforeRun)
            {
                WasCalled = true;
                SaveBeforeRun = saveBeforeRun;
                return _result;
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class StubTestExecutionService : TestExecutionService
        {
            public bool TestFrameworkAvailable { get; set; } = true;
            public bool WasCalled { get; private set; }
            // Default stub result satisfies RunTestsResponse preconditions (non-null status
            // and noTestsFoundExplanation); individual tests override NextResult when they
            // need a specific execution status.
            public SerializableTestResult NextResult { get; set; } = new()
            {
                status = RunTestsExecutionStatus.Passed,
                noTestsFoundExplanation = string.Empty
            };

            public override bool IsTestFrameworkAvailable => TestFrameworkAvailable;

            public override Task<SerializableTestResult> ExecutePlayModeTestAsync(TestExecutionFilter filter, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                WasCalled = true;
                return Task.FromResult(NextResult);
            }

            public override Task<SerializableTestResult> ExecuteEditModeTestAsync(TestExecutionFilter filter, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                WasCalled = true;
                return Task.FromResult(NextResult);
            }
        }

        /// <summary>
        /// Test support type that resumes cleanup off-thread without Task.Run.
        /// </summary>
        private sealed class OffThreadCleanupResume
        {
            public bool ObservedIsMainThread { get; private set; }

            public async Task ResumeAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                // Why loop TimerDelay instead of Task.Run: EditMode guardrails
                // forbid thread-pool tests that can stall the runner. A single
                // Wait(1).ConfigureAwait(false) can complete before await and
                // continue inline on the main thread; retry until a suspend
                // actually resumes off-thread.
                while (MainThreadSwitcher.IsMainThread)
                {
                    await TimerDelay.Wait(1, ct).ConfigureAwait(false);
                }

                ObservedIsMainThread = MainThreadSwitcher.IsMainThread;
            }
        }

        private sealed class RecordingNoTestsDiagnosticCapture
        {
            public bool AppendCalled { get; private set; }
            public bool ObservedIsMainThread { get; private set; }

            public string Append(
                string message,
                bool noTestsFound,
                UnityCliLoopTestMode testMode,
                TestFilterType filterType)
            {
                AppendCalled = true;
                ObservedIsMainThread = MainThreadSwitcher.IsMainThread;
                return message;
            }
        }
    }
}
