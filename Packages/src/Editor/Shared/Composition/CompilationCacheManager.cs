using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Manages the caching of compilation results.
    /// Avoids recompiling the same code by hashing requests with SHA256.
    /// </summary>
    public class CompilationCacheManager
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
            StringBuilder keyBuilder = new StringBuilder();
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
                result.HasSecurityViolations,
                CloneSecurityViolations(result.SecurityViolations),
                result.FailureReason,
                CloneAmbiguousTypeCandidates(result.AmbiguousTypeCandidates),
                CloneStrings(result.AutoInjectedNamespaces),
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
                HasSecurityViolations = cachedResult.HasSecurityViolations,
                SecurityViolations = CloneSecurityViolations(cachedResult.SecurityViolations),
                FailureReason = cachedResult.FailureReason,
                AmbiguousTypeCandidates = CloneAmbiguousTypeCandidates(cachedResult.AmbiguousTypeCandidates),
                AutoInjectedNamespaces = CloneStrings(cachedResult.AutoInjectedNamespaces),
                Timings = BuildCachedCompilationTimings(cachedResult.CompilationBackendKind),
                AdvisoryLogs = CloneStrings(cachedResult.AdvisoryLogs),
                CompilationBackendKind = cachedResult.CompilationBackendKind
            };
        }

        private static List<string> BuildCachedCompilationTimings(
            DynamicCompilationBackendKind compilationBackendKind)
        {
            List<string> timings = new List<string>
            {
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
            List<CompilationError> clonedErrors = new List<CompilationError>();
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

        private static List<SecurityViolation> CloneSecurityViolations(List<SecurityViolation> securityViolations)
        {
            List<SecurityViolation> clonedViolations = new List<SecurityViolation>();
            if (securityViolations == null)
            {
                return clonedViolations;
            }

            foreach (SecurityViolation violation in securityViolations)
            {
                clonedViolations.Add(new SecurityViolation
                {
                    Type = violation.Type,
                    Description = violation.Description,
                    LineNumber = violation.LineNumber,
                    CodeSnippet = violation.CodeSnippet,
                    Message = violation.Message,
                    ApiName = violation.ApiName,
                    Location = violation.Location
                });
            }

            return clonedViolations;
        }

        private static Dictionary<string, List<string>> CloneAmbiguousTypeCandidates(
            Dictionary<string, List<string>> ambiguousTypeCandidates)
        {
            Dictionary<string, List<string>> clonedCandidates = new Dictionary<string, List<string>>();
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

        private sealed class CachedCompilationResult
        {
            public Assembly CompiledAssembly { get; }

            public List<CompilationError> Errors { get; }

            public List<string> Warnings { get; }

            public string UpdatedCode { get; }

            public bool HasSecurityViolations { get; }

            public List<SecurityViolation> SecurityViolations { get; }

            public CompilationFailureReason FailureReason { get; }

            public Dictionary<string, List<string>> AmbiguousTypeCandidates { get; }

            public List<string> AutoInjectedNamespaces { get; }

            public List<string> Timings { get; }

            public List<string> AdvisoryLogs { get; }

            public DynamicCompilationBackendKind CompilationBackendKind { get; }

            public CachedCompilationResult(
                Assembly compiledAssembly,
                List<CompilationError> errors,
                List<string> warnings,
                string updatedCode,
                bool hasSecurityViolations,
                List<SecurityViolation> securityViolations,
                CompilationFailureReason failureReason,
                Dictionary<string, List<string>> ambiguousTypeCandidates,
                List<string> autoInjectedNamespaces,
                List<string> timings,
                List<string> advisoryLogs,
                DynamicCompilationBackendKind compilationBackendKind)
            {
                CompiledAssembly = compiledAssembly;
                Errors = errors;
                Warnings = warnings;
                UpdatedCode = updatedCode;
                HasSecurityViolations = hasSecurityViolations;
                SecurityViolations = securityViolations;
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
