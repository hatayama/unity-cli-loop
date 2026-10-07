using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies which query ids are rewritten to the asset path, and when the package roots are read.
    /// </summary>
    public sealed class SourcePausePointIdTests
    {
        private static readonly string ProjectRoot = Path.GetFullPath("/proj");

        // A local package whose folder sits inside the project root but outside Packages/.
        private static readonly ScriptPackageRoot LocalPackage = new ScriptPackageRoot(
            Path.Combine(ProjectRoot, "LocalPackages", "foo"),
            "Packages/com.example.foo");

        /// <summary>
        /// What: the project-relative folder path of a local package outside Packages/ is rewritten
        /// to the package's asset path.
        /// </summary>
        [Test]
        public void ToMarkerId_FolderPathOfAPackageOutsidePackagesFolder_RewritesToTheAssetPath()
        {
            string markerId = SourcePausePointId.ToMarkerId(
                "LocalPackages/foo/Runtime/X.cs:3", ProjectRoot, NotRegistered, () => new[] { LocalPackage });

            Assert.That(markerId, Is.EqualTo("Packages/com.example.foo/Runtime/X.cs:3"));
        }

        /// <summary>
        /// What: a plain Assets path comes back as given without reading the package roots, since an
        /// await polls status every second.
        /// </summary>
        [Test]
        public void ToMarkerId_AssetsPath_ReturnsItWithoutReadingPackageRoots()
        {
            string markerId = SourcePausePointId.ToMarkerId(
                "Assets/X.cs:3", ProjectRoot, NotRegistered, FailWhenRead);

            Assert.That(markerId, Is.EqualTo("Assets/X.cs:3"));
        }

        /// <summary>
        /// What: a package script already named by its asset path comes back as given after the roots are read.
        /// </summary>
        [Test]
        public void ToMarkerId_PackageAssetPath_ReturnsItUnchanged()
        {
            string markerId = SourcePausePointId.ToMarkerId(
                "Packages/com.example.foo/Runtime/X.cs:3", ProjectRoot, NotRegistered, () => new[] { LocalPackage });

            Assert.That(markerId, Is.EqualTo("Packages/com.example.foo/Runtime/X.cs:3"));
        }

        private static bool NotRegistered(string id)
        {
            return false;
        }

        private static IReadOnlyList<ScriptPackageRoot> FailWhenRead()
        {
            Assert.Fail("The package roots must not be read for an Assets path.");
            return Array.Empty<ScriptPackageRoot>();
        }
    }
}
