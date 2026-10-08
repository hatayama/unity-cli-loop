using System;
using System.IO;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Points the shared Roslyn worker at an empty, test-owned assembly cache until disposed, so a
    /// worker assembly another run cached does not skip the worker build a test depends on.
    /// </summary>
    internal sealed class SharedRoslynCompilerWorkerCacheScope : IDisposable
    {
        private readonly Func<Func<string>, Func<string>> _swap;
        private readonly Func<string> _previousResolver;

        public string CacheRoot { get; }

        private SharedRoslynCompilerWorkerCacheScope(Func<Func<string>, Func<string>> swap)
        {
            CacheRoot = Path.Combine(
                Path.GetTempPath(),
                "RoslynWorkerCacheTests_" + Guid.NewGuid().ToString("N"));
            _swap = swap;
            _previousResolver = swap(() => CacheRoot);
        }

        /// <summary>
        /// Swaps the cache root of the shared worker the host facade uses.
        /// </summary>
        public static SharedRoslynCompilerWorkerCacheScope ForHost()
        {
            return new SharedRoslynCompilerWorkerCacheScope(
                SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCacheRootForTests);
        }

        /// <summary>
        /// Swaps the cache root of a session the test created itself.
        /// </summary>
        public static SharedRoslynCompilerWorkerCacheScope ForSession(SharedRoslynCompilerWorkerSession session)
        {
            return new SharedRoslynCompilerWorkerCacheScope(session.SwapWorkerAssemblyCacheRootForTests);
        }

        public void Dispose()
        {
            _swap(_previousResolver);
            if (Directory.Exists(CacheRoot))
            {
                Directory.Delete(CacheRoot, true);
            }
        }
    }
}
