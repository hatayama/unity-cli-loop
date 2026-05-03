using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Runs the prepared wrapper entry point while keeping Undo and cancellation handling consistent.
    /// </summary>
    public class CommandRunner : ICompiledCommandInvoker
    {
        private readonly CompiledCommandEntryPointResolver _entryPointResolver;
        private bool _isRunning = false;
        private CancellationTokenSource _cancellationTokenSource;

        public bool IsRunning => _isRunning;

        public CommandRunner()
            : this(DynamicCodeServices.CommandEntryPointResolver)
        {
        }

        internal CommandRunner(CompiledCommandEntryPointResolver entryPointResolver)
        {
            _entryPointResolver = entryPointResolver;
        }

        private static void LogExecutionError(Exception ex, string correlationId)
        {
            VibeLogger.LogError(
                "command_execution_error",
                "Command execution failed with exception",
                new { 
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                },
                correlationId,
                "Unexpected execution error occurred",
                "Investigate execution failures and improve error handling"
            );
        }

        private CancellationTokenSource CreateCombinedCancellationTokenSource(ExecutionContext context)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(
                _cancellationTokenSource.Token,
                context.CancellationToken
            );
        }

        public ExecutionResult Execute(ExecutionContext context)
        {
            string correlationId = McpConstants.GenerateCorrelationId();
            if (!TryBeginExecution(out int undoGroup))
            {
                return CreateErrorResult(McpConstants.ERROR_MESSAGE_EXECUTION_IN_PROGRESS);
            }

            try
            {
                using CancellationTokenSource combinedCts = CreateCombinedCancellationTokenSource(context);
                return ExecuteInternal(context, combinedCts.Token);
            }
            catch (OperationCanceledException)
            {
                return CreateCancelledResult();
            }
            catch (Exception ex)
            {
                LogExecutionError(ex, correlationId);

                return CreateErrorResult(
                    ex.Message,
                    new List<string> { $"Exception: {ex.Message}" });
            }
            finally
            {
                EndExecution(undoGroup);
            }
        }

        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }

        public async Task<ExecutionResult> ExecuteAsync(ExecutionContext context)
        {
            string correlationId = McpConstants.GenerateCorrelationId();
            if (!TryBeginExecution(out int undoGroup))
            {
                return CreateErrorResult(McpConstants.ERROR_MESSAGE_EXECUTION_IN_PROGRESS);
            }

            try
            {
                using CancellationTokenSource combinedCts = CreateCombinedCancellationTokenSource(context);
                return await ExecuteInternalAsync(context, combinedCts.Token);
            }
            catch (OperationCanceledException)
            {
                return CreateCancelledResult();
            }
            catch (Exception ex)
            {
                LogExecutionError(ex, correlationId);

                return CreateErrorResult(
                    ex.Message,
                    new List<string> { $"Exception: {ex.Message}" });
            }
            finally
            {
                EndExecution(undoGroup);
            }
        }

        private bool TryBeginExecution(out int undoGroup)
        {
            undoGroup = -1;
            if (_isRunning)
            {
                return false;
            }

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ExecuteDynamicCode");
            return true;
        }

        private void EndExecution(int undoGroup)
        {
            Undo.CollapseUndoOperations(undoGroup);
            _isRunning = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        private static ExecutionResult CreateErrorResult(string errorMessage, List<string> logs = null)
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                ExecutionTime = TimeSpan.Zero,
                Logs = logs ?? new List<string>()
            };
        }

        private static ExecutionResult CreateCancelledResult()
        {
            return CreateErrorResult(
                McpConstants.ERROR_MESSAGE_EXECUTION_CANCELLED,
                new List<string> { "Execution cancelled" });
        }

        private static ExecutionResult CreateSuccessResult(string result, List<string> logs = null)
        {
            return new ExecutionResult
            {
                Success = true,
                Result = result,
                ExecutionTime = TimeSpan.Zero, // Will be set by the caller
                Logs = logs ?? new List<string> { "Execution completed successfully" }
            };
        }

        private static object CreateInstance(Type targetType)
        {
            return Activator.CreateInstance(targetType);
        }

        private static object InvokeExecuteMethod(
            MethodInfo executeMethod,
            object instance,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            object[] callArgs = BuildSupportedArguments(executeMethod, parameters, cancellationToken);
            return executeMethod.Invoke(instance, callArgs);
        }

        private ExecutionResult ExecuteInternal(ExecutionContext context, CancellationToken cancellationToken)
        {
            if (context.CompiledAssembly == null)
            {
                return CreateErrorResult(McpConstants.ERROR_MESSAGE_NO_COMPILED_ASSEMBLY);
            }

            try
            {
                // Find executable type from assembly (sync only)
                (Type targetType, MethodInfo executeMethod) = _entryPointResolver.TryFindExecuteMethod(
                    context.CompiledAssembly);
                
                if (targetType == null || executeMethod == null)
                {
                    return CreateErrorResult(
                        McpConstants.ERROR_MESSAGE_NO_EXECUTE_METHOD,
                        new List<string> { "Assembly types checked but no Execute method found" });
                }

                // Create instance
                object instance = CreateInstance(targetType);
                if (instance == null)
                {
                    return CreateErrorResult(McpConstants.ERROR_MESSAGE_FAILED_TO_CREATE_INSTANCE);
                }

                // Check cancellation
                    cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Execute method
                    object executionResult = InvokeExecuteMethod(
                        executeMethod,
                        instance,
                        context.Parameters,
                        cancellationToken);
                    
                    // Convert result to string
                    string resultString = executionResult?.ToString() ?? "";
                    
                    return CreateSuccessResult(resultString);
                }
                catch (NotSupportedException ex)
                {
                    return CreateErrorResult(
                        McpConstants.ERROR_MESSAGE_UNSUPPORTED_SIGNATURE,
                        new List<string> { ex.Message });
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TargetInvocationException ex)
            {
                // Retrieve actual exception
                Exception innerException = ex.InnerException ?? ex;
                if (innerException is OperationCanceledException)
                {
                    throw innerException;
                }
                
                return CreateErrorResult(
                    innerException.Message,
                    new List<string> 
                    { 
                        $"Target invocation exception: {innerException.Message}",
                        $"Stack trace: {innerException.StackTrace}"
                    });
            }
            catch (Exception ex)
            {
                return CreateErrorResult(
                    ex.Message,
                    new List<string> 
                    { 
                        $"Execution exception: {ex.Message}",
                        $"Stack trace: {ex.StackTrace}"
                    });
            }
        }

        private async Task<ExecutionResult> ExecuteInternalAsync(ExecutionContext context, CancellationToken cancellationToken)
        {
            if (context.CompiledAssembly == null)
            {
                return CreateErrorResult(McpConstants.ERROR_MESSAGE_NO_COMPILED_ASSEMBLY);
            }

            try
            {
                // Prefer async ExecuteAsync; fallback to sync Execute
                (Type asyncType, MethodInfo executeAsyncMethod) = _entryPointResolver.TryFindExecuteAsyncMethod(
                    context.CompiledAssembly);
                if (asyncType != null && executeAsyncMethod != null)
                {
                    object instance = CreateInstance(asyncType);
                    if (instance == null)
                    {
                        return CreateErrorResult(McpConstants.ERROR_MESSAGE_FAILED_TO_CREATE_INSTANCE);
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    object[] callArgs = BuildSupportedArguments(
                        executeAsyncMethod,
                        context.Parameters,
                        cancellationToken);
                    object invoked = executeAsyncMethod.Invoke(instance, callArgs);

                    object awaitedResult = await io.github.hatayama.UnityCliLoop.AwaitableHelper.AwaitIfNeeded(invoked);
                    string resultString = awaitedResult?.ToString() ?? "";

                    return CreateSuccessResult(resultString);
                }

                // Fallback to sync path if no async method found
                ExecutionResult syncResult = ExecuteInternal(context, cancellationToken);
                return syncResult;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TargetInvocationException ex)
            {
                Exception innerException = ex.InnerException ?? ex;
                if (innerException is OperationCanceledException)
                {
                    throw innerException;
                }

                return CreateErrorResult(
                    innerException.Message,
                    new List<string>
                    {
                        $"Target invocation exception: {innerException.Message}",
                        $"Stack trace: {innerException.StackTrace}"
                    });
            }
            catch (Exception ex)
            {
                return CreateErrorResult(
                    ex.Message,
                    new List<string>
                    {
                        $"Execution exception: {ex.Message}",
                        $"Stack trace: {ex.StackTrace}"
                    });
            }
        }

        private static object[] BuildSupportedArguments(
            MethodInfo method,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            System.Reflection.ParameterInfo[] methodParameters = method.GetParameters();
            if (methodParameters.Length == 0)
            {
                return null;
            }
            if (methodParameters.Length == 2 && methodParameters[0].ParameterType == typeof(Dictionary<string, object>) && methodParameters[1].ParameterType == typeof(CancellationToken))
            {
                return new object[] { parameters, cancellationToken };
            }
            if (methodParameters.Length == 1 && methodParameters[0].ParameterType == typeof(Dictionary<string, object>))
            {
                return new object[] { parameters };
            }
            if (methodParameters.Length == 1 && methodParameters[0].ParameterType == typeof(CancellationToken))
            {
                return new object[] { cancellationToken };
            }

            throw new NotSupportedException(
                "Expected Execute/ExecuteAsync overloads with Dictionary<string,object> and/or CancellationToken");
        }
    }
}
