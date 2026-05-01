using System.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Responsible for temporal cohesion of Console clear processing
    /// Processing sequence: 1. Get current log count, 2. Execute console clear, 3. Add confirmation message, 4. Create result
    /// Related classes: ClearConsoleTool, ConsoleUtility
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    public class ClearConsoleUseCase : AbstractUseCase<ClearConsoleSchema, ClearConsoleResponse>
    {
        /// <summary>
        /// Execute Console clear processing
        /// </summary>
        /// <param name="parameters">Clear configuration parameters</param>
        /// <param name="cancellationToken">Cancellation control token</param>
        /// <returns>Clear execution result</returns>
        public override Task<ClearConsoleResponse> ExecuteAsync(ClearConsoleSchema parameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            try
            {
                // 1. Get current log count
                ConsoleUtility.GetConsoleLogCounts(out int errorCount, out int warningCount, out int logCount);
                int totalLogCount = errorCount + warningCount + logCount;
                
                ClearedLogCounts clearedCounts = new ClearedLogCounts(errorCount, warningCount, logCount);

                cancellationToken.ThrowIfCancellationRequested();

                // 2. Execute console clear
                ConsoleUtility.ClearConsole();

                // 3. Add confirmation message
                if (parameters.AddConfirmationMessage)
                {
                    Debug.Log("=== Console cleared via Unity CLI Loop ===");
                }

                // 4. Create result
                string message = totalLogCount > 0 
                    ? $"Successfully cleared {totalLogCount} console logs (Errors: {errorCount}, Warnings: {warningCount}, Logs: {logCount})"
                    : "Console was already empty";

                ClearConsoleResponse response = new ClearConsoleResponse(
                    success: true,
                    clearedLogCount: totalLogCount,
                    clearedCounts: clearedCounts,
                    message: message
                );

                return Task.FromResult(response);
            }
            catch (System.OperationCanceledException)
            {
                throw; // Re-throw cancellation exceptions
            }
            catch (System.Exception ex)
            {
                // Handle exceptions from console operations
                ClearConsoleResponse errorResponse = new ClearConsoleResponse(
                    success: false,
                    clearedLogCount: 0,
                    clearedCounts: new ClearedLogCounts(0, 0, 0),
                    message: $"Failed to clear console: {ex.Message}"
                );
                return Task.FromResult(errorResponse);
            }
        }
    }
}
