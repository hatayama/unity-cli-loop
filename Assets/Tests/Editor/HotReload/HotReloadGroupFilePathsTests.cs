using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for resolving a worker row's file identity to the path outcomes report.
    /// </summary>
    public class HotReloadGroupFilePathsTests
    {
        /// <summary>
        /// What: each file of a group resolves to its own assembly-resolve path, so an outcome of
        /// one file is never reported under another file of the same group.
        /// </summary>
        [Test]
        public void ResolveAssemblyResolvePath_TwoFiles_ResolvesEachToItsOwnPath()
        {
            HotReloadGroupFilePaths paths = new HotReloadGroupFilePaths(
                new List<(string ProjectRelativePath, string AssemblyResolvePath)>
                {
                    ("Assets/Scripts/First.cs", "Assets/Scripts/First.cs"),
                    ("Assets/Scripts/Second.cs", "Packages/dev.example/Second.cs")
                });

            Assert.That(
                paths.ResolveAssemblyResolvePath("Assets/Scripts/First.cs"),
                Is.EqualTo("Assets/Scripts/First.cs"));
            Assert.That(
                paths.ResolveAssemblyResolvePath("Assets/Scripts/Second.cs"),
                Is.EqualTo("Packages/dev.example/Second.cs"));
        }

        /// <summary>
        /// What: a single-file group resolves its one row set, which is what a run of one edited
        /// file produces.
        /// </summary>
        [Test]
        public void ForSingleFile_ResolvesThatFile()
        {
            HotReloadGroupFilePaths paths = HotReloadGroupFilePaths.ForSingleFile(
                "Assets/Scripts/Only.cs",
                "Assets/Scripts/Only.cs");

            Assert.That(
                paths.ResolveAssemblyResolvePath("Assets/Scripts/Only.cs"),
                Is.EqualTo("Assets/Scripts/Only.cs"));
        }
    }
}
