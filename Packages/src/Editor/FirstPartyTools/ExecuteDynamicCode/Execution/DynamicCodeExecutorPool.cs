using System;
using io.github.hatayama.UnityCliLoop.FirstPartyTools.Factory;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Manages pooled Dynamic Code Executor instances for reuse by this module.
    /// </summary>
    internal sealed class DynamicCodeExecutorPool : IDynamicCodeExecutorPool
    {
        private readonly IDynamicCodeExecutorProvider _executorProvider;
        private IDynamicCodeExecutor _executor;
        private readonly object _executorsLock = new();
        private bool _disposed;

        public DynamicCodeExecutorPool(IDynamicCodeExecutorProvider executorProvider)
        {
            _executorProvider = executorProvider ?? throw new ArgumentNullException(nameof(executorProvider));
        }

        public IDynamicCodeExecutor GetOrCreate()
        {
            lock (_executorsLock)
            {
                ThrowIfDisposed();

                if (_executor != null)
                {
                    return _executor;
                }

                IDynamicCodeExecutor createdExecutor = _executorProvider.Create();
                if (createdExecutor is DynamicCodeExecutorStub)
                {
                    return createdExecutor;
                }

                _executor = createdExecutor;
                return _executor;
            }
        }

        public void Dispose()
        {
            lock (_executorsLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (_executor != null)
                {
                    _executor.Dispose();
                }

                _executor = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DynamicCodeExecutorPool));
            }
        }
    }
}
