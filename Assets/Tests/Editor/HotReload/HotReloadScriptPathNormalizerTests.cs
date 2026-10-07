using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the pure mapping between a script's physical path and the project-relative asset
    /// path Unity's script APIs use, in both directions.
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
        public void ToProjectRelative_WhenPackageResolvesOutsideTheProject_ReturnsTheVirtualPackagePath()
        {
            // Verifies a file: package whose folder lives outside the project root still maps to its virtual path.
            string relative = HotReloadScriptPathNormalizer.ToProjectRelative(
                "/MyPackage/Runtime/Foo.cs",
                "/proj",
                PackageRoots(("/MyPackage", "Packages/com.example.core")),
                StringComparison.Ordinal);

            Assert.That(relative, Is.EqualTo("Packages/com.example.core/Runtime/Foo.cs"));
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

        [Test]
        public void ToPhysicalProjectRelative_AssetsPath_IsReturnedUnchanged()
        {
            // Verifies an Assets path, which has no virtual root, keeps the exact string the snapshot and the PDB already agree on.
            string physical = HotReloadScriptPathNormalizer.ToPhysicalProjectRelative(
                "Assets/Scripts/A.cs",
                "/proj",
                PackageRoots(("/proj/Packages/src", "Packages/io.example.pkg")),
                StringComparison.Ordinal);

            Assert.That(physical, Is.EqualTo("Assets/Scripts/A.cs"));
        }

        [Test]
        public void ToPhysicalProjectRelative_EmbeddedPackage_MapsToTheFolderUnderPackages()
        {
            // Verifies an embedded package's virtual path maps to its folder, whose name differs from the package name.
            string physical = HotReloadScriptPathNormalizer.ToPhysicalProjectRelative(
                "Packages/io.example.pkg/Runtime/A.cs",
                "/proj",
                PackageRoots(("/proj/Packages/src", "Packages/io.example.pkg")),
                StringComparison.Ordinal);

            Assert.That(physical, Is.EqualTo("Packages/src/Runtime/A.cs"));
        }

        [Test]
        public void ToPhysicalProjectRelative_LocalPackageInsideTheProject_MapsToTheProjectRelativeFolder()
        {
            // Verifies a file: package that lives in the project outside Packages maps to its project-relative folder.
            string physical = HotReloadScriptPathNormalizer.ToPhysicalProjectRelative(
                "Packages/io.example.pkg/Runtime/A.cs",
                "/proj",
                PackageRoots(("/proj/Modules/pkg", "Packages/io.example.pkg")),
                StringComparison.Ordinal);

            Assert.That(physical, Is.EqualTo("Modules/pkg/Runtime/A.cs"));
        }

        [Test]
        public void ToPhysicalProjectRelative_LocalPackageOutsideTheProject_ReturnsTheAbsolutePath()
        {
            // Verifies a file: package outside the project root yields the absolute path, since no project-relative path names it.
            string physical = HotReloadScriptPathNormalizer.ToPhysicalProjectRelative(
                "Packages/io.example.pkg/Runtime/A.cs",
                "/proj",
                PackageRoots(("/elsewhere/pkg", "Packages/io.example.pkg")),
                StringComparison.Ordinal);

            Assert.That(physical, Is.EqualTo("/elsewhere/pkg/Runtime/A.cs"));
        }

        [Test]
        public void ToPhysicalProjectRelative_PackageNameThatPrefixesAnother_DoesNotMatchTheLongerName()
        {
            // Verifies a package whose name prefixes another's is not taken for the longer one, even when it is listed first.
            string physical = HotReloadScriptPathNormalizer.ToPhysicalProjectRelative(
                "Packages/io.example.pkg.extra/Runtime/A.cs",
                "/proj",
                PackageRoots(
                    ("/proj/Packages/short-folder", "Packages/io.example.pkg"),
                    ("/proj/Packages/long-folder", "Packages/io.example.pkg.extra")),
                StringComparison.Ordinal);

            Assert.That(physical, Is.EqualTo("Packages/long-folder/Runtime/A.cs"));
        }

        [Test]
        public void ToPhysicalProjectRelative_NoMatchingRoot_IsReturnedUnchanged()
        {
            // Verifies a package path that no registered package claims is left as is rather than guessed at.
            string physical = HotReloadScriptPathNormalizer.ToPhysicalProjectRelative(
                "Packages/io.example.unregistered/Runtime/A.cs",
                "/proj",
                PackageRoots(("/proj/Packages/src", "Packages/io.example.pkg")),
                StringComparison.Ordinal);

            Assert.That(physical, Is.EqualTo("Packages/io.example.unregistered/Runtime/A.cs"));
        }

        [Test]
        public void ToPhysicalProjectRelative_OnWindowsSeparators_NormalizesToForwardSlashes()
        {
            // Verifies Windows separators in the path, the project root, and the package root all come out as forward slashes.
            string physical = HotReloadScriptPathNormalizer.ToPhysicalProjectRelative(
                "Packages\\io.example.pkg\\Runtime\\A.cs",
                "C:\\proj",
                PackageRoots(("C:\\proj\\Packages\\src", "Packages\\io.example.pkg")),
                StringComparison.OrdinalIgnoreCase);

            Assert.That(physical, Is.EqualTo("Packages/src/Runtime/A.cs"));
        }
    }
}
