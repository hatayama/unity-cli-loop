using System;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Dynamic Code Execution Scheduler Hooks behavior for Unity CLI Loop.
    /// </summary>
    internal sealed class DynamicCodeExecutionSchedulerHooks
    {
        public Action AfterSemaphoreEntered { get; set; }

        public Func<Task> AfterBackgroundExecutionStatePublishedAsync { get; set; }

        public Func<Task> AfterBusySemaphoreProbeFailedAsync { get; set; }

        /// <summary>
        /// Optional warning sink. Why not Debug.LogWarning here: the scheduler is pure C# and
        /// must stay Unity-free for the dedicated unit-test project.
        /// </summary>
        public Action<string> LogWarning { get; set; }

        public async Task InvokeAfterBackgroundExecutionStatePublishedAsync()
        {
            if (AfterBackgroundExecutionStatePublishedAsync == null)
            {
                return;
            }

            await AfterBackgroundExecutionStatePublishedAsync();
        }

        public async Task InvokeAfterBusySemaphoreProbeFailedAsync()
        {
            if (AfterBusySemaphoreProbeFailedAsync == null)
            {
                return;
            }

            await AfterBusySemaphoreProbeFailedAsync();
        }

        public void InvokeLogWarning(string message)
        {
            LogWarning?.Invoke(message);
        }
    }
}
