using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Task related extension methods
    /// Provides convenient methods for fire-and-forget async operations
    /// </summary>
    public static class TaskExtensions
    {
        /// <summary>
        /// Fire-and-forget extension method for Task
        /// Equivalent to "_ = task;" but more explicit and readable
        /// </summary>
        /// <param name="task">Task to fire and forget</param>
        public static void Forget(this Task task)
        {
            if (task == null) return;
            
            // Handle exceptions to prevent crashes
            _ = HandleTaskExceptions(task);
        }
        
        /// <summary>
        /// Handle task exceptions to prevent unhandled exceptions in fire-and-forget scenarios
        /// </summary>
        private static async Task HandleTaskExceptions(Task task)
        {
            try
            {
                await task;
            }
            catch (System.Exception ex)
            {
                // Log the exception to prevent silent failures
                UnityEngine.Debug.LogException(ex);
            }
        }
        
    }
}
