using System;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Rewarms execute-dynamic-code after Unity compilation invalidates the previously hot path.
    internal sealed class DynamicCodePostCompileWarmup
    {
        private readonly IDynamicCodeExecutionRuntime _runtime;

        internal DynamicCodePostCompileWarmup(IDynamicCodeExecutionRuntime runtime)
        {
            System.Diagnostics.Debug.Assert(runtime != null, "runtime must not be null");
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        internal async Task WarmAsync(CancellationToken ct)
        {
            DynamicCodeForegroundWarmupState.Reset();

            bool completed = false;
            try
            {
                completed = await DynamicCodeForegroundWarmupRunner.RunForegroundSequenceAsync(
                    _runtime,
                    FirstPartyDynamicCodeSettings.GetDynamicCodeSecurityLevel(),
                    yieldToForegroundRequests: false,
                    ct);
                if (completed)
                {
                    DynamicCodeForegroundWarmupState.MarkCompleted();
                }
            }
            finally
            {
                if (!completed)
                {
                    DynamicCodeForegroundWarmupState.ResetAfterIncompleteAttempt();
                }
            }
        }
    }
}
