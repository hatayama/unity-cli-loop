using System.IO;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers script-path normalization against the real Unity path APIs.
    /// The expected values depend on this development project's own package layout
    /// (the package embedded at Packages/src and the packages it depends on).
    /// </summary>
    public sealed class HotReloadPatchTargetSupportPathTests
    {
        private const string EmbeddedPhysicalPath =
            "Packages/src/Editor/FirstPartyTools/HotReload/HotReloadPatchTargetSupport.cs";
        private const string EmbeddedLogicalPath =
            "Packages/io.github.hatayama.uloopmcp/Editor/FirstPartyTools/HotReload/HotReloadPatchTargetSupport.cs";
        private const string GitPackageLogicalPath =
            "Packages/com.boxqkrtm.ide.cursor/Editor/ProjectGeneration/ProjectGeneration.cs";
        private const string AssetsScriptPath =
            "Assets/Tests/Editor/HotReload/HotReloadToolTests.cs";
        private const string EmbeddedAssemblyName =
            "UnityCLILoop.FirstPartyTools.HotReload.Editor.dll";

        [SetUp]
        public void SetUp()
        {
            // The production run captures these at its entry point; a direct call to the path
            // normalizer in a test has to do the same.
            HotReloadCompositionRoot.Services.PackageRootCapture.CaptureCurrent();
        }

        [Test]
        public void ToProjectRelativeScriptPath_WhenEmbeddedPackagePhysicalPath_ReturnsLogicalPackagePath()
        {
            // Verifies the physical folder of an embedded package is mapped back to its virtual package path.
            string relative = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(EmbeddedPhysicalPath);

            Assert.That(relative, Is.EqualTo(EmbeddedLogicalPath));
        }

        [Test]
        public void ToProjectRelativeScriptPath_WhenEmbeddedPackageLogicalPath_ReturnsItUnchanged()
        {
            // Verifies a logical package path survives normalization instead of collapsing to the physical folder.
            string relative = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(EmbeddedLogicalPath);

            Assert.That(relative, Is.EqualTo(EmbeddedLogicalPath));
        }

        [Test]
        public void ToProjectRelativeScriptPath_WhenGitPackageLogicalPath_ReturnsItUnchanged()
        {
            // Verifies a git package path is not rewritten into its Library/PackageCache location.
            string relative = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(GitPackageLogicalPath);

            Assert.That(relative, Is.EqualTo(GitPackageLogicalPath));
        }

        [Test]
        public void ToProjectRelativeScriptPath_WhenAssetsRelativePath_ReturnsItUnchanged()
        {
            // Verifies an already project-relative Assets path is left alone.
            string relative = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(AssetsScriptPath);

            Assert.That(relative, Is.EqualTo(AssetsScriptPath));
        }

        [Test]
        public void ToProjectRelativeScriptPath_WhenAbsoluteAssetsPath_StripsTheProjectRoot()
        {
            // Verifies an absolute path under Assets becomes project-relative.
            string absolutePath = Path.Combine(Application.dataPath, "Tests/Editor/HotReload/HotReloadToolTests.cs");

            string relative = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(absolutePath);

            Assert.That(relative, Is.EqualTo(AssetsScriptPath));
        }

        [Test]
        public void ToProjectRelativeScriptPath_WhenEmbeddedPackagePhysicalPath_ResolvesTheOwningAssembly()
        {
            // Verifies the normalized path resolves to the package's own assembly instead of Assembly-CSharp-Editor.
            string relative = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(EmbeddedPhysicalPath);

            Assert.That(CompilationPipeline.GetAssemblyNameFromScriptPath(relative), Is.EqualTo(EmbeddedAssemblyName));
        }
    }
}
