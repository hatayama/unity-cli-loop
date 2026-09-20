using System.Collections.Generic;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies compilation cache clones preserve auto-injected namespace attributions.
    /// </summary>
    [TestFixture]
    public sealed class CompilationCacheManagerTests
    {
        /// <summary>
        /// What: a cached compilation result clone keeps IsSpeculative=true on copied
        /// auto-injected namespace attributions.
        /// </summary>
        [Test]
        public void CheckCache_WhenSpeculativeAttributionIsCached_PreservesIsSpeculative()
        {
            CompilationCacheManager manager = new();
            CompilationRequest request = new()
            {
                Code = "return 1;",
                ClassName = "CachedClass",
                Namespace = "CachedNs"
            };
            CompilationResult original = new()
            {
                Success = true,
                CompiledAssembly = typeof(object).Assembly,
                AutoInjectedNamespaces = new List<AutoInjectedNamespace>
                {
                    new AutoInjectedNamespace("System.Text", "StringBuilder", true)
                }
            };

            manager.CacheResultIfSuccessful(original, request);
            CompilationResult cached = manager.CheckCache(request);

            Assert.That(cached, Is.Not.Null);
            Assert.That(cached.AutoInjectedNamespaces, Is.Not.SameAs(original.AutoInjectedNamespaces));
            Assert.That(cached.AutoInjectedNamespaces.Count, Is.EqualTo(1));
            Assert.That(cached.AutoInjectedNamespaces[0].Namespace, Is.EqualTo("System.Text"));
            Assert.That(cached.AutoInjectedNamespaces[0].TriggerIdentifier, Is.EqualTo("StringBuilder"));
            Assert.That(cached.AutoInjectedNamespaces[0].IsSpeculative, Is.True);
        }

        /// <summary>
        /// What: the same snippet compiled against a different set of additional references gets a
        /// different cache key, so a result compiled against one generation of hot-reload introduced
        /// types is not reused once that generation is gone.
        /// </summary>
        [Test]
        public void GenerateCacheKey_WhenAdditionalReferencesDiffer_DiffersToo()
        {
            CompilationCacheManager manager = new();
            CompilationRequest withoutReferences = new()
            {
                Code = "return 1;",
                ClassName = "CachedClass",
                Namespace = "CachedNs"
            };
            CompilationRequest withFirstGeneration = new()
            {
                Code = "return 1;",
                ClassName = "CachedClass",
                Namespace = "CachedNs",
                AdditionalReferences = new List<string> { "Library/Introduced/Generation1.dll" }
            };
            CompilationRequest withSecondGeneration = new()
            {
                Code = "return 1;",
                ClassName = "CachedClass",
                Namespace = "CachedNs",
                AdditionalReferences = new List<string> { "Library/Introduced/Generation2.dll" }
            };

            string keyWithoutReferences = manager.GenerateCacheKey(withoutReferences);
            string keyWithFirstGeneration = manager.GenerateCacheKey(withFirstGeneration);
            string keyWithSecondGeneration = manager.GenerateCacheKey(withSecondGeneration);

            Assert.That(keyWithFirstGeneration, Is.Not.EqualTo(keyWithoutReferences));
            Assert.That(keyWithSecondGeneration, Is.Not.EqualTo(keyWithFirstGeneration));
        }

        /// <summary>
        /// What: the key does not depend on the order the additional references arrive in, so a
        /// reordered but identical reference set still hits the cache.
        /// </summary>
        [Test]
        public void GenerateCacheKey_WhenAdditionalReferencesAreReordered_StaysTheSame()
        {
            CompilationCacheManager manager = new();
            CompilationRequest ascending = new()
            {
                Code = "return 1;",
                ClassName = "CachedClass",
                Namespace = "CachedNs",
                AdditionalReferences = new List<string>
                {
                    "Library/Introduced/One.dll",
                    "Library/Introduced/Two.dll"
                }
            };
            CompilationRequest descending = new()
            {
                Code = "return 1;",
                ClassName = "CachedClass",
                Namespace = "CachedNs",
                AdditionalReferences = new List<string>
                {
                    "Library/Introduced/Two.dll",
                    "Library/Introduced/One.dll"
                }
            };

            Assert.That(
                manager.GenerateCacheKey(descending),
                Is.EqualTo(manager.GenerateCacheKey(ascending)));
        }
    }
}
