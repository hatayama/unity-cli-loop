using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using Debug = UnityEngine.Debug;
using Stopwatch = System.Diagnostics.Stopwatch;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Dynamic compilation service for execute-dynamic-code.
    /// Keeps the orchestration flow in one place and delegates low-level compiler concerns to helpers.
    /// </summary>
    internal sealed class DynamicCodeCompiler : IDynamicCompilationService, IDisposable
    {
        private readonly IDynamicCompilationPlanner _planner;
        private readonly ICompiledAssemblyBuilder _assemblyBuilder;
        private readonly ICompiledAssemblyLoader _assemblyLoader;
        private readonly CompilationCacheManager _cacheManager = new();
        private bool _disposed;

        internal int LastBuildCount { get; private set; }

        public DynamicCodeCompiler()
            : this(
                new DynamicCompilationPlanner(new DynamicCodeSourcePreparationService()),
                new CompiledAssemblyBuilder(
                    new ExternalCompilerPathResolutionService(),
                    new DynamicReferenceSetBuilderService(),
                    new DynamicCompilationBackend()),
                new CompiledAssemblyLoadService())
        {
        }

        internal DynamicCodeCompiler(
            IDynamicCompilationPlanner planner,
            ICompiledAssemblyBuilder assemblyBuilder,
            ICompiledAssemblyLoader assemblyLoader)
        {
            _planner = planner ?? throw new ArgumentNullException(nameof(planner));
            _assemblyBuilder = assemblyBuilder ?? throw new ArgumentNullException(nameof(assemblyBuilder));
            _assemblyLoader = assemblyLoader ?? throw new ArgumentNullException(nameof(assemblyLoader));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _cacheManager.ClearCache();
            _disposed = true;
        }

        public async Task<CompilationResult> CompileAsync(CompilationRequest request, CancellationToken ct = default)
        {
            Debug.Assert(request != null, "request must not be null");
            Debug.Assert(!string.IsNullOrWhiteSpace(request.Code), "request.Code must not be empty");

            ct.ThrowIfCancellationRequested();
            await MainThreadSwitcher.SwitchToMainThread(ct);
            string[] activeDefineSymbols = EditorUserBuildSettings.activeScriptCompilationDefines ?? Array.Empty<string>();
            RoslynCompilerOptions compilerOptions = new(
                activeDefineSymbols,
                PlayerSettings.allowUnsafeCode);
            LastBuildCount = 0;
            Stopwatch compilerTotalStopwatch = Stopwatch.StartNew();
            Stopwatch planStopwatch = Stopwatch.StartNew();

            DynamicCompilationPlan plan = _planner.CreatePlan(request);
            planStopwatch.Stop();

            Stopwatch cacheStopwatch = Stopwatch.StartNew();
            CompilationResult cachedResult = TryGetCachedResult(plan);
            cacheStopwatch.Stop();
            if (cachedResult != null)
            {
                AppendCompilerStageTimings(
                    cachedResult.Timings,
                    planStopwatch.Elapsed.TotalMilliseconds,
                    cacheStopwatch.Elapsed.TotalMilliseconds,
                    0,
                    compilerTotalStopwatch.Elapsed.TotalMilliseconds);
                return cachedResult;
            }

            if (plan.PreparedCode.PreparedSource == null)
            {
                CompilationResult mixedModeFailure = CreateMixedModeFailureResult(request.Code, 0, 0);
                AppendCompilerStageTimings(
                    mixedModeFailure.Timings,
                    planStopwatch.Elapsed.TotalMilliseconds,
                    cacheStopwatch.Elapsed.TotalMilliseconds,
                    0,
                    compilerTotalStopwatch.Elapsed.TotalMilliseconds);
                return mixedModeFailure;
            }

            Stopwatch builderTotalStopwatch = Stopwatch.StartNew();
            CompiledAssemblyBuildResult buildResult = await _assemblyBuilder.BuildAsync(
                plan,
                compilerOptions,
                ct).ConfigureAwait(false);
            builderTotalStopwatch.Stop();
            LastBuildCount = buildResult.BuildCount;

            if (buildResult.Diagnostics.Errors.Count > 0)
            {
                CompilationResult failureResult = new()                {
                    Success = false,
                    Errors = buildResult.Diagnostics.Errors,
                    Warnings = buildResult.Diagnostics.Warnings,
                    UpdatedCode = buildResult.UpdatedSource,
                    FailureReason = CompilationFailureReason.CompilationError,
                    AmbiguousTypeCandidates = buildResult.AmbiguousTypeCandidates,
                    AutoInjectedNamespaces = buildResult.AutoInjectedNamespaces,
                    AdvisoryLogs = BuildAdvisoryLogs(buildResult.CompilationBackendKind),
                    CompilationBackendKind = buildResult.CompilationBackendKind,
                    Timings = DynamicCompilationTimingFormatter.CreateCompilationTimings(
                        buildResult.ReferenceResolutionMilliseconds,
                        buildResult.BuildMilliseconds,
                        0,
                        buildResult.CompilationBackendKind)
                };
                AppendCompilerStageTimings(
                    failureResult.Timings,
                    planStopwatch.Elapsed.TotalMilliseconds,
                    cacheStopwatch.Elapsed.TotalMilliseconds,
                    builderTotalStopwatch.Elapsed.TotalMilliseconds,
                    compilerTotalStopwatch.Elapsed.TotalMilliseconds);
                return failureResult;
            }

            CompiledAssemblyLoadResult assemblyLoadResult = _assemblyLoader.Load(buildResult.AssemblyBytes);

            CompilationResult result = new()            {
                Success = true,
                CompiledAssembly = assemblyLoadResult.CompiledAssembly,
                Warnings = buildResult.Diagnostics.Warnings,
                UpdatedCode = buildResult.UpdatedSource,
                AmbiguousTypeCandidates = buildResult.AmbiguousTypeCandidates,
                AutoInjectedNamespaces = buildResult.AutoInjectedNamespaces,
                AdvisoryLogs = BuildAdvisoryLogs(buildResult.CompilationBackendKind),
                CompilationBackendKind = buildResult.CompilationBackendKind,
                Timings = DynamicCompilationTimingFormatter.CreateCompilationTimings(
                    buildResult.ReferenceResolutionMilliseconds,
                    buildResult.BuildMilliseconds,
                    assemblyLoadResult.AssemblyLoadMilliseconds,
                    buildResult.CompilationBackendKind)
            };
            AppendCompilerStageTimings(
                result.Timings,
                planStopwatch.Elapsed.TotalMilliseconds,
                cacheStopwatch.Elapsed.TotalMilliseconds,
                builderTotalStopwatch.Elapsed.TotalMilliseconds,
                compilerTotalStopwatch.Elapsed.TotalMilliseconds);

            if (buildResult.ShouldCacheResult)
            {
                _cacheManager.CacheResultIfSuccessful(result, plan.NormalizedRequest);
            }
            return result;
        }

        private CompilationResult TryGetCachedResult(
            DynamicCompilationPlan plan)
        {
            return _cacheManager.CheckCache(plan.NormalizedRequest);
        }

        private static CompilationResult CreateMixedModeFailureResult(
            string originalCode,
            double referenceResolutionMilliseconds,
            double buildMilliseconds)
        {
            return new CompilationResult
            {
                Success = false,
                Errors = new List<CompilationError>
                {
                    new CompilationError
                    {
                        Message = "Mixed top-level statements and type declarations are not supported. Use a full class definition instead.",
                        ErrorCode = "MIXED_MODE",
                        Line = 0,
                        Column = 0
                    }
                },
                FailureReason = CompilationFailureReason.CompilationError,
                UpdatedCode = originalCode,
                CompilationBackendKind = DynamicCompilationBackendKind.Unknown,
                Timings = DynamicCompilationTimingFormatter.CreateCompilationTimings(
                    referenceResolutionMilliseconds,
                    buildMilliseconds,
                    0)
            };
        }

        private static List<string> BuildAdvisoryLogs(DynamicCompilationBackendKind compilationBackendKind)
        {
            return compilationBackendKind == DynamicCompilationBackendKind.AssemblyBuilderFallback
                ? new List<string>
                {
                    "Warning: Fast Roslyn path is unavailable; execute-dynamic-code is using AssemblyBuilder fallback, so new snippets compile slower."
                }
                : new List<string>();
        }

        private static void AppendCompilerStageTimings(
            List<string> timings,
            double planMilliseconds,
            double cacheMilliseconds,
            double builderTotalMilliseconds,
            double compilerTotalMilliseconds)
        {
            if (timings == null)
            {
                return;
            }

            timings.Add($"[Perf] CompilePlan: {planMilliseconds:F1}ms");
            timings.Add($"[Perf] CompileCacheCheck: {cacheMilliseconds:F1}ms");
            if (builderTotalMilliseconds > 0)
            {
                timings.Add($"[Perf] BuilderTotal: {builderTotalMilliseconds:F1}ms");
            }
            timings.Add($"[Perf] CompilerTotal: {compilerTotalMilliseconds:F1}ms");
        }

    }
}
