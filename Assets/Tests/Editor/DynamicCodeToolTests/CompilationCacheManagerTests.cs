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
    }
}
