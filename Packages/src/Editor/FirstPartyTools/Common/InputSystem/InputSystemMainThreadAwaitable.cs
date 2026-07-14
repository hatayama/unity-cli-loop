#nullable enable
#if ULOOP_HAS_INPUT_SYSTEM
using System;
using System.Runtime.CompilerServices;
using System.Threading;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Switches input simulation continuations back to Unity's main thread without wrapping the switch in a Task.
    /// </summary>
    internal readonly struct InputSystemMainThreadAwaitable
    {
        private readonly CancellationToken _ct;

        public InputSystemMainThreadAwaitable(CancellationToken ct)
        {
            _ct = ct;
        }

        public Awaiter GetAwaiter()
        {
            return new Awaiter(_ct);
        }

        public readonly struct Awaiter : INotifyCompletion
        {
            private readonly CancellationToken _ct;

            public Awaiter(CancellationToken ct)
            {
                _ct = ct;
            }

            public bool IsCompleted
            {
                get
                {
                    _ct.ThrowIfCancellationRequested();
                    return MainThreadSwitcher.IsMainThread;
                }
            }

            public void GetResult()
            {
                _ct.ThrowIfCancellationRequested();
            }

            public void OnCompleted(Action continuation)
            {
                MainThreadSwitcher.SwitchToMainThread(_ct).GetAwaiter().OnCompleted(continuation);
            }
        }
    }
}
#endif
