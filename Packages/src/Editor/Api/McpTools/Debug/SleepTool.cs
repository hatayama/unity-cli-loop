using System.Threading.Tasks;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Debug tool that sleeps for specified duration to test timeout functionality
    /// Related classes:
    /// - SleepSchema: Input parameters including sleep duration and timeout
    /// - SleepResponse: Output containing results and timing information
    /// - AbstractUnityTool: Base class providing timeout infrastructure
    /// </summary>
    [McpTool(
        DisplayDevelopmentOnly = true,
        Description = "Debug tool that sleeps for specified duration to test timeout functionality"
    )]
    public class SleepTool : AbstractUnityTool<SleepSchema, SleepResponse>
    {
        public override string ToolName => "debug-sleep";
        
        protected override async Task<SleepResponse> ExecuteAsync(SleepSchema parameters, CancellationToken cancellationToken)
        {
            int actualSleepSeconds = 0;
            
            // Sleep 1 second at a time, checking for cancellation between each sleep
            for (int i = 0; i < parameters.SleepSeconds; i++)
            {
                // Check for cancellation before each sleep iteration
                cancellationToken.ThrowIfCancellationRequested();
                
                // Sleep for 1 second with cancellation support
                await Task.Delay(1000, cancellationToken);
                
                actualSleepSeconds = i + 1;
            }
            
            return new SleepResponse
            {
                Message = $"Successfully slept for {actualSleepSeconds} seconds",
                ActualSleepSeconds = actualSleepSeconds,
                WasCancelled = false
            };
        }
    }
}