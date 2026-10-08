using System;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the check that the output path the compilation pipeline reports for an assembly is
    /// the DLL hot reload reads, for an ordinary project and for a Multiplayer Play Mode Virtual Player.
    /// </summary>
    public sealed class HotReloadCompiledAssemblyPathCheckTests
    {
        private string _mainRoot;
        private string _playerRoot;

        [SetUp]
        public void SetUp()
        {
            string workspace = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            _mainRoot = Path.Combine(workspace, "project");
            _playerRoot = Path.Combine(_mainRoot, "Library", "VP", "mppm1");
        }

        /// <summary>
        /// What: an ordinary project whose pipeline reports Library/ScriptAssemblies/A.dll passes.
        /// </summary>
        [Test]
        public void DescribeOutputPathMismatch_OrdinaryRootReportingItsScriptAssemblies_ReturnsNull()
        {
            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(_mainRoot);

            Assert.That(
                HotReloadCompiledAssemblyPathCheck.DescribeOutputPathMismatch(layout, "A", "Library/ScriptAssemblies/A.dll"),
                Is.Null);
        }

        /// <summary>
        /// What: a Virtual Player whose pipeline reports ../../ScriptAssemblies/A.dll, the main
        /// project's DLL, passes.
        /// </summary>
        [Test]
        public void DescribeOutputPathMismatch_VirtualPlayerReportingTheMainProjectsDll_ReturnsNull()
        {
            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(_playerRoot);

            Assert.That(
                HotReloadCompiledAssemblyPathCheck.DescribeOutputPathMismatch(layout, "A", "../../ScriptAssemblies/A.dll"),
                Is.Null);
        }

        /// <summary>
        /// What: a Virtual Player whose pipeline reports a DLL under its own root fails as a Virtual
        /// Player's missing assembly, naming both the reported path and the directory hot reload reads.
        /// </summary>
        [Test]
        public void DescribeOutputPathMismatch_VirtualPlayerReportingItsOwnLibrary_FailsNamingBothPaths()
        {
            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(_playerRoot);

            HotReloadFailureDescription failure =
                HotReloadCompiledAssemblyPathCheck.DescribeOutputPathMismatch(layout, "A", "Library/ScriptAssemblies/A.dll");

            Assert.That(failure, Is.Not.Null);
            Assert.That(
                failure.Kinds,
                Is.EqualTo(HotReloadFailureKinds.CompiledAssemblyMissing | HotReloadFailureKinds.VirtualPlayer));
            Assert.That(
                failure.Message,
                Does.Contain(Path.GetFullPath(Path.Combine(_playerRoot, "Library", "ScriptAssemblies", "A.dll"))));
            Assert.That(failure.Message, Does.Contain(layout.CompiledAssembliesDirectory));
        }

        /// <summary>
        /// What: an absolute output path that names the DLL hot reload reads passes.
        /// </summary>
        [Test]
        public void DescribeOutputPathMismatch_AbsoluteMatchingPath_ReturnsNull()
        {
            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(_mainRoot);

            Assert.That(
                HotReloadCompiledAssemblyPathCheck.DescribeOutputPathMismatch(layout, "A", layout.DllPath("A")),
                Is.Null);
        }

        /// <summary>
        /// What: an ordinary project whose pipeline reports another assembly's DLL fails as a missing
        /// assembly that is not a Virtual Player's.
        /// </summary>
        [Test]
        public void DescribeOutputPathMismatch_OrdinaryRootReportingAnotherAssembly_Fails()
        {
            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(_mainRoot);

            HotReloadFailureDescription failure =
                HotReloadCompiledAssemblyPathCheck.DescribeOutputPathMismatch(layout, "A", "Library/ScriptAssemblies/B.dll");

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.Kinds, Is.EqualTo(HotReloadFailureKinds.CompiledAssemblyMissing));
        }

        /// <summary>
        /// What: when the pipeline reports no output path, nothing is compared.
        /// </summary>
        [TestCase("")]
        [TestCase(null)]
        public void DescribeOutputPathMismatch_NoOutputPath_ReturnsNull(string outputPath)
        {
            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(_mainRoot);

            Assert.That(HotReloadCompiledAssemblyPathCheck.DescribeOutputPathMismatch(layout, "A", outputPath), Is.Null);
        }
    }
}
