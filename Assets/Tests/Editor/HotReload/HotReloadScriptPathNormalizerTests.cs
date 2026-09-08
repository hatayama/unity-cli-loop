using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the pure normalization of an absolute path into a project-relative script path.
    /// </summary>
    public sealed class HotReloadScriptPathNormalizerTests
    {
        private static IReadOnlyList<HotReloadPackageRoot> PackageRoots(params (string Resolved, string Asset)[] roots)
        {
            List<HotReloadPackageRoot> mapped = new List<HotReloadPackageRoot>(roots.Length);
            foreach ((string Resolved, string Asset) root in roots)
            {
                mapped.Add(new HotReloadPackageRoot(root.Resolved, root.Asset));
            }

            return mapped;
        }

        [Test]
        public void ToProjectRelative_WhenPathIsInsideACachedPackage_ReturnsTheVirtualPackagePath()
        {
            // Verifies a package's physical folder is mapped back to the Packages/<id> path Unity expects.
            string relative = HotReloadScriptPathNormalizer.ToProjectRelative(
                "/proj/Library/PackageCache/com.example.core@abc123/Runtime/Foo.cs",
                "/proj",
                PackageRoots(("/proj/Library/PackageCache/com.example.core@abc123", "Packages/com.example.core")),
                StringComparison.Ordinal);

            Assert.That(relative, Is.EqualTo("Packages/com.example.core/Runtime/Foo.cs"));
        }

        [Test]
        public void ToProjectRelative_WhenPackageIsEmbeddedUnderTheProjectRoot_PrefersTheVirtualPackagePath()
        {
            // Verifies an embedded package is not reduced to its physical folder, which resolves to the wrong assembly.
            string relative = HotReloadScriptPathNormalizer.ToProjectRelative(
                "/proj/Packages/src/Editor/Foo.cs",
                "/proj",
                PackageRoots(("/proj/Packages/src", "Packages/com.example.core")),
                StringComparison.Ordinal);

            Assert.That(relative, Is.EqualTo("Packages/com.example.core/Editor/Foo.cs"));
        }

        [Test]
        public void ToProjectRelative_WhenAbsoluteAssetsPath_StripsTheProjectRoot()
        {
            // Verifies an absolute Assets path under the project root becomes project-relative.
            string relative = HotReloadScriptPathNormalizer.ToProjectRelative(
                "/proj/Assets/Scripts/Foo.cs",
                "/proj",
                PackageRoots(),
                StringComparison.Ordinal);

            Assert.That(relative, Is.EqualTo("Assets/Scripts/Foo.cs"));
        }

        [Test]
        public void ToProjectRelative_WhenProjectRootEndsWithSlash_StripsTheSameRoot()
        {
            // Verifies a trailing slash on the project root does not change the result.
            string relative = HotReloadScriptPathNormalizer.ToProjectRelative(
                "/proj/Assets/Scripts/Foo.cs",
                "/proj/",
                PackageRoots(),
                StringComparison.Ordinal);

            Assert.That(relative, Is.EqualTo("Assets/Scripts/Foo.cs"));
        }

        [Test]
        public void ToProjectRelative_WhenPathUsesBackslashes_NormalizesSeparators()
        {
            // Verifies Windows separators in the path, the root, and a package root are all normalized.
            string relative = HotReloadScriptPathNormalizer.ToProjectRelative(
                "C:\\proj\\Assets\\Scripts\\Foo.cs",
                "C:\\proj",
                PackageRoots(("C:\\proj\\Packages\\src", "Packages/com.example.core")),
                StringComparison.OrdinalIgnoreCase);

            Assert.That(relative, Is.EqualTo("Assets/Scripts/Foo.cs"));
        }

        [Test]
        public void ToProjectRelative_WhenWindowsCaseDiffers_StillMapsThePackageRoot()
        {
            // Verifies the Windows comparison ignores case differences against a package root.
            string relative = HotReloadScriptPathNormalizer.ToProjectRelative(
                "c:/PROJ/Packages/SRC/Editor/Foo.cs",
                "C:/proj",
                PackageRoots(("C:/proj/Packages/src", "Packages/com.example.core")),
                StringComparison.OrdinalIgnoreCase);

            Assert.That(relative, Is.EqualTo("Packages/com.example.core/Editor/Foo.cs"));
        }

        [Test]
        public void ToProjectRelative_WhenPathIsOutsideTheProject_OnlyNormalizesSeparators()
        {
            // Verifies a path outside the project root is returned as is, so the caller hits the existing failure path.
            string relative = HotReloadScriptPathNormalizer.ToProjectRelative(
                "/other/Assets/Scripts/Foo.cs",
                "/proj",
                PackageRoots(),
                StringComparison.Ordinal);

            Assert.That(relative, Is.EqualTo("/other/Assets/Scripts/Foo.cs"));
        }
    }
}
