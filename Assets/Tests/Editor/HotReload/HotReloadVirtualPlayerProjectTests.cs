using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the missing-assembly reason worded for an ordinary project and for a
    /// Multiplayer Play Mode Virtual Player.
    /// </summary>
    public class HotReloadVirtualPlayerProjectTests
    {
        private static readonly string VirtualPlayerRoot =
            Path.Combine("workspace", "project", "Library", "VP", "mppm0a1b2c3d");

        private static readonly string OrdinaryProjectRoot = Path.Combine("workspace", "project");

        /// <summary>
        /// What: for a Virtual Player, the reason says hot reload cannot patch it and that a compile
        /// brings the edit in, instead of asking for a compile first, and the failure is marked as a
        /// Virtual Player's so the next step names the main Editor's project.
        /// </summary>
        [Test]
        public void DescribeMissingCompiledAssembly_VirtualPlayerRoot_SaysHotReloadCannotPatchAVirtualPlayer()
        {
            string dllPath = Path.Combine(VirtualPlayerRoot, "Library", "ScriptAssemblies", "Sample.dll");

            HotReloadFailureDescription failure =
                HotReloadVirtualPlayerProject.DescribeMissingCompiledAssembly(VirtualPlayerRoot, dllPath);
            string reason = failure.Message;

            Assert.That(
                failure.Kinds,
                Is.EqualTo(HotReloadFailureKinds.CompiledAssemblyMissing | HotReloadFailureKinds.VirtualPlayer));
            Assert.That(reason, Does.Contain(dllPath));
            Assert.That(reason, Does.Contain("Virtual Player"));
            Assert.That(reason, Does.Contain("main Editor"));
            Assert.That(reason, Does.Contain("compile"));
            Assert.That(reason, Does.Not.Contain("Compile the project first"));
        }

        /// <summary>
        /// What: for an ordinary project, the reason keeps the compile-first text unchanged, and the
        /// failure is not marked as a Virtual Player's.
        /// </summary>
        [Test]
        public void DescribeMissingCompiledAssembly_OrdinaryRoot_KeepsTheCompileFirstText()
        {
            string dllPath = Path.Combine(OrdinaryProjectRoot, "Library", "ScriptAssemblies", "Sample.dll");

            HotReloadFailureDescription failure =
                HotReloadVirtualPlayerProject.DescribeMissingCompiledAssembly(OrdinaryProjectRoot, dllPath);

            Assert.That(failure.Kinds, Is.EqualTo(HotReloadFailureKinds.CompiledAssemblyMissing));
            Assert.That(
                failure.Message,
                Is.EqualTo("Compiled assembly not found at '" + dllPath + "'. Compile the project first."));
        }
    }
}
