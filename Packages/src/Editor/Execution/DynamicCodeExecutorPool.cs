using System;
using System.Collections.Generic;
using io.github.hatayama.UnityCliLoop.Factory;

namespace io.github.hatayama.UnityCliLoop
{
    internal sealed class DynamicCodeExecutorPool : IDynamicCodeExecutorPool
    {
        private readonly IDynamicCodeExecutorProvider _executorProvider;
        private readonly Dictionary<DynamicCodeSecurityLevel, IDynamicCodeExecutor> _executorsBySecurityLevel = new();
        private readonly object _executorsLock = new();
        private bool _disposed;

        public DynamicCodeExecutorPool(IDynamicCodeExecutorProvider executorProvider)
        {
            _executorProvider = executorProvider ?? throw new ArgumentNullException(nameof(executorProvider));
        }

        public IDynamicCodeExecutor GetOrCreate(DynamicCodeSecurityLevel securityLevel)
        {
            lock (_executorsLock)
            {
                ThrowIfDisposed();

                if (_executorsBySecurityLevel.TryGetValue(securityLevel, out IDynamicCodeExecutor executor))
                {
                    return executor;
                }

                IDynamicCodeExecutor createdExecutor = _executorProvider.Create(securityLevel);
                if (createdExecutor is DynamicCodeExecutorStub)
                {
                    return createdExecutor;
                }

                _executorsBySecurityLevel.Add(securityLevel, createdExecutor);
                return createdExecutor;
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

                foreach (IDynamicCodeExecutor executor in _executorsBySecurityLevel.Values)
                {
                    executor.Dispose();
                }

                _executorsBySecurityLevel.Clear();
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
