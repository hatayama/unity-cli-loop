using System;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Handles temporal cohesion for test execution processing
    /// Processing sequence: 1. Test filter creation, 2. Test execution, 3. Result processing
    /// Related classes: RunTestsTool, TestFilterCreationService, TestExecutionService
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    public class RunTestsUseCase : AbstractUseCase<RunTestsSchema, RunTestsResponse>
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
        /// <param name="cancellationToken">Cancellation control token</param>
        /// <returns>Test execution result</returns>
        public override async Task<RunTestsResponse> ExecuteAsync(RunTestsSchema parameters, CancellationToken ct)
        {
#if !ULOOPMCP_HAS_TEST_FRAMEWORK
            ct.ThrowIfCancellationRequested();
            return await Task.FromResult(RunTestsResponse.CreateTestFrameworkUnavailable());
#else
            ValidationResult validation = _validationService.Validate(parameters.TestMode, parameters.SaveBeforeRun);
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
                if (parameters.TestMode == RunTestMode.PlayMode)
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
                    new { testMode = parameters.TestMode, filterType = parameters.FilterType, filterValue = parameters.FilterValue, error = ex.Message }
                );
                
                // Create a minimal error result
                throw new System.InvalidOperationException("Test execution failed. Please check the logs for details.", ex);
            }
            
            // 3. Response creation
            return new RunTestsResponse(
                success: result.success,
                message: result.message,
                completedAt: result.completedAt,
                testCount: result.testCount,
                passedCount: result.passedCount,
                failedCount: result.failedCount,
                skippedCount: result.skippedCount,
                xmlPath: result.xmlPath
            );
#endif
        }

        private static RunTestsResponse CreateFailureResponse(string message)
        {
            return new RunTestsResponse(
                success: false,
                message: message,
                completedAt: DateTime.UtcNow.ToString("o"),
                testCount: 0,
                passedCount: 0,
                failedCount: 0,
                skippedCount: 0,
                xmlPath: null
            );
        }
    }
}
