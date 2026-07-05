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
                ValidationResult.Failure("EditMode tests cannot run during play mode"));
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                NoCleanupWait
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
            Assert.That(response.Message, Is.EqualTo("EditMode tests cannot run during play mode"));
            Assert.That(response.CompletedAt, Is.Not.Empty);
            Assert.That(response.TestCount, Is.EqualTo(0));
            Assert.That(response.PassedCount, Is.EqualTo(0));
            Assert.That(response.FailedCount, Is.EqualTo(0));
            Assert.That(response.SkippedCount, Is.EqualTo(0));
            Assert.That(executionService.WasCalled, Is.False);
            Assert.That(validationService.SaveBeforeRun, Is.True);
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
                NoCleanupWait
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
                NoCleanupWait
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
                NoCleanupWait
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
                NoCleanupWait
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
                NoCleanupWait
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
                ct =>
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
                ValidationResult.Failure("EditMode tests cannot run during play mode"));
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService,
                ct =>
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
                ct =>
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
    }
}
