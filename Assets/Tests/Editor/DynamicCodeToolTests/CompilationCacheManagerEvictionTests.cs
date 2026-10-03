using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies the compilation cache replaces an entry cached again under the same request, evicts the oldest
    /// entry once it holds 32, skips failed results, and hands out copies that report the cached backend.
    /// </summary>
    public sealed class CompilationCacheManagerEvictionTests
    {
        private const int MaxCacheEntries = 32;

        /// <summary>
        /// Verifies caching the same request again replaces the stored result.
        /// </summary>
        [Test]
        public void CacheResultIfSuccessful_WithTheSameRequestAgain_ReplacesTheStoredResult()
        {
            CompilationCacheManager manager = new CompilationCacheManager();
            CompilationRequest request = CreateRequest("return 1;");
            manager.CacheResultIfSuccessful(CreateSuccess("first"), request);

            manager.CacheResultIfSuccessful(CreateSuccess("second"), request);

            Assert.That(manager.CheckCache(request).UpdatedCode, Is.EqualTo("second"));
        }

        /// <summary>
        /// Verifies the 33rd distinct request evicts the first one and keeps the rest.
        /// </summary>
        [Test]
        public void CacheResultIfSuccessful_BeyondTheLimit_EvictsTheOldestEntry()
        {
            CompilationCacheManager manager = new CompilationCacheManager();
            for (int index = 0; index <= MaxCacheEntries; index++)
            {
                manager.CacheResultIfSuccessful(CreateSuccess("code" + index), CreateRequest("return " + index + ";"));
            }

            Assert.That(manager.CheckCache(CreateRequest("return 0;")), Is.Null);
            Assert.That(manager.CheckCache(CreateRequest("return 1;")).UpdatedCode, Is.EqualTo("code1"));
            Assert.That(manager.CheckCache(CreateRequest("return " + MaxCacheEntries + ";")).UpdatedCode, Is.EqualTo("code" + MaxCacheEntries));
        }

        /// <summary>
        /// Verifies a failed result is not cached.
        /// </summary>
        [Test]
        public void CacheResultIfSuccessful_WithAFailedResult_CachesNothing()
        {
            CompilationCacheManager manager = new CompilationCacheManager();
            CompilationRequest request = CreateRequest("return 1;");
            CompilationResult failed = CreateSuccess("failed");
            failed.Success = false;

            manager.CacheResultIfSuccessful(failed, request);

            Assert.That(manager.CheckCache(request), Is.Null);
        }

        /// <summary>
        /// Verifies a cache hit copies errors and ambiguity candidates instead of sharing the cached lists.
        /// </summary>
        [Test]
        public void CheckCache_CopiesErrorsAndAmbiguityCandidates()
        {
            CompilationCacheManager manager = new CompilationCacheManager();
            CompilationRequest request = CreateRequest("return 1;");
            CompilationResult original = CreateSuccess("code");
            original.Errors = new List<CompilationError>
            {
                new CompilationError { Message = "note", Line = 2, Column = 4, ErrorCode = "CS0168" }
            };
            original.AmbiguousTypeCandidates = new Dictionary<string, List<string>>
            {
                { "Random", new List<string> { "UnityEngine", "System" } }
            };
            manager.CacheResultIfSuccessful(original, request);

            CompilationResult cached = manager.CheckCache(request);
            original.Errors[0].Message = "changed";
            original.AmbiguousTypeCandidates["Random"].Add("Other");

            Assert.That(cached.Errors[0].Message, Is.EqualTo("note"));
            Assert.That(cached.Errors[0].Line, Is.EqualTo(2));
            Assert.That(cached.Errors[0].ErrorCode, Is.EqualTo("CS0168"));
            Assert.That(cached.AmbiguousTypeCandidates["Random"], Is.EqualTo(new List<string> { "UnityEngine", "System" }));
        }

        /// <summary>
        /// Verifies a cache hit reports the backend that produced the cached result, after the zeroed stage
        /// timings and before the cache-hit marker.
        /// </summary>
        [TestCase(DynamicCompilationBackendKind.SharedRoslynWorker, "[Perf] Backend: SharedRoslynWorker")]
        [TestCase(DynamicCompilationBackendKind.OneShotRoslyn, "[Perf] Backend: OneShotRoslyn")]
        [TestCase(DynamicCompilationBackendKind.AssemblyBuilderFallback, "[Perf] Backend: AssemblyBuilderFallback")]
        public void CheckCache_ReportsTheCachedBackendInItsTimings(DynamicCompilationBackendKind backendKind, string backendTiming)
        {
            CompilationCacheManager manager = new CompilationCacheManager();
            CompilationRequest request = CreateRequest("return 1;");
            CompilationResult original = CreateSuccess("code");
            original.CompilationBackendKind = backendKind;
            manager.CacheResultIfSuccessful(original, request);

            CompilationResult cached = manager.CheckCache(request);

            Assert.That(cached.Timings, Is.EqualTo(new List<string>
            {
                "[Perf] ReferenceResolution: 0.0ms",
                "[Perf] Build: 0.0ms",
                "[Perf] AssemblyLoad: 0.0ms",
                backendTiming,
                "[Perf] CacheHit: true"
            }));
            Assert.That(cached.CompilationBackendKind, Is.EqualTo(backendKind));
        }

        private static CompilationRequest CreateRequest(string code)
        {
            return new CompilationRequest { Code = code, ClassName = "CachedClass", Namespace = "CachedNs" };
        }

        private static CompilationResult CreateSuccess(string updatedCode)
        {
            return new CompilationResult
            {
                Success = true,
                CompiledAssembly = typeof(object).Assembly,
                UpdatedCode = updatedCode
            };
        }
    }
}
