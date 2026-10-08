using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What happened when a freshly built worker assembly was offered to the cache.
    /// </summary>
    internal enum CachePublishKind
    {
        // Why zero: the default outcome must read as "nothing was offered to the cache".
        NotAttempted = 0,
        Published,
        AlreadyPresent,
        Failed
    }

    /// <summary>
    /// Result of offering a built worker assembly to the cache; only a failure carries an error.
    /// </summary>
    internal readonly struct CachePublishOutcome
    {
        private readonly string _error;

        public CachePublishKind Kind { get; }

        public string Error => _error ?? string.Empty;

        private CachePublishOutcome(CachePublishKind kind, string error)
        {
            Kind = kind;
            _error = error;
        }

        public static CachePublishOutcome NotAttempted => throw new NotImplementedException();

        public static CachePublishOutcome Published => throw new NotImplementedException();

        public static CachePublishOutcome AlreadyPresent => throw new NotImplementedException();

        public static CachePublishOutcome Failed(string error)
        {
            throw new NotImplementedException();
        }

        internal string ToVibeValue()
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Keeps built shared Roslyn worker assemblies under a key made from the worker source and the
    /// toolchain, so a new domain or Editor copies the assembly instead of running csc again.
    /// </summary>
    internal static class SharedRoslynCompilerWorkerAssemblyCache
    {
        internal static string ComputeCacheKey(string workerSource, ExternalCompilerPaths paths)
        {
            throw new NotImplementedException();
        }

        internal static string ResolveCacheRoot()
        {
            throw new NotImplementedException();
        }

        internal static string ResolveCachedAssemblyPath(string cacheRoot, string cacheKey)
        {
            throw new NotImplementedException();
        }

        internal static bool TryCopyCachedAssembly(string cachedAssemblyPath, string destinationAssemblyPath)
        {
            throw new NotImplementedException();
        }

        internal static CachePublishOutcome PublishBuiltAssembly(string builtAssemblyPath, string cachedAssemblyPath)
        {
            throw new NotImplementedException();
        }
    }
}
