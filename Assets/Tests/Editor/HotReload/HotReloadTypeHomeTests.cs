using System.IO;
using System.Reflection;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for <see cref="HotReloadTypeHome"/>: how each home kind finds its live
    /// assembly and how it reports a compiled image that no longer matches it.
    /// </summary>
    public class HotReloadTypeHomeTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        // A Guid no compiled module can carry, so it always reads as a stale image.
        private const string MismatchedMvid = "00000000-0000-0000-0000-000000000000";

        private static Assembly TestAssembly => typeof(HotReloadTypeHomeTests).Assembly;

        private static string LoadedTestAssemblyMvid =>
            TestAssembly.ManifestModule.ModuleVersionId.ToString();

        private static string TestAssemblyDllPath
        {
            get
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.Combine(projectRoot, "Library/ScriptAssemblies", TestAssemblyName + ".dll");
            }
        }

        /// <summary>
        /// What: a ScriptAssemblies home whose compiled Mvid matches the live assembly resolves to that assembly.
        /// </summary>
        [Test]
        public void ResolveLoadedAssembly_ScriptAssembliesHomeWithMatchingMvid_ReturnsLoadedAssembly()
        {
            HotReloadTypeHome home = HotReloadTypeHome.ScriptAssemblies(TestAssemblyName, TestAssemblyDllPath);

            HotReloadLoadedAssemblyResolution resolution = home.ResolveLoadedAssembly(LoadedTestAssemblyMvid);

            Assert.That(resolution.State, Is.EqualTo(HotReloadLoadedAssemblyState.Loaded));
            Assert.That(resolution.Assembly, Is.EqualTo(TestAssembly));
        }

        /// <summary>
        /// What: a ScriptAssemblies home whose compiled Mvid differs from the live assembly reports Stale.
        /// </summary>
        [Test]
        public void ResolveLoadedAssembly_ScriptAssembliesHomeWithMismatchedMvid_ReturnsStale()
        {
            HotReloadTypeHome home = HotReloadTypeHome.ScriptAssemblies(TestAssemblyName, TestAssemblyDllPath);

            HotReloadLoadedAssemblyResolution resolution = home.ResolveLoadedAssembly(MismatchedMvid);

            Assert.That(resolution.State, Is.EqualTo(HotReloadLoadedAssemblyState.Stale));
            Assert.That(resolution.Assembly, Is.Null);
        }

        /// <summary>
        /// What: a ScriptAssemblies home naming an assembly no domain holds reports NotLoaded rather than Stale.
        /// </summary>
        [Test]
        public void ResolveLoadedAssembly_ScriptAssembliesHomeForUnloadedAssembly_ReturnsNotLoaded()
        {
            const string unloadedAssemblyName = "UnityCLILoop.Tests.Editor.HotReload.NotLoadedFixture";
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dllPath = Path.Combine(
                projectRoot,
                "Library/ScriptAssemblies",
                unloadedAssemblyName + ".dll");
            HotReloadTypeHome home = HotReloadTypeHome.ScriptAssemblies(unloadedAssemblyName, dllPath);

            HotReloadLoadedAssemblyResolution resolution = home.ResolveLoadedAssembly(LoadedTestAssemblyMvid);

            Assert.That(resolution.State, Is.EqualTo(HotReloadLoadedAssemblyState.NotLoaded));
            Assert.That(resolution.Assembly, Is.Null);
        }

        /// <summary>
        /// What: a retained-artifact home resolves through the assembly it carries instead of scanning by name.
        /// </summary>
        [Test]
        public void ResolveLoadedAssembly_RetainedArtifactHomeWithMatchingMvid_ReturnsCarriedAssembly()
        {
            HotReloadTypeHome home = HotReloadTypeHome.RetainedArtifact(
                TestAssemblyName,
                TestAssemblyDllPath,
                TestAssembly);

            HotReloadLoadedAssemblyResolution resolution = home.ResolveLoadedAssembly(LoadedTestAssemblyMvid);

            Assert.That(resolution.State, Is.EqualTo(HotReloadLoadedAssemblyState.Loaded));
            Assert.That(resolution.Assembly, Is.EqualTo(TestAssembly));
        }

        /// <summary>
        /// What: a retained-artifact home whose carried assembly no longer matches the compiled Mvid reports Stale.
        /// </summary>
        [Test]
        public void ResolveLoadedAssembly_RetainedArtifactHomeWithMismatchedMvid_ReturnsStale()
        {
            HotReloadTypeHome home = HotReloadTypeHome.RetainedArtifact(
                TestAssemblyName,
                TestAssemblyDllPath,
                TestAssembly);

            HotReloadLoadedAssemblyResolution resolution = home.ResolveLoadedAssembly(MismatchedMvid);

            Assert.That(resolution.State, Is.EqualTo(HotReloadLoadedAssemblyState.Stale));
            Assert.That(resolution.Assembly, Is.Null);
        }
    }
}
