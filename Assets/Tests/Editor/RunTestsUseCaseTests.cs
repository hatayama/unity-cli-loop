using System;
using System.Collections.Generic;
using System.IO;
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
        private const string RecordedCompletedAt = "2026-01-02T03:04:05.0000000Z";

        // Why a temporary store for every use case: ExecuteAsync rewrites the last-run record, and the
        // project's own record must not be replaced by stub results while uloop runs these tests.
        private string _recordDirectory;
        private RunTestsLastRunRecordStore _recordStore;

        [SetUp]
        public void SetUp()
        {
            _recordDirectory = Path.Combine(Path.GetTempPath(), "uloop-last-run-" + Guid.NewGuid().ToString("N"));
            _recordStore = new RunTestsLastRunRecordStore(_recordDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_recordDirectory))
            {
                Directory.Delete(_recordDirectory, true);
            }
        }

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
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new()
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                UnsavedChanges = RunTestsUnsavedChangesMode.save
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
            Assert.That(validationService.UnsavedChanges, Is.EqualTo(RunTestsUnsavedChangesMode.save));
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
                _recordStore,
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
                _recordStore,
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
                _recordStore,
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

        /// <summary>
        /// Verifies PlayMode respect options and request id are forwarded to the execution stub.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenPlayModeRespectsEnterPlayModeSettings_ForwardsOptionsToStub()
        {
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new()
            {
                TestMode = UnityCliLoopTestMode.PlayMode,
                RespectEnterPlayModeSettings = true,
                RequestId = "run_tests_x"
            };

            await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(executionService.PlayModeWasCalled, Is.True);
            Assert.That(executionService.LastRespectEnterPlayModeSettings, Is.True);
            Assert.That(executionService.LastRequestId, Is.EqualTo("run_tests_x"));
        }

        /// <summary>
        /// Verifies EditMode ignores respect settings and does not call the PlayMode stub.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenEditModeRespectsEnterPlayModeSettings_DoesNotCallPlayModeStub()
        {
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new()
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                RespectEnterPlayModeSettings = true,
                RequestId = "run_tests_x"
            };

            await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(executionService.PlayModeWasCalled, Is.False);
            Assert.That(executionService.WasCalled, Is.True);
            Assert.That(executionService.LastRequestId, Is.Null);
        }

        [Test]
        public async Task ExecuteAsync_WithDefaultRequest_ShouldUseSaveUnsavedChangesMode()
        {
            // Verifies default use-case requests preserve the run-tests auto-save behavior.
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(ValidationResult.Success());
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new();

            await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(validationService.WasCalled, Is.True);
            Assert.That(validationService.UnsavedChanges, Is.EqualTo(RunTestsUnsavedChangesMode.save));
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
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait
            );
            RunTestsSchema parameters = new()
            {
                TestMode = UnityCliLoopTestMode.PlayMode,
                UnsavedChanges = RunTestsUnsavedChangesMode.save
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
                _recordStore,
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
            Assert.That(response.NoTestsFoundExplanation, Does.Contain("missing test assembly"));
            Assert.That(response.Message, Is.EqualTo(RunTestsResponse.NoTestsFoundMessage));
            Assert.That(response.TestCount, Is.EqualTo(0));
            Assert.That(response.FailedCount, Is.EqualTo(0));
            Assert.That(response.ShouldSerializeFilterType(), Is.EqualTo(false));
            Assert.That(response.ShouldSerializeFilterValue(), Is.EqualTo(false));
            Assert.That(response.ShouldSerializeUnfilteredTestNames(), Is.EqualTo(false));
            Assert.That(response.ShouldSerializeUnfilteredTestCount(), Is.EqualTo(false));
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
                _recordStore,
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
                _recordStore,
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
                _recordStore,
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
                _recordStore,
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
                _recordStore,
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
                _recordStore,
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
                _recordStore,
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
                _recordStore,
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
                _recordStore,
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
        /// What: skipped full names from the execution result are copied onto the response.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenTestsAreSkipped_CopiesSkippedTestFullNamesOntoResponse()
        {
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = new SerializableTestResult
                {
                    success = true,
                    status = RunTestsExecutionStatus.Passed,
                    hasFailures = false,
                    noTestsFound = false,
                    noTestsFoundExplanation = string.Empty,
                    message = "Test execution completed with status: Passed",
                    completedAt = "2026-01-01T00:00:00.0000000Z",
                    testCount = 2,
                    passedCount = 1,
                    failedCount = 0,
                    skippedCount = 1,
                    xmlPath = null,
                    skippedTests = new[] { "Example.Tests.SkippedTest" }
                }
            };
            StubTestExecutionStateValidationService validationService =
                new StubTestExecutionStateValidationService(ValidationResult.Success());
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                validationService,
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait);
            RunTestsSchema parameters = new RunTestsSchema();

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.SkippedTests, Is.EqualTo(new[] { "Example.Tests.SkippedTest" }));
        }

        /// <summary>
        /// What: an execution result without skipped leaves leaves SkippedTests null on the response.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenNoTestsAreSkipped_LeavesSkippedTestsNull()
        {
            StubTestExecutionService executionService = new StubTestExecutionService();
            StubTestExecutionStateValidationService validationService =
                new StubTestExecutionStateValidationService(ValidationResult.Success());
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                validationService,
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait);
            RunTestsSchema parameters = new RunTestsSchema();

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.SkippedTests, Is.Null);
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
                _recordStore,
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
                _recordStore,
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
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait);
            RunTestsSchema parameters = new RunTestsSchema();

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.FailedTests, Is.Null);
        }

        /// <summary>
        /// What: live hot-reload changes at test-run start copy the exact policy-form Warning onto the response.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenHotReloadChangesAreLive_AssignsExactPolicyFormWarning()
        {
            StubTestExecutionService executionService = new StubTestExecutionService();
            StubTestExecutionStateValidationService validationService =
                new StubTestExecutionStateValidationService(ValidationResult.Success());
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                validationService,
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait,
                getActiveHotReloadChangeCount: () => 2);
            RunTestsSchema parameters = new RunTestsSchema();

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(
                response.Warning,
                Is.EqualTo(
                    "2 active hot-reload change(s) were live during this test run. If script changes were imported during the run, the deferred domain reload that follows it discards every active hot-reload change, including introduced types - check 'uloop hot-reload --status' and re-apply, or run 'uloop compile' to bake them in."));
        }

        /// <summary>
        /// What: a test run with no live hot-reload changes leaves Warning empty.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenNoHotReloadChangesAreLive_LeavesWarningEmpty()
        {
            StubTestExecutionService executionService = new StubTestExecutionService();
            StubTestExecutionStateValidationService validationService =
                new StubTestExecutionStateValidationService(ValidationResult.Success());
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                validationService,
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait,
                getActiveHotReloadChangeCount: () => 0);
            RunTestsSchema parameters = new RunTestsSchema();

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: fail-fast validation does not query the live hot-reload change count.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenValidationFails_DoesNotQueryHotReloadChangeCount()
        {
            bool getterCalled = false;
            StubTestExecutionService executionService = new StubTestExecutionService();
            StubTestExecutionStateValidationService validationService =
                new StubTestExecutionStateValidationService(
                    ValidationResult.Failure("EditMode tests cannot run during play mode"));
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                validationService,
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait,
                getActiveHotReloadChangeCount: () =>
                {
                    getterCalled = true;
                    return 2;
                });
            RunTestsSchema parameters = new RunTestsSchema();

            await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(getterCalled, Is.False);
        }

        /// <summary>
        /// What: the default getter path reads HotReloadRuntimeChangeCoordination.GetActiveRuntimeChangeCount
        /// into the exact policy-form Warning, so a run reports the introduced types a deferred
        /// domain reload discards as well as the patches (stubs keep this from nesting Test Runner).
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenDefaultCoordinationGetterReturnsThree_AssignsExactPolicyFormWarning()
        {
            Func<int> originalGetter = HotReloadRuntimeChangeCoordination.GetActiveRuntimeChangeCount;
            HotReloadRuntimeChangeCoordination.GetActiveRuntimeChangeCount = () => 3;
            try
            {
                StubTestExecutionService executionService = new StubTestExecutionService();
                StubTestExecutionStateValidationService validationService =
                    new StubTestExecutionStateValidationService(ValidationResult.Success());
                RunTestsUseCase useCase = new RunTestsUseCase(
                    new TestFilterCreationService(),
                    executionService,
                    validationService,
                    _recordStore,
                    waitForTestRunnerCleanupAsync: NoCleanupWait);
                RunTestsSchema parameters = new RunTestsSchema();

                RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

                Assert.That(
                    response.Warning,
                    Is.EqualTo(
                        "3 active hot-reload change(s) were live during this test run. If script changes were imported during the run, the deferred domain reload that follows it discards every active hot-reload change, including introduced types - check 'uloop hot-reload --status' and re-apply, or run 'uloop compile' to bake them in."));
            }
            finally
            {
                HotReloadRuntimeChangeCoordination.GetActiveRuntimeChangeCount = originalGetter;
            }
        }

        /// <summary>
        /// What: a run that times out after starting still reports the hot-reload discard Warning,
        /// so a failed run explains the patches it may have cost.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenRunTimesOutWithLiveHotReloadChanges_AssignsExactPolicyFormWarning()
        {
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                ThrowsExecutionTimeout = true
            };
            StubTestExecutionStateValidationService validationService =
                new StubTestExecutionStateValidationService(ValidationResult.Success());
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                validationService,
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait,
                getActiveHotReloadChangeCount: () => 2);
            RunTestsSchema parameters = new RunTestsSchema();

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(
                response.Warning,
                Is.EqualTo(
                    "2 active hot-reload change(s) were live during this test run. If script changes were imported during the run, the deferred domain reload that follows it discards every active hot-reload change, including introduced types - check 'uloop hot-reload --status' and re-apply, or run 'uloop compile' to bake them in."));
        }

        /// <summary>
        /// What: a run that times out with no live hot-reload changes leaves Warning empty.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenRunTimesOutWithoutLiveHotReloadChanges_LeavesWarningEmpty()
        {
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                ThrowsExecutionTimeout = true
            };
            StubTestExecutionStateValidationService validationService =
                new StubTestExecutionStateValidationService(ValidationResult.Success());
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                validationService,
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait,
                getActiveHotReloadChangeCount: () => 0);
            RunTestsSchema parameters = new RunTestsSchema();

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Warning, Is.Null.Or.Empty);
        }

        /// <summary>
        /// What: a pre-run validation failure never carries the discard Warning, because no test
        /// run started and therefore no domain reload could have discarded a patch.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenValidationFails_LeavesWarningEmpty()
        {
            StubTestExecutionService executionService = new StubTestExecutionService();
            StubTestExecutionStateValidationService validationService =
                new StubTestExecutionStateValidationService(
                    ValidationResult.Failure("EditMode tests cannot run during play mode"));
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                validationService,
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait,
                getActiveHotReloadChangeCount: () => 2);
            RunTestsSchema parameters = new RunTestsSchema();

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Warning, Is.Null.Or.Empty);
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
                _recordStore,
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
                _recordStore,
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

        /// <summary>
        /// What: a filtered NoTestsFound run echoes the filter and stubbed unfiltered names on the response.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilteredNoTestsFound_EchoesUnfilteredNamesAndAppendsMessage()
        {
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateNoTestsFoundResult(),
                UnfilteredTestListResult = RunTestsUnfilteredTestListResult.Success(
                    new[]
                    {
                        "Example.Tests.Alpha",
                        "Example.Tests.Beta"
                    })
            };
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                new StubTestExecutionStateValidationService(ValidationResult.Success()),
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait);
            RunTestsSchema parameters = new RunTestsSchema
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                FilterType = TestFilterType.exact,
                FilterValue = "Missing.Test"
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.NoTestsFound, Is.EqualTo(true));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "No tests found matching the specified filter criteria. No tests matched FilterType 'exact' with FilterValue 'Missing.Test'. 2 test(s) exist in this TestMode without the filter; compare UnfilteredTestNames against the filter value."));
            Assert.That(response.FilterType, Is.EqualTo("exact"));
            Assert.That(response.FilterValue, Is.EqualTo("Missing.Test"));
            Assert.That(response.UnfilteredTestCount, Is.EqualTo(2));
            Assert.That(
                response.UnfilteredTestNames,
                Is.EqualTo(new List<string> { "Example.Tests.Alpha", "Example.Tests.Beta" }));
            Assert.That(response.ShouldSerializeFilterType(), Is.EqualTo(true));
            Assert.That(response.ShouldSerializeFilterValue(), Is.EqualTo(true));
            Assert.That(response.ShouldSerializeUnfilteredTestNames(), Is.EqualTo(true));
            Assert.That(response.ShouldSerializeUnfilteredTestCount(), Is.EqualTo(true));
        }

        /// <summary>
        /// What: filter-all NoTestsFound keeps the original message and omits unfiltered echo fields.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilterAllNoTestsFound_OmitsUnfilteredEchoFields()
        {
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateNoTestsFoundResult(),
                UnfilteredTestListResult = RunTestsUnfilteredTestListResult.Success(
                    new[] { "Example.Tests.Alpha" })
            };
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                new StubTestExecutionStateValidationService(ValidationResult.Success()),
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait,
                appendNoTestsDiagnostics: PassThroughNoTestsDiagnostics);
            RunTestsSchema parameters = new RunTestsSchema
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                FilterType = TestFilterType.all
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Message, Is.EqualTo(RunTestsResponse.NoTestsFoundMessage));
            Assert.That(response.ShouldSerializeFilterType(), Is.EqualTo(false));
            Assert.That(response.ShouldSerializeFilterValue(), Is.EqualTo(false));
            Assert.That(response.ShouldSerializeUnfilteredTestNames(), Is.EqualTo(false));
            Assert.That(response.ShouldSerializeUnfilteredTestCount(), Is.EqualTo(false));
            Assert.That(response.UnfilteredTestNames, Is.Null);
        }

        /// <summary>
        /// What: filter-all NoTestsFound attaches the proposed test .asmdef to the response and names it in Message.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilterAllNoTestsFoundWithoutTestAsmdef_AttachesProposedTestAsmdef()
        {
            RunTestsTestAsmdefProposal proposal = new RunTestsTestAsmdefProposal("Assets/Tests/Editor/Game.Tests.Editor.asmdef", "{}");
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateNoTestsFoundResult()
            };
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                new StubTestExecutionStateValidationService(ValidationResult.Success()),
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait,
                appendNoTestsDiagnostics: PassThroughNoTestsDiagnostics,
                proposeTestAsmdef: _ => proposal);
            RunTestsSchema parameters = new RunTestsSchema
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                FilterType = TestFilterType.all
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.ProposedTestAsmdef, Is.SameAs(proposal));
            Assert.That(response.ShouldSerializeProposedTestAsmdef(), Is.True);
            Assert.That(response.Message, Does.StartWith(RunTestsResponse.NoTestsFoundMessage + ". "));
            Assert.That(response.Message, Does.Contain("Assets/Tests/Editor/Game.Tests.Editor.asmdef"));
        }

        /// <summary>
        /// What: an unfiltered NoTestsFound run whose project already has a test assembly leaves the response untouched.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilterAllNoTestsFoundWithTestAsmdef_LeavesResponseWithoutProposal()
        {
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateNoTestsFoundResult()
            };
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                new StubTestExecutionStateValidationService(ValidationResult.Success()),
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait,
                appendNoTestsDiagnostics: PassThroughNoTestsDiagnostics,
                proposeTestAsmdef: _ => null);
            RunTestsSchema parameters = new RunTestsSchema
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                FilterType = TestFilterType.all
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.ProposedTestAsmdef, Is.Null);
            Assert.That(response.ShouldSerializeProposedTestAsmdef(), Is.False);
            Assert.That(response.Message, Is.EqualTo(RunTestsResponse.NoTestsFoundMessage));
        }

        /// <summary>
        /// What: a filtered NoTestsFound run never asks for a test .asmdef proposal, because the filter is the likelier cause.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilteredNoTestsFound_DoesNotProposeTestAsmdef()
        {
            bool proposalRequested = false;
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateNoTestsFoundResult(),
                UnfilteredTestListResult = RunTestsUnfilteredTestListResult.Success(new[] { "Example.Tests.Alpha" })
            };
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                new StubTestExecutionStateValidationService(ValidationResult.Success()),
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait,
                proposeTestAsmdef: _ =>
                {
                    proposalRequested = true;
                    return null;
                });
            RunTestsSchema parameters = new RunTestsSchema
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                FilterType = TestFilterType.exact,
                FilterValue = "Missing.Test"
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(proposalRequested, Is.False);
            Assert.That(response.ProposedTestAsmdef, Is.Null);
            Assert.That(response.ShouldSerializeProposedTestAsmdef(), Is.False);
        }

        /// <summary>
        /// What: filter-all NoTestsFound appends the predefined-assembly notice after the original message when findings exist.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilterAllNoTestsFoundWithPredefinedAssemblyTests_AppendsNotice()
        {
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateNoTestsFoundResult(),
                PredefinedAssemblyTestFindings = RunTestsPredefinedAssemblyTestFindings.Create(
                    2,
                    new[]
                    {
                        "Assembly-CSharp: Game.Foo.Alpha",
                        "Assembly-CSharp-Editor: Editor.Bar.Beta"
                    })
            };
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                new StubTestExecutionStateValidationService(ValidationResult.Success()),
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait,
                appendNoTestsDiagnostics: PassThroughNoTestsDiagnostics);
            RunTestsSchema parameters = new RunTestsSchema
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                FilterType = TestFilterType.all
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(
                response.Message,
                Is.EqualTo(
                    "No tests found matching the specified filter criteria. Additionally, 2 NUnit test method(s) are compiled into predefined assemblies rather than any test assembly, so this run could not discover them: Assembly-CSharp: Game.Foo.Alpha, Assembly-CSharp-Editor: Editor.Bar.Beta. Move these scripts into a folder whose .asmdef has Test Assemblies enabled (EditMode tests target the Editor platform only), reference the assemblies under test, then run 'uloop compile' and rerun the tests."));
        }

        /// <summary>
        /// What: filter-all NoTestsFound appends the predefined-assembly notice after a period-terminated asmdef hint without a second period.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilterAllNoTestsFoundWithAsmdefHintAndPredefinedAssemblyTests_AppendsNoticeAfterPeriod()
        {
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateNoTestsFoundResult(),
                PredefinedAssemblyTestFindings = RunTestsPredefinedAssemblyTestFindings.Create(
                    1,
                    new[] { "Assembly-CSharp: Game.Foo.Alpha" })
            };
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                new StubTestExecutionStateValidationService(ValidationResult.Success()),
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait,
                appendNoTestsDiagnostics: AppendPeriodTerminatedAsmdefHint);
            RunTestsSchema parameters = new RunTestsSchema
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                FilterType = TestFilterType.all
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(
                response.Message,
                Is.EqualTo(
                    "No tests found matching the specified filter criteria Possible asmdef issues: Assets/Tests/EditMode/Sample.Tests.asmdef: sample finding. Additionally, 1 NUnit test method(s) are compiled into predefined assemblies rather than any test assembly, so this run could not discover them: Assembly-CSharp: Game.Foo.Alpha. Move these scripts into a folder whose .asmdef has Test Assemblies enabled (EditMode tests target the Editor platform only), reference the assemblies under test, then run 'uloop compile' and rerun the tests."));
        }

        /// <summary>
        /// What: filter-all NoTestsFound keeps the original message when predefined-assembly findings are empty.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenFilterAllNoTestsFoundWithNoPredefinedAssemblyTests_KeepsOriginalMessage()
        {
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateNoTestsFoundResult(),
                PredefinedAssemblyTestFindings = RunTestsPredefinedAssemblyTestFindings.None()
            };
            RunTestsUseCase useCase = new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                new StubTestExecutionStateValidationService(ValidationResult.Success()),
                _recordStore,
                waitForTestRunnerCleanupAsync: NoCleanupWait,
                appendNoTestsDiagnostics: PassThroughNoTestsDiagnostics);
            RunTestsSchema parameters = new RunTestsSchema
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                FilterType = TestFilterType.all
            };

            RunTestsResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Message, Is.EqualTo(RunTestsResponse.NoTestsFoundMessage));
        }

        private static SerializableTestResult CreateNoTestsFoundResult()
        {
            return new SerializableTestResult
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
            };
        }

        private static string AppendPeriodTerminatedAsmdefHint(
            string message,
            bool noTestsFound,
            UnityCliLoopTestMode testMode,
            TestFilterType filterType)
        {
            return message + " Possible asmdef issues: Assets/Tests/EditMode/Sample.Tests.asmdef: sample finding.";
        }

        private static string PassThroughNoTestsDiagnostics(
            string message,
            bool noTestsFound,
            UnityCliLoopTestMode testMode,
            TestFilterType filterType)
        {
            return message;
        }

        /// <summary>
        /// What: a completed run replaces the old record of its test mode with this run's failures.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenRunCompletes_RecordsFailedTestsForTestMode()
        {
            SeedRecord(UnityCliLoopTestMode.EditMode, new[] { "Ns.Old.StaleFailure" });
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateCompletedRunResult(RunTestsExecutionStatus.Failed, 3, 2, "Ns.C.FailA", "Ns.C.FailB")
            };
            RunTestsUseCase useCase = CreateRecordingUseCase(executionService);

            await ExecuteToCompletionAsync(useCase, new RunTestsSchema());

            AssertRecord(UnityCliLoopTestMode.EditMode, RunCompletedAt, "Ns.C.FailA", "Ns.C.FailB");
        }

        /// <summary>
        /// What: a run that times out leaves no record, so the next --rerun-failed cannot rerun stale failures.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenRunTimesOut_LeavesNoRecord()
        {
            SeedRecord(UnityCliLoopTestMode.EditMode, new[] { "Ns.Old.StaleFailure" });
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                ThrowsExecutionTimeout = true
            };
            RunTestsUseCase useCase = CreateRecordingUseCase(executionService);

            RunTestsResponse response = await ExecuteToCompletionAsync(useCase, new RunTestsSchema());

            Assert.That(response.Success, Is.False);
            AssertNoRecord(UnityCliLoopTestMode.EditMode);
        }

        /// <summary>
        /// What: a request cancelled before it starts leaves the existing record as it was.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenCancelledBeforeStart_LeavesRecordUntouched()
        {
            SeedRecord(UnityCliLoopTestMode.EditMode, new[] { "Ns.Old.StaleFailure" });
            StubTestExecutionService executionService = new StubTestExecutionService();
            RunTestsUseCase useCase = CreateRecordingUseCase(executionService);
            using CancellationTokenSource parent = new CancellationTokenSource();
            parent.Cancel();

            bool canceled = await ExecuteExpectingCancellationAsync(useCase, new RunTestsSchema(), parent.Token);

            Assert.That(canceled, Is.True);
            Assert.That(executionService.WasCalled, Is.False);
            AssertRecord(UnityCliLoopTestMode.EditMode, RecordedCompletedAt, "Ns.Old.StaleFailure");
        }

        /// <summary>
        /// What: a request cancelled while the tests run leaves no record.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenParentCancelsDuringRun_LeavesNoRecord()
        {
            SeedRecord(UnityCliLoopTestMode.EditMode, new[] { "Ns.Old.StaleFailure" });
            using CancellationTokenSource parent = new CancellationTokenSource();
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                OnExecuteStarted = () => parent.Cancel()
            };
            RunTestsUseCase useCase = CreateRecordingUseCase(executionService);

            bool canceled = await ExecuteExpectingCancellationAsync(useCase, new RunTestsSchema(), parent.Token);

            Assert.That(canceled, Is.True);
            AssertNoRecord(UnityCliLoopTestMode.EditMode);
        }

        /// <summary>
        /// What: a request cancelled during the cleanup wait after the run finished keeps that run's record.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenParentCancelsDuringCleanupWait_KeepsRecordOfCompletedRun()
        {
            SeedRecord(UnityCliLoopTestMode.EditMode, new[] { "Ns.Old.StaleFailure" });
            using CancellationTokenSource parent = new CancellationTokenSource();
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateCompletedRunResult(RunTestsExecutionStatus.Failed, 2, 1, "Ns.C.FailA")
            };
            RunTestsUseCase useCase = CreateRecordingUseCase(
                executionService,
                waitForTestRunnerCleanupAsync: ct =>
                {
                    parent.Cancel();
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                });

            bool canceled = await ExecuteExpectingCancellationAsync(useCase, new RunTestsSchema(), parent.Token);

            Assert.That(canceled, Is.True);
            AssertRecord(UnityCliLoopTestMode.EditMode, RunCompletedAt, "Ns.C.FailA");
        }

        /// <summary>
        /// What: a record that cannot be removed stops the request before it runs tests or clears pause points.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenRecordCannotBeCleared_DoesNotRun()
        {
            OccupyRecordPathWithDirectory(UnityCliLoopTestMode.EditMode);
            bool pausePointsCleared = false;
            StubTestExecutionService executionService = new StubTestExecutionService();
            RunTestsUseCase useCase = CreateRecordingUseCase(
                executionService,
                clearActivePausePoints: () =>
                {
                    pausePointsCleared = true;
                    return new[] { "pause-point-1" };
                });

            RunTestsResponse response = await ExecuteToCompletionAsync(useCase, new RunTestsSchema());

            Assert.That(response.Success, Is.False);
            Assert.That(response.Status, Is.EqualTo(RunTestsExecutionStatus.ExecutionFailed));
            Assert.That(response.Message, Does.Contain(_recordStore.GetRecordPath(UnityCliLoopTestMode.EditMode)));
            Assert.That(executionService.WasCalled, Is.False);
            Assert.That(pausePointsCleared, Is.False);
            Assert.That(response.ClearedPausePointIds, Is.Null);
        }

        /// <summary>
        /// What: a completed run writes only its own test mode's record, empty when nothing failed.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenRunCompletes_LeavesOtherTestModeRecordUntouched()
        {
            SeedRecord(UnityCliLoopTestMode.PlayMode, new[] { "Ns.Play.Failure" });
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateCompletedRunResult(RunTestsExecutionStatus.Passed, 1, 0)
            };
            RunTestsUseCase useCase = CreateRecordingUseCase(executionService);
            RunTestsSchema parameters = new RunTestsSchema
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                FilterType = TestFilterType.@class,
                FilterValue = "SomeTests"
            };

            await ExecuteToCompletionAsync(useCase, parameters);

            AssertRecord(UnityCliLoopTestMode.EditMode, RunCompletedAt);
            AssertRecord(UnityCliLoopTestMode.PlayMode, RecordedCompletedAt, "Ns.Play.Failure");
        }

        /// <summary>
        /// What: a run that produced no result tree fails without writing a record.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenRunProducesNoResult_LeavesNoRecord()
        {
            SeedRecord(UnityCliLoopTestMode.EditMode, new[] { "Ns.Old.StaleFailure" });
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = new SerializableTestResult
                {
                    success = false,
                    status = RunTestsExecutionStatus.ExecutionFailed,
                    noTestsFoundExplanation = string.Empty,
                    message = "Test execution failed: no test result was produced",
                    completedAt = RunCompletedAt
                }
            };
            RunTestsUseCase useCase = CreateRecordingUseCase(executionService);

            RunTestsResponse response = await ExecuteToCompletionAsync(useCase, new RunTestsSchema());

            Assert.That(response.Success, Is.False);
            AssertNoRecord(UnityCliLoopTestMode.EditMode);
        }

        /// <summary>
        /// What: a completed run whose filter matched no tests records an empty target list.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenRunFindsNoTests_RecordsEmptyTargets()
        {
            SeedRecord(UnityCliLoopTestMode.EditMode, new[] { "Ns.Old.StaleFailure" });
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateNoTestsFoundResult()
            };
            RunTestsUseCase useCase = CreateRecordingUseCase(executionService);
            RunTestsSchema parameters = new RunTestsSchema
            {
                FilterType = TestFilterType.@class,
                FilterValue = "NoSuchTests"
            };

            RunTestsResponse response = await ExecuteToCompletionAsync(useCase, parameters);

            Assert.That(response.NoTestsFound, Is.True);
            AssertRecord(UnityCliLoopTestMode.EditMode, "2026-01-01T00:00:00.0000000Z");
        }

        /// <summary>
        /// What: --rerun-failed with a filter type is rejected without running or touching the record.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_RerunFailedWithFilterType_ReturnsConflictWithoutRunning()
        {
            await AssertRerunFilterConflictAsync(TestFilterType.@class, "SomeTests");
        }

        /// <summary>
        /// What: --rerun-failed with a filter value is rejected without running or touching the record.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_RerunFailedWithFilterValue_ReturnsConflictWithoutRunning()
        {
            await AssertRerunFilterConflictAsync(TestFilterType.all, "Ns.C.M");
        }

        /// <summary>
        /// What: a whitespace-only filter value counts as a filter value and is rejected with --rerun-failed.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_RerunFailedWithWhitespaceFilterValue_ReturnsConflictWithoutRunning()
        {
            await AssertRerunFilterConflictAsync(TestFilterType.all, "  ");
        }

        /// <summary>
        /// What: a null filter value counts as no filter value, so --rerun-failed runs the recorded tests.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_RerunFailedWithNullFilterValue_RunsRecordedTests()
        {
            SeedRecord(UnityCliLoopTestMode.EditMode, new[] { "Ns.C.FirstFailure", "Ns.C.SecondFailure" });
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateCompletedRunResult(RunTestsExecutionStatus.Failed, 2, 1, "Ns.C.SecondFailure")
            };
            RunTestsUseCase useCase = CreateRecordingUseCase(executionService);
            RunTestsSchema parameters = new RunTestsSchema
            {
                RerunFailed = true,
                FilterValue = null
            };

            RunTestsResponse response = await ExecuteToCompletionAsync(useCase, parameters);

            Assert.That(executionService.ExecuteCallCount, Is.EqualTo(1));
            Assert.That(executionService.LastFilter.FilterType, Is.EqualTo(TestExecutionFilterType.TestNames));
            Assert.That(response.RerunTargetCount, Is.EqualTo(2));
        }

        /// <summary>
        /// What: --rerun-failed without a record of the test mode fails without running.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_RerunFailedWithoutRecord_ReturnsFailureWithoutRunning()
        {
            StubTestExecutionService executionService = new StubTestExecutionService();
            RunTestsUseCase useCase = CreateRecordingUseCase(executionService);

            RunTestsResponse response = await ExecuteToCompletionAsync(
                useCase,
                new RunTestsSchema { RerunFailed = true });

            Assert.That(response.Success, Is.False);
            Assert.That(response.Status, Is.EqualTo(RunTestsExecutionStatus.ExecutionFailed));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "No completed EditMode run is recorded for this project. Run uloop run-tests without --rerun-failed first."));
            Assert.That(executionService.WasCalled, Is.False);
        }

        /// <summary>
        /// What: --rerun-failed with an unreadable record fails without running and leaves the file as it was.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_RerunFailedWithUnreadableRecord_ReturnsFailureWithoutRunning()
        {
            const string invalidRecord = "{ \"FormatVersion\": 1, ";
            Directory.CreateDirectory(_recordDirectory);
            string recordPath = _recordStore.GetRecordPath(UnityCliLoopTestMode.EditMode);
            File.WriteAllText(recordPath, invalidRecord);
            StubTestExecutionService executionService = new StubTestExecutionService();
            RunTestsUseCase useCase = CreateRecordingUseCase(executionService);

            RunTestsResponse response = await ExecuteToCompletionAsync(
                useCase,
                new RunTestsSchema { RerunFailed = true });

            Assert.That(response.Success, Is.False);
            Assert.That(response.Status, Is.EqualTo(RunTestsExecutionStatus.ExecutionFailed));
            Assert.That(response.Message, Does.StartWith("The recorded EditMode run could not be read (invalid JSON"));
            Assert.That(response.Message, Does.EndWith("Run uloop run-tests without --rerun-failed."));
            Assert.That(executionService.WasCalled, Is.False);
            Assert.That(File.ReadAllText(recordPath), Is.EqualTo(invalidRecord));
        }

        /// <summary>
        /// What: --rerun-failed with a record of no failures succeeds as NothingToRerun without running,
        /// clearing pause points, or touching the record.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_RerunFailedWithEmptyRecord_ReturnsNothingToRerun()
        {
            SeedRecord(UnityCliLoopTestMode.EditMode, Array.Empty<string>());
            bool pausePointsCleared = false;
            StubTestExecutionService executionService = new StubTestExecutionService();
            RunTestsUseCase useCase = CreateRecordingUseCase(
                executionService,
                clearActivePausePoints: () =>
                {
                    pausePointsCleared = true;
                    return null;
                });

            RunTestsResponse response = await ExecuteToCompletionAsync(
                useCase,
                new RunTestsSchema { RerunFailed = true });

            Assert.That(response.Success, Is.True);
            Assert.That(response.Status, Is.EqualTo(RunTestsExecutionStatus.NothingToRerun));
            Assert.That(response.HasFailures, Is.False);
            Assert.That(response.NoTestsFound, Is.False);
            Assert.That(response.TestCount, Is.EqualTo(0));
            Assert.That(response.RerunTargetCount, Is.EqualTo(0));
            Assert.That(response.RerunSourceCompletedAt, Is.EqualTo(RecordedCompletedAt));
            Assert.That(executionService.WasCalled, Is.False);
            Assert.That(pausePointsCleared, Is.False);
            AssertRecord(UnityCliLoopTestMode.EditMode, RecordedCompletedAt);
        }

        /// <summary>
        /// What: --rerun-failed runs the recorded tests once by name, reports the rerun, and records the new failures.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_RerunFailedWithRecord_RunsRecordedTestsAndRewritesRecord()
        {
            string[] recordedTargets = { "Ns.C.FirstFailure", "Ns.C.SecondFailure" };
            SeedRecord(UnityCliLoopTestMode.EditMode, recordedTargets);
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateCompletedRunResult(RunTestsExecutionStatus.Failed, 2, 1, "Ns.C.SecondFailure")
            };
            RunTestsUseCase useCase = CreateRecordingUseCase(executionService);

            RunTestsResponse response = await ExecuteToCompletionAsync(
                useCase,
                new RunTestsSchema { RerunFailed = true });

            Assert.That(executionService.ExecuteCallCount, Is.EqualTo(1));
            Assert.That(executionService.LastFilter.FilterType, Is.EqualTo(TestExecutionFilterType.TestNames));
            Assert.That(executionService.LastFilter.FilterValues, Is.EqualTo(recordedTargets));
            Assert.That(response.FailedCount, Is.EqualTo(1));
            Assert.That(response.RerunTargetCount, Is.EqualTo(2));
            Assert.That(response.RerunSourceCompletedAt, Is.EqualTo(RecordedCompletedAt));
            AssertRecord(UnityCliLoopTestMode.EditMode, RunCompletedAt, "Ns.C.SecondFailure");
        }

        /// <summary>
        /// What: a rerun whose recorded tests no longer exist says so instead of giving the no-test-assembly advice.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_RerunFailedWhenRecordedTestsAreGone_ReportsRenamedOrRemoved()
        {
            SeedRecord(UnityCliLoopTestMode.EditMode, new[] { "Ns.C.FirstFailure", "Ns.C.SecondFailure" });
            bool asmdefProposed = false;
            RecordingNoTestsDiagnosticCapture diagnosticCapture = new RecordingNoTestsDiagnosticCapture();
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                NextResult = CreateNoTestsFoundResult(),
                PredefinedAssemblyTestFindings = RunTestsPredefinedAssemblyTestFindings.Create(
                    1,
                    new[] { "Assembly-CSharp: Game.Foo.Alpha" }),
                UnfilteredTestListResult = RunTestsUnfilteredTestListResult.Success(new[] { "Example.Tests.Alpha" })
            };
            RunTestsUseCase useCase = CreateRecordingUseCase(
                executionService,
                appendNoTestsDiagnostics: diagnosticCapture.Append,
                proposeTestAsmdef: _ =>
                {
                    asmdefProposed = true;
                    return new RunTestsTestAsmdefProposal("Assets/Tests/Editor/Game.Tests.Editor.asmdef", "{}");
                });

            RunTestsResponse response = await ExecuteToCompletionAsync(
                useCase,
                new RunTestsSchema { RerunFailed = true });

            Assert.That(response.NoTestsFound, Is.True);
            Assert.That(response.RerunTargetCount, Is.EqualTo(2));
            const string expectedMessage =
                "None of the 2 tests recorded as failed in the EditMode run completed at 2026-01-02T03:04:05.0000000Z exist any more; they were renamed or removed. Run uloop run-tests without --rerun-failed.";
            Assert.That(response.Message, Is.EqualTo(expectedMessage));
            Assert.That(response.NoTestsFoundExplanation, Is.EqualTo(expectedMessage));
            Assert.That(response.ProposedTestAsmdef, Is.Null);
            Assert.That(response.UnfilteredTestNames, Is.Null);
            Assert.That(diagnosticCapture.AppendCalled, Is.False);
            Assert.That(asmdefProposed, Is.False);
        }

        /// <summary>
        /// What: --rerun-failed in PlayMode reads only the PlayMode record, even when an EditMode record exists.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_RerunFailedUsesRecordOfRequestedTestMode()
        {
            SeedRecord(UnityCliLoopTestMode.EditMode, new[] { "Ns.C.EditModeFailure" });
            StubTestExecutionService executionService = new StubTestExecutionService();
            RunTestsUseCase useCase = CreateRecordingUseCase(executionService);

            RunTestsResponse response = await ExecuteToCompletionAsync(
                useCase,
                new RunTestsSchema
                {
                    TestMode = UnityCliLoopTestMode.PlayMode,
                    RerunFailed = true
                });

            Assert.That(response.Success, Is.False);
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "No completed PlayMode run is recorded for this project. Run uloop run-tests without --rerun-failed first."));
            Assert.That(executionService.WasCalled, Is.False);
        }

        /// <summary>
        /// What: a rerun that times out leaves no record.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_RerunFailedTimesOut_LeavesNoRecord()
        {
            SeedRecord(UnityCliLoopTestMode.EditMode, new[] { "Ns.C.FirstFailure", "Ns.C.SecondFailure" });
            StubTestExecutionService executionService = new StubTestExecutionService
            {
                ThrowsExecutionTimeout = true
            };
            RunTestsUseCase useCase = CreateRecordingUseCase(executionService);

            RunTestsResponse response = await ExecuteToCompletionAsync(
                useCase,
                new RunTestsSchema { RerunFailed = true });

            Assert.That(response.Success, Is.False);
            Assert.That(executionService.LastFilter.FilterType, Is.EqualTo(TestExecutionFilterType.TestNames));
            AssertNoRecord(UnityCliLoopTestMode.EditMode);
        }

        private async Task AssertRerunFilterConflictAsync(TestFilterType filterType, string filterValue)
        {
            SeedRecord(UnityCliLoopTestMode.EditMode, new[] { "Ns.C.FirstFailure" });
            StubTestExecutionService executionService = new StubTestExecutionService();
            RunTestsUseCase useCase = CreateRecordingUseCase(executionService);
            RunTestsSchema parameters = new RunTestsSchema
            {
                RerunFailed = true,
                FilterType = filterType,
                FilterValue = filterValue
            };

            RunTestsResponse response = await ExecuteToCompletionAsync(useCase, parameters);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Status, Is.EqualTo(RunTestsExecutionStatus.ExecutionFailed));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "--rerun-failed cannot be combined with --filter-type or --filter-value; it reruns the failures recorded for the test mode."));
            Assert.That(executionService.WasCalled, Is.False);
            AssertRecord(UnityCliLoopTestMode.EditMode, RecordedCompletedAt, "Ns.C.FirstFailure");
        }

        // Builds a use case whose Editor-touching hooks are inert, so record tests change nothing outside
        // the temporary record directory.
        private RunTestsUseCase CreateRecordingUseCase(
            StubTestExecutionService executionService,
            Func<string[]> clearActivePausePoints = null,
            Func<CancellationToken, Task> waitForTestRunnerCleanupAsync = null,
            Func<string, bool, UnityCliLoopTestMode, TestFilterType, string> appendNoTestsDiagnostics = null,
            Func<UnityCliLoopTestMode, RunTestsTestAsmdefProposal> proposeTestAsmdef = null)
        {
            return new RunTestsUseCase(
                new TestFilterCreationService(),
                executionService,
                new StubTestExecutionStateValidationService(ValidationResult.Success()),
                _recordStore,
                clearActivePausePoints: clearActivePausePoints ?? (() => null),
                waitForTestRunnerCleanupAsync: waitForTestRunnerCleanupAsync ?? NoCleanupWait,
                appendNoTestsDiagnostics: appendNoTestsDiagnostics ?? PassThroughNoTestsDiagnostics,
                getActiveHotReloadChangeCount: () => 0,
                proposeTestAsmdef: proposeTestAsmdef ?? (_ => null));
        }

        // Why catch: Unity Test Framework passes an async test that ends Canceled, so an unexpected
        // cancellation must fail the test instead of skipping its assertions.
        private static async Task<RunTestsResponse> ExecuteToCompletionAsync(
            RunTestsUseCase useCase,
            RunTestsSchema parameters)
        {
            try
            {
                return await useCase.ExecuteAsync(parameters, CancellationToken.None);
            }
            catch (OperationCanceledException exception)
            {
                Assert.Fail("ExecuteAsync was cancelled unexpectedly: " + exception);
                return null;
            }
        }

        private static async Task<bool> ExecuteExpectingCancellationAsync(
            RunTestsUseCase useCase,
            RunTestsSchema parameters,
            CancellationToken ct)
        {
            try
            {
                await useCase.ExecuteAsync(parameters, ct);
            }
            catch (OperationCanceledException)
            {
                return true;
            }

            return false;
        }

        private const string RunCompletedAt = "2026-01-03T04:05:06.0000000Z";

        private static SerializableTestResult CreateCompletedRunResult(
            string status,
            int testCount,
            int failedCount,
            params string[] rerunTargets)
        {
            return new SerializableTestResult
            {
                success = status == RunTestsExecutionStatus.Passed,
                status = status,
                hasFailures = failedCount > 0,
                noTestsFound = false,
                noTestsFoundExplanation = string.Empty,
                message = "Test execution completed with status: " + status,
                completedAt = RunCompletedAt,
                testCount = testCount,
                passedCount = testCount - failedCount,
                failedCount = failedCount,
                rerunTargetFullNames = rerunTargets
            };
        }

        private void SeedRecord(UnityCliLoopTestMode testMode, string[] rerunTargets)
        {
            Assert.That(_recordStore.TryWrite(testMode, RecordedCompletedAt, rerunTargets), Is.True);
        }

        private void OccupyRecordPathWithDirectory(UnityCliLoopTestMode testMode)
        {
            string recordPath = _recordStore.GetRecordPath(testMode);
            Directory.CreateDirectory(recordPath);
            File.WriteAllText(Path.Combine(recordPath, "keep.txt"), "occupied");
        }

        private void AssertRecord(UnityCliLoopTestMode testMode, string completedAt, params string[] rerunTargets)
        {
            RunTestsLastRunRecordReadResult read = _recordStore.Read(testMode);
            Assert.That(read.Status, Is.EqualTo(RunTestsLastRunRecordReadStatus.Found), read.UnreadableReason);
            Assert.That(read.Record.CompletedAt, Is.EqualTo(completedAt));
            Assert.That(read.Record.RerunTargets, Is.EqualTo(rerunTargets));
        }

        private void AssertNoRecord(UnityCliLoopTestMode testMode)
        {
            Assert.That(_recordStore.Read(testMode).Status, Is.EqualTo(RunTestsLastRunRecordReadStatus.Missing));
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

            public RunTestsUnsavedChangesMode UnsavedChanges { get; private set; }
            public bool WasCalled { get; private set; }

            public StubTestExecutionStateValidationService(ValidationResult result)
            {
                _result = result;
            }

            public override ValidationResult Validate(UnityCliLoopTestMode testMode, RunTestsUnsavedChangesMode unsavedChanges)
            {
                WasCalled = true;
                UnsavedChanges = unsavedChanges;
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
            public int ExecuteCallCount { get; private set; }
            public TestExecutionFilter LastFilter { get; private set; }
            // Runs first in both execute methods, before the token check, so a test can cancel mid-run.
            public Action OnExecuteStarted { get; set; }
            public bool PlayModeWasCalled { get; private set; }
            public bool LastRespectEnterPlayModeSettings { get; private set; }
            public string LastRequestId { get; private set; }
            // Default stub result satisfies RunTestsResponse preconditions (non-null status
            // and noTestsFoundExplanation); individual tests override NextResult when they
            // need a specific execution status.
            public SerializableTestResult NextResult { get; set; } = new()
            {
                status = RunTestsExecutionStatus.Passed,
                noTestsFoundExplanation = string.Empty
            };

            public override bool IsTestFrameworkAvailable => TestFrameworkAvailable;

            public RunTestsUnfilteredTestListResult UnfilteredTestListResult { get; set; } =
                RunTestsUnfilteredTestListResult.NotRetrieved();

            public RunTestsPredefinedAssemblyTestFindings PredefinedAssemblyTestFindings { get; set; } =
                RunTestsPredefinedAssemblyTestFindings.None();

            public override Task<SerializableTestResult> ExecutePlayModeTestAsync(
                TestExecutionFilter filter,
                CancellationToken ct,
                RunTestsPlayModeRunOptions options)
            {
                OnExecuteStarted?.Invoke();
                ct.ThrowIfCancellationRequested();
                WasCalled = true;
                ExecuteCallCount++;
                LastFilter = filter;
                PlayModeWasCalled = true;
                LastRespectEnterPlayModeSettings = options.RespectEnterPlayModeSettings;
                LastRequestId = options.RequestId;
                return Task.FromResult(NextResult);
            }

            // Lets a test reach the CancelAfter failure branch without waiting out a real timeout.
            public bool ThrowsExecutionTimeout { get; set; }

            public override Task<SerializableTestResult> ExecuteEditModeTestAsync(TestExecutionFilter filter, CancellationToken ct)
            {
                OnExecuteStarted?.Invoke();
                ct.ThrowIfCancellationRequested();
                WasCalled = true;
                ExecuteCallCount++;
                LastFilter = filter;
                if (ThrowsExecutionTimeout)
                {
                    throw new OperationCanceledException();
                }

                return Task.FromResult(NextResult);
            }

            internal override Task<RunTestsUnfilteredTestListResult> RetrieveUnfilteredTestNamesAsync(
                UnityCliLoopTestMode testMode,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(UnfilteredTestListResult);
            }

            internal override RunTestsPredefinedAssemblyTestFindings ScanPredefinedAssemblyTests()
            {
                return PredefinedAssemblyTestFindings;
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
