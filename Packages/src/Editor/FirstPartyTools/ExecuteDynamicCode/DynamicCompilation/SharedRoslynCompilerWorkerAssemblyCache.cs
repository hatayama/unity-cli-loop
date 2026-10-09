using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Debug = UnityEngine.Debug;

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

        public static CachePublishOutcome NotAttempted => new(CachePublishKind.NotAttempted, string.Empty);

        public static CachePublishOutcome Published => new(CachePublishKind.Published, string.Empty);

        public static CachePublishOutcome AlreadyPresent => new(CachePublishKind.AlreadyPresent, string.Empty);

        public static CachePublishOutcome Failed(string error)
        {
            Debug.Assert(!string.IsNullOrEmpty(error), "a failed publish must say why");
            return new CachePublishOutcome(CachePublishKind.Failed, error);
        }

        internal string ToVibeValue()
        {
            switch (Kind)
            {
                case CachePublishKind.Published:
                    return "published";
                case CachePublishKind.AlreadyPresent:
                    return "present";
                case CachePublishKind.Failed:
                    return "failed";
                default:
                    return "not_attempted";
            }
        }
    }

    /// <summary>
    /// Keeps built shared Roslyn worker assemblies under a key made from the worker source and the
    /// toolchain, so a new domain or Editor copies the assembly instead of running csc again.
    /// </summary>
    internal static class SharedRoslynCompilerWorkerAssemblyCache
    {
        private const string WorkerAssemblyFileName = "RoslynCompilerWorker.dll";

        // Why the source text and the toolchain paths: either one changing makes a different
        // assembly. The paths stand in for the toolchain contents; a Unity upgrade changes them.
        internal static string ComputeCacheKey(string workerSource, ExternalCompilerPaths paths)
        {
            Debug.Assert(!string.IsNullOrEmpty(workerSource), "workerSource must not be empty");
            Debug.Assert(paths != null, "paths must not be null");

            string toolchain = (paths.CompilerDllPath ?? string.Empty)
                + "|" + (paths.CompilerRuntimeConfigPath ?? string.Empty)
                + "|" + (paths.DotnetHostPath ?? string.Empty)
                + "|" + (paths.CodeAnalysisDllPath ?? string.Empty)
                + "|" + (paths.CodeAnalysisCSharpDllPath ?? string.Empty);
            byte[] sourceBytes = Encoding.UTF8.GetBytes(workerSource);
            byte[] toolchainBytes = Encoding.UTF8.GetBytes(toolchain);
            byte[] input = new byte[sourceBytes.Length + 1 + toolchainBytes.Length];
            Buffer.BlockCopy(sourceBytes, 0, input, 0, sourceBytes.Length);
            input[sourceBytes.Length] = 0;
            Buffer.BlockCopy(toolchainBytes, 0, input, sourceBytes.Length + 1, toolchainBytes.Length);

            byte[] hash;
            using (SHA256 sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(input);
            }

            StringBuilder builder = new(hash.Length * 2);
            for (int index = 0; index < hash.Length; index++)
            {
                builder.Append(hash[index].ToString("x2"));
            }

            return builder.ToString();
        }

        // Why OS temp and not Library: the worker can be prepared on a pool thread, where the project
        // root cannot be read, and the per-process worker directory already lives beside this one.
        internal static string ResolveCacheRoot()
        {
            return Path.Combine(Path.GetTempPath(), "UnityCliLoopCompilation", "RoslynWorkerCache");
        }

        internal static string ResolveCachedAssemblyPath(string cacheRoot, string cacheKey)
        {
            return Path.Combine(cacheRoot, cacheKey, WorkerAssemblyFileName);
        }

        /// <summary>
        /// Copies the cached worker assembly to the worker directory. Returns false when there is no
        /// usable cached assembly or the copy fails, so the caller builds the assembly instead.
        /// </summary>
        internal static bool TryCopyCachedAssembly(string cachedAssemblyPath, string destinationAssemblyPath)
        {
            if (!HasNonEmptyFile(cachedAssemblyPath))
            {
                return false;
            }

            // Why catch here: the cache only saves a build. A copy that fails for an environmental
            // reason must fall back to building, not fail the worker start.
            try
            {
                File.Copy(cachedAssemblyPath, destinationAssemblyPath, true);
                return true;
            }
            catch (Exception ex) when (IsCacheIoException(ex))
            {
                if (File.Exists(destinationAssemblyPath))
                {
                    File.Delete(destinationAssemblyPath);
                }

                return false;
            }
        }

        /// <summary>
        /// Offers an assembly this run built to the cache. An assembly already in the cache is kept,
        /// and a failure is returned as an outcome because the built assembly is still usable.
        /// </summary>
        internal static CachePublishOutcome PublishBuiltAssembly(string builtAssemblyPath, string cachedAssemblyPath)
        {
            string tempPath = cachedAssemblyPath + ".tmp-" + Guid.NewGuid().ToString("N");
            // Why one catch over the whole body: creating the directory, deleting, copying, and moving
            // can each fail for environmental reasons, and none of them should fail the worker start,
            // whose assembly is already built. Only IO and access errors are caught; others are bugs.
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachedAssemblyPath));
                if (HasNonEmptyFile(cachedAssemblyPath))
                {
                    return CachePublishOutcome.AlreadyPresent;
                }

                // An empty file is what a crashed Editor can leave; keeping it would hide the cache forever.
                if (File.Exists(cachedAssemblyPath))
                {
                    File.Delete(cachedAssemblyPath);
                }

                // Why copy then move: the cached file appears in one rename, so another Editor never
                // reads a partly written assembly.
                File.Copy(builtAssemblyPath, tempPath);
                if (File.Exists(cachedAssemblyPath))
                {
                    return CachePublishOutcome.AlreadyPresent;
                }

                File.Move(tempPath, cachedAssemblyPath);
                return CachePublishOutcome.Published;
            }
            catch (Exception ex) when (IsCacheIoException(ex))
            {
                // Another Editor can publish between the existence check and the move.
                if (HasNonEmptyFile(cachedAssemblyPath))
                {
                    return CachePublishOutcome.AlreadyPresent;
                }

                return CachePublishOutcome.Failed(ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static bool HasNonEmptyFile(string path)
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }

        private static bool IsCacheIoException(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException;
        }
    }
}
