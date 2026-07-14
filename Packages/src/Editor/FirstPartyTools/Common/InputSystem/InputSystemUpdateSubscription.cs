#nullable enable
#if ULOOP_HAS_INPUT_SYSTEM
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Owns one Input System update subscription so timeout and cancellation paths remove it exactly once.
    /// </summary>
    internal sealed class InputSystemUpdateSubscription : IDisposable
    {
        private readonly Action callback;
        private int isDisposed;

        public InputSystemUpdateSubscription(Action callback)
        {
            Debug.Assert(callback != null, "callback must not be null");

            this.callback = callback ?? throw new ArgumentNullException(nameof(callback));
            InputSystem.onBeforeUpdate += this.callback;
            Interlocked.Increment(ref InputSystemUpdateHelper.PendingConfiguredUpdateCallbackCountValue);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref isDisposed, 1) != 0)
            {
                return;
            }

            Interlocked.Decrement(ref InputSystemUpdateHelper.PendingConfiguredUpdateCallbackCountValue);
            if (MainThreadSwitcher.IsMainThread)
            {
                InputSystem.onBeforeUpdate -= callback;
                return;
            }

            RemoveOnMainThreadAsync(CancellationToken.None).Forget();
        }

        private async Task RemoveOnMainThreadAsync(CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            InputSystem.onBeforeUpdate -= callback;
        }
    }
}
#endif
