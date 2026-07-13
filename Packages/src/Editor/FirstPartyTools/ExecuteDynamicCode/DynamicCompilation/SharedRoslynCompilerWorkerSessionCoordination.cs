using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Separates async compile conversation serialization from short process-state locking
    /// so shutdown can kill the worker without waiting on an in-flight ReadLine.
    /// Kept free of UnityEngine so pure unit tests can cover the shutdown interrupt path.
    /// </summary>
    internal sealed class SharedRoslynCompilerWorkerSessionCoordination
    {
        private readonly object _syncRoot = new();
        private readonly SemaphoreSlim _compileGate = new(1, 1);

        /// <summary>
        /// Serializes worker request/response conversations without holding the state lock across awaits.
        /// </summary>
        public async Task<T> RunSerializedCompileAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct)
        {
            Debug.Assert(operation != null, "operation must not be null");

            await _compileGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await operation(ct).ConfigureAwait(false);
            }
            finally
            {
                _compileGate.Release();
            }
        }

        /// <summary>
        /// Runs a short critical section over process/directory state.
        /// Why not hold this across ReadLine: shutdown must be able to kill the worker while a read waits.
        /// </summary>
        public T ExecuteWithStateLock<T>(Func<T> operation)
        {
            Debug.Assert(operation != null, "operation must not be null");

            lock (_syncRoot)
            {
                return operation();
            }
        }

        public void ExecuteWithStateLock(Action operation)
        {
            Debug.Assert(operation != null, "operation must not be null");

            lock (_syncRoot)
            {
                operation();
            }
        }

        /// <summary>
        /// Runs shutdown under the state lock without acquiring the compile gate.
        /// </summary>
        public void RunShutdownWithoutCompileGate(Action shutdownUnderStateLock)
        {
            Debug.Assert(shutdownUnderStateLock != null, "shutdownUnderStateLock must not be null");

            lock (_syncRoot)
            {
                shutdownUnderStateLock();
            }
        }

        public void AssertStateLockHeld()
        {
            Debug.Assert(Monitor.IsEntered(_syncRoot), "Shared worker session state lock must be held");
        }
    }
}
