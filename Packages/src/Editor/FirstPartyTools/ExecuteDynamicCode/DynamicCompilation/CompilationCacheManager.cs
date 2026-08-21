using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Manages the caching of compilation results.
    /// Avoids recompiling the same code by hashing requests with SHA256.
    /// </summary>
    internal sealed class CompilationCacheManager
    {
        private const int MaxCacheEntries = 32;
        private readonly Dictionary<string, CachedCompilationResult> _compilationCache = new();
        private readonly Queue<string> _cacheOrder = new();

        public CompilationResult CheckCache(CompilationRequest request)
        {
            string cacheKey = GenerateCacheKey(request);
            if (_compilationCache.TryGetValue(cacheKey, out CachedCompilationResult cachedResult))
            {
                return CloneCompilationResult(cachedResult);
            }

            return null;
        }

        public void CacheResultIfSuccessful(CompilationResult result, CompilationRequest request)
        {
            if (result.Success && result.CompiledAssembly != null)
            {
                string cacheKey = GenerateCacheKey(request);
                CachedCompilationResult cachedResult = CreateCachedCompilationResult(result);
                if (_compilationCache.ContainsKey(cacheKey))
                {
                    _compilationCache[cacheKey] = cachedResult;
                    return;
                }

                // Dynamic assemblies cannot be unloaded from the default AppDomain,
                // so the cache keeps only a small hot set instead of retaining every snippet forever.
                if (_compilationCache.Count >= MaxCacheEntries)
                {
                    string oldestKey = _cacheOrder.Dequeue();
                    _compilationCache.Remove(oldestKey);
                }

                _compilationCache[cacheKey] = cachedResult;
                _cacheOrder.Enqueue(cacheKey);
            }
        }

        public void ClearCache()
        {
            _compilationCache.Clear();
            _cacheOrder.Clear();
        }

        public string GenerateCacheKey(CompilationRequest request)
        {
            StringBuilder keyBuilder = new();
            keyBuilder.Append(request.Code);
            keyBuilder.Append("|");
            keyBuilder.Append(request.ClassName ?? "");
            keyBuilder.Append("|");
            keyBuilder.Append(request.Namespace ?? "");

            if (request.AdditionalReferences != null && request.AdditionalReferences.Any())
            {
                keyBuilder.Append("|");
                keyBuilder.Append(string.Join(",", request.AdditionalReferences.OrderBy(r => r)));
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyBuilder.ToString()));
                return Convert.ToBase64String(hashBytes);
            }
        }

        private static CachedCompilationResult CreateCachedCompilationResult(CompilationResult result)
        {
            return new CachedCompilationResult(
                result.CompiledAssembly,
                CloneCompilationErrors(result.Errors),
                CloneStrings(result.Warnings),
                result.UpdatedCode,
                result.FailureReason,
                CloneAmbiguousTypeCandidates(result.AmbiguousTypeCandidates),
                CloneAutoInjectedNamespaces(result.AutoInjectedNamespaces),
                CloneStrings(result.Timings),
                CloneStrings(result.AdvisoryLogs),
                result.CompilationBackendKind);
        }

        private static CompilationResult CloneCompilationResult(CachedCompilationResult cachedResult)
        {
            return new CompilationResult
            {
                Success = true,
                CompiledAssembly = cachedResult.CompiledAssembly,
                Errors = CloneCompilationErrors(cachedResult.Errors),
                Warnings = CloneStrings(cachedResult.Warnings),
                UpdatedCode = cachedResult.UpdatedCode,
                FailureReason = cachedResult.FailureReason,
                AmbiguousTypeCandidates = CloneAmbiguousTypeCandidates(cachedResult.AmbiguousTypeCandidates),
                AutoInjectedNamespaces = CloneAutoInjectedNamespaces(cachedResult.AutoInjectedNamespaces),
                Timings = BuildCachedCompilationTimings(cachedResult.CompilationBackendKind),
                AdvisoryLogs = CloneStrings(cachedResult.AdvisoryLogs),
                CompilationBackendKind = cachedResult.CompilationBackendKind
            };
        }

        private static List<string> BuildCachedCompilationTimings(
            DynamicCompilationBackendKind compilationBackendKind)
        {
            List<string> timings = new()            {
                "[Perf] ReferenceResolution: 0.0ms",
                "[Perf] Build: 0.0ms",
                "[Perf] AssemblyLoad: 0.0ms"
            };

            switch (compilationBackendKind)
            {
                case DynamicCompilationBackendKind.SharedRoslynWorker:
                    timings.Add("[Perf] Backend: SharedRoslynWorker");
                    break;
                case DynamicCompilationBackendKind.OneShotRoslyn:
                    timings.Add("[Perf] Backend: OneShotRoslyn");
                    break;
                case DynamicCompilationBackendKind.AssemblyBuilderFallback:
                    timings.Add("[Perf] Backend: AssemblyBuilderFallback");
                    break;
            }

            timings.Add("[Perf] CacheHit: true");
            return timings;
        }

        private static List<CompilationError> CloneCompilationErrors(List<CompilationError> errors)
        {
            List<CompilationError> clonedErrors = new();
            if (errors == null)
            {
                return clonedErrors;
            }

            foreach (CompilationError error in errors)
            {
                clonedErrors.Add(new CompilationError
                {
                    Message = error.Message,
                    Line = error.Line,
                    Column = error.Column,
                    ErrorCode = error.ErrorCode
                });
            }

            return clonedErrors;
        }

        private static Dictionary<string, List<string>> CloneAmbiguousTypeCandidates(
            Dictionary<string, List<string>> ambiguousTypeCandidates)
        {
            Dictionary<string, List<string>> clonedCandidates = new();
            if (ambiguousTypeCandidates == null)
            {
                return clonedCandidates;
            }

            foreach (KeyValuePair<string, List<string>> entry in ambiguousTypeCandidates)
            {
                clonedCandidates[entry.Key] = CloneStrings(entry.Value);
            }

            return clonedCandidates;
        }

        private static List<string> CloneStrings(List<string> values)
        {
            if (values == null)
            {
                return new List<string>();
            }

            return new List<string>(values);
        }

        private static List<AutoInjectedNamespace> CloneAutoInjectedNamespaces(
            List<AutoInjectedNamespace> values)
        {
            List<AutoInjectedNamespace> cloned = new();
            if (values == null)
            {
                return cloned;
            }

            foreach (AutoInjectedNamespace item in values)
            {
                cloned.Add(new AutoInjectedNamespace(item.Namespace, item.TriggerIdentifier, item.IsSpeculative));
            }

            return cloned;
        }

        /// <summary>
        /// Carries the result data produced by Cached Compilation behavior.
        /// </summary>
        private sealed class CachedCompilationResult
        {
            public Assembly CompiledAssembly { get; }

            public List<CompilationError> Errors { get; }

            public List<string> Warnings { get; }

            public string UpdatedCode { get; }

            public CompilationFailureReason FailureReason { get; }

            public Dictionary<string, List<string>> AmbiguousTypeCandidates { get; }

            public List<AutoInjectedNamespace> AutoInjectedNamespaces { get; }

            public List<string> Timings { get; }

            public List<string> AdvisoryLogs { get; }

            public DynamicCompilationBackendKind CompilationBackendKind { get; }

            public CachedCompilationResult(
                Assembly compiledAssembly,
                List<CompilationError> errors,
                List<string> warnings,
                string updatedCode,
                CompilationFailureReason failureReason,
                Dictionary<string, List<string>> ambiguousTypeCandidates,
                List<AutoInjectedNamespace> autoInjectedNamespaces,
                List<string> timings,
                List<string> advisoryLogs,
                DynamicCompilationBackendKind compilationBackendKind)
            {
                CompiledAssembly = compiledAssembly;
                Errors = errors;
                Warnings = warnings;
                UpdatedCode = updatedCode;
                FailureReason = failureReason;
                AmbiguousTypeCandidates = ambiguousTypeCandidates;
                AutoInjectedNamespaces = autoInjectedNamespaces;
                Timings = timings;
                AdvisoryLogs = advisoryLogs;
                CompilationBackendKind = compilationBackendKind;
            }
        }
    }
}
