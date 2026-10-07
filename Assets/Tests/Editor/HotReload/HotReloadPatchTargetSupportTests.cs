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
        /// Verifies that a loaded assembly whose MVID differs from the compiled dll is reported as
        /// stale, and as the Editor not being ready, because a compile or a reload replaced it.
        /// </summary>
        [Test]
        public void CheckMvidGuard_WhenLoadedAssemblyMvidDiffersFromDll_ReturnsStaleHint()
        {
            string loadedAssemblyName = typeof(HotReloadPatchTargetSupport).Assembly.GetName().Name;
            string otherDllPath = typeof(AssemblyDefinition).Assembly.Location;
            HotReloadTypeHome home = HotReloadTypeHome.ScriptAssemblies(loadedAssemblyName, otherDllPath);

            HotReloadFailureDescription failure = HotReloadPatchTargetSupport.CheckMvidGuard(home);

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.Message, Is.EqualTo(HotReloadConstants.StaleAssemblyHint));
            Assert.That(failure.Kinds, Is.EqualTo(HotReloadFailureKinds.EditorNotReady));
        }

        /// <summary>
        /// Verifies that a compiled dll whose assembly name is not loaded in the domain is reported as
        /// not loaded, and as a Declaration, because only the reader's code path loads it.
        /// </summary>
        [Test]
        public void CheckMvidGuard_WhenAssemblyIsNotLoaded_ReturnsNotLoadedHint()
        {
            string notLoadedAssemblyName = "UloopTests.NeverLoaded." + Guid.NewGuid().ToString("N");
            string dllPath = typeof(HotReloadPatchTargetSupport).Assembly.Location;
            HotReloadTypeHome home = HotReloadTypeHome.ScriptAssemblies(notLoadedAssemblyName, dllPath);

            HotReloadFailureDescription failure = HotReloadPatchTargetSupport.CheckMvidGuard(home);

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.Message, Is.EqualTo(HotReloadConstants.AssemblyNotLoadedHint));
            Assert.That(failure.Kinds, Is.EqualTo(HotReloadFailureKinds.Declaration));
        }
    }
}
