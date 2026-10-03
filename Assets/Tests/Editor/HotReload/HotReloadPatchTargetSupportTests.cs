using System;

using Mono.Cecil;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers source-path expansion and the compiled-assembly MVID guard of the patch target resolver.
    /// </summary>
    public sealed class HotReloadPatchTargetSupportTests
    {
        /// <summary>
        /// Verifies that an assembly without a source list expands to an empty path array.
        /// </summary>
        [Test]
        public void BuildAssemblySourcePaths_WhenSourceFilesAreNull_ReturnsEmptyArray()
        {
            string[] paths = HotReloadPatchTargetSupport.BuildAssemblySourcePaths("<PROJECT_ROOT>", null);

            Assert.That(paths, Is.Not.Null);
            Assert.That(paths, Is.Empty);
        }

        /// <summary>
        /// Verifies that a loaded assembly whose MVID differs from the compiled dll is reported as stale.
        /// </summary>
        [Test]
        public void CheckMvidGuard_WhenLoadedAssemblyMvidDiffersFromDll_ReturnsStaleHint()
        {
            string loadedAssemblyName = typeof(HotReloadPatchTargetSupport).Assembly.GetName().Name;
            string otherDllPath = typeof(AssemblyDefinition).Assembly.Location;
            HotReloadTypeHome home = HotReloadTypeHome.ScriptAssemblies(loadedAssemblyName, otherDllPath);

            string error = HotReloadPatchTargetSupport.CheckMvidGuard(home);

            Assert.That(error, Is.EqualTo(HotReloadConstants.StaleAssemblyHint));
        }

        /// <summary>
        /// Verifies that a compiled dll whose assembly name is not loaded in the domain is reported as not loaded.
        /// </summary>
        [Test]
        public void CheckMvidGuard_WhenAssemblyIsNotLoaded_ReturnsNotLoadedHint()
        {
            string notLoadedAssemblyName = "UloopTests.NeverLoaded." + Guid.NewGuid().ToString("N");
            string dllPath = typeof(HotReloadPatchTargetSupport).Assembly.Location;
            HotReloadTypeHome home = HotReloadTypeHome.ScriptAssemblies(notLoadedAssemblyName, dllPath);

            string error = HotReloadPatchTargetSupport.CheckMvidGuard(home);

            Assert.That(error, Is.EqualTo(HotReloadConstants.AssemblyNotLoadedHint));
        }
    }
}
