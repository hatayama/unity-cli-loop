using System;
using System.Threading.Tasks;
using System.Threading;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Handles temporal cohesion for test execution processing
    /// Processing sequence: 1. Test filter creation, 2. Test execution, 3. Result processing
    /// Related classes: RunTestsTool, TestFilterCreationService, TestExecutionService
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    public class RunTestsUseCase : IUnityCliLoopTestExecutionService
    {
        private readonly TestFilterCreationService _filterService;
        private readonly TestExecutionService _executionService;
        private readonly TestExecutionStateValidationService _validationService;

        public RunTestsUseCase()
            : this(
                new TestFilterCreationService(),
                new TestExecutionService(),
                new TestExecutionStateValidationService())
        {
        }

        public RunTestsUseCase(
            TestFilterCreationService filterService,
            TestExecutionService executionService,
            TestExecutionStateValidationService validationService)
        {
            Debug.Assert(filterService != null, "filterService must not be null");
            Debug.Assert(executionService != null, "executionService must not be null");
            Debug.Assert(validationService != null, "validationService must not be null");
            _filterService = filterService;
            _executionService = executionService;
            _validationService = validationService;
        }

        /// <summary>
        /// Executes test execution processing
        /// </summary>
        /// <param name="parameters">Test execution parameters</param>
        /// <param name="ct">Cancellation control token</param>
        /// <returns>Test execution result</returns>
        public async Task<UnityCliLoopTestExecutionResult> ExecuteAsync(UnityCliLoopTestExecutionRequest parameters, CancellationToken ct)
        {
            TestMode testMode = ToUnityTestMode(parameters.TestMode);
            ValidationResult validation = _validationService.Validate(testMode, parameters.SaveBeforeRun);
            if (!validation.IsValid)
            {
                return CreateFailureResponse(validation.ErrorMessage);
            }

            // 1. Test filter creation
            TestExecutionFilter filter = null;
            if (parameters.FilterType != TestFilterType.all)
            {
                filter = _filterService.CreateFilter(parameters.FilterType, parameters.FilterValue);
            }
            
            // 2. Test execution
            ct.ThrowIfCancellationRequested();
            SerializableTestResult result;
            
            try
            {
                if (testMode == TestMode.PlayMode)
                {
                    // TODO: Add cancellationToken parameter when TestExecutionService supports it
                    result = await _executionService.ExecutePlayModeTestAsync(filter);
                }
                else
                {
                    // TODO: Add cancellationToken parameter when TestExecutionService supports it
                    result = await _executionService.ExecuteEditModeTestAsync(filter);
                }
            }
            catch (System.OperationCanceledException)
            {
                // Propagate cancellation exceptions
                throw;
            }
            catch (System.Exception ex)
            {
                // Log full exception details for debugging
                UnityEngine.Debug.LogError($"Test execution failed: {ex}");
                VibeLogger.LogError(
                    "test_execution_failed", 
                    "Test execution encountered an error", 
                    new { testMode, filterType = parameters.FilterType, filterValue = parameters.FilterValue, error = ex.Message }
                );
                
                // Create a minimal error result
                throw new System.InvalidOperationException("Test execution failed. Please check the logs for details.", ex);
            }
            
            // 3. Response creation
            return new UnityCliLoopTestExecutionResult
            {
                Success = result.success,
                Message = result.message,
                CompletedAt = result.completedAt,
                TestCount = result.testCount,
                PassedCount = result.passedCount,
                FailedCount = result.failedCount,
                SkippedCount = result.skippedCount,
                XmlPath = result.xmlPath
            };
        }

        public Task<UnityCliLoopTestExecutionResult> RunTestsAsync(UnityCliLoopTestExecutionRequest request, CancellationToken ct)
        {
            return ExecuteAsync(request, ct);
        }

        private static TestMode ToUnityTestMode(UnityCliLoopTestMode testMode)
        {
            if (testMode == UnityCliLoopTestMode.PlayMode)
            {
                return TestMode.PlayMode;
            }

            return TestMode.EditMode;
        }

        private static UnityCliLoopTestExecutionResult CreateFailureResponse(string message)
        {
            return new UnityCliLoopTestExecutionResult
            {
                Success = false,
                Message = message,
                CompletedAt = DateTime.UtcNow.ToString("o"),
                TestCount = 0,
                PassedCount = 0,
                FailedCount = 0,
                SkippedCount = 0,
                XmlPath = null
            };
        }
    }
}
