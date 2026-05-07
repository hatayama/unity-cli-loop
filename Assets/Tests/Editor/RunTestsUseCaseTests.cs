using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;

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
            StubTestExecutionService executionService = new();
            StubTestExecutionStateValidationService validationService = new(
                ValidationResult.Failure("EditMode tests cannot run during play mode"));
            RunTestsUseCase useCase = new(
                new TestFilterCreationService(),
                executionService,
                validationService
            );
            UnityCliLoopTestExecutionRequest parameters = new()
            {
                TestMode = UnityCliLoopTestMode.EditMode,
                SaveBeforeRun = true
            };

            UnityCliLoopTestExecutionResult response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("EditMode tests cannot run during play mode"));
            Assert.That(response.CompletedAt, Is.Not.Empty);
            Assert.That(response.TestCount, Is.EqualTo(0));
            Assert.That(response.PassedCount, Is.EqualTo(0));
            Assert.That(response.FailedCount, Is.EqualTo(0));
            Assert.That(response.SkippedCount, Is.EqualTo(0));
            Assert.That(executionService.WasCalled, Is.False);
            Assert.That(validationService.SaveBeforeRun, Is.True);
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class StubTestExecutionStateValidationService : TestExecutionStateValidationService
        {
            private readonly ValidationResult _result;

            public bool SaveBeforeRun { get; private set; }

            public StubTestExecutionStateValidationService(ValidationResult result)
            {
                _result = result;
            }

            public override ValidationResult Validate(TestMode testMode, bool saveBeforeRun)
            {
                SaveBeforeRun = saveBeforeRun;
                return _result;
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class StubTestExecutionService : TestExecutionService
        {
            public bool WasCalled { get; private set; }

            public override Task<SerializableTestResult> ExecutePlayModeTestAsync(TestExecutionFilter filter)
            {
                WasCalled = true;
                return Task.FromResult(new SerializableTestResult());
            }

            public override Task<SerializableTestResult> ExecuteEditModeTestAsync(TestExecutionFilter filter)
            {
                WasCalled = true;
                return Task.FromResult(new SerializableTestResult());
            }
        }
    }
}
