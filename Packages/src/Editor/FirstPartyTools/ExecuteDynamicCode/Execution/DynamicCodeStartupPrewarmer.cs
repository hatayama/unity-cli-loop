using System;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Warms the dynamic-code execution path after editor startup so the first user request avoids startup compiler cost.
    /// </summary>
    internal sealed class DynamicCodeStartupPrewarmer
    {
        private readonly object _syncRoot = new();
        private readonly IDynamicCodeExecutionRuntime _runtime;
        private readonly int _delayFrameCount;
        private Task _prewarmTask;
        private bool _requested;

        internal DynamicCodeStartupPrewarmer(
            IDynamicCodeExecutionRuntime runtime,
            int delayFrameCount)
        {
            System.Diagnostics.Debug.Assert(runtime != null, "runtime must not be null");
            System.Diagnostics.Debug.Assert(delayFrameCount >= 0, "delayFrameCount must not be negative");

            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _delayFrameCount = delayFrameCount;
        }

        internal void Request()
        {
            RequestAsync(CancellationToken.None).Forget();
        }

        internal Task RequestAsync(CancellationToken ct)
        {
            lock (_syncRoot)
            {
                if (_requested)
                {
                    return _prewarmTask ?? Task.CompletedTask;
                }

                _requested = true;
                _prewarmTask = RunAsync(ct);
                return _prewarmTask;
            }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            await EditorDelay.DelayFrame(_delayFrameCount, ct);
            if (!DynamicCodeForegroundWarmupState.TryBegin())
            {
                return;
            }

            bool completed = false;
            try
            {
                completed = await ExecuteStartupWarmupSequenceAsync(ct);
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
                    ResetRequestStateAfterIncompleteAttempt();
                }
            }
        }

        private async Task<bool> ExecuteStartupWarmupSequenceAsync(CancellationToken ct)
        {
            foreach (string warmupCode in DynamicCodeForegroundWarmupSnippets.ReturnStringShapes)
            {
                DynamicCodeExecutionRequest request = new()
                {
                    Code = warmupCode,
                    ClassName = DynamicCodeConstants.DEFAULT_CLASS_NAME,
                    CompileOnly = false,
                    SecurityLevel = FirstPartyDynamicCodeSettings.GetDynamicCodeSecurityLevel(),
                    YieldToForegroundRequests = true
                };

                (bool entered, ExecutionResult result) = await _runtime.TryExecuteIfIdleAsync(request, ct);
                if (!entered || !result.Success)
                {
                    return false;
                }
            }

            return true;
        }

        private void ResetRequestStateAfterIncompleteAttempt()
        {
            lock (_syncRoot)
            {
                _requested = false;
                _prewarmTask = null;
            }
        }
    }
}
