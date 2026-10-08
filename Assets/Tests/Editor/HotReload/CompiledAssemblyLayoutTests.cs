using System;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers where the compiled assemblies of a project root live: under the root itself for an
    /// ordinary project, and under the main project for a Multiplayer Play Mode Virtual Player.
    /// </summary>
    public sealed class CompiledAssemblyLayoutTests
    {
        // Why under the temp directory: Resolve makes the root absolute, so an absolute fixture keeps
        // the expected values independent of the current directory. Nothing is created on disk.
        private string _workspace;

        [SetUp]
        public void SetUp()
        {
            _workspace = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
        }

        private static string Full(params string[] parts)
        {
            return Path.GetFullPath(Path.Combine(parts));
        }

        /// <summary>
        /// What: an ordinary project reads its compiled assemblies from its own Library/ScriptAssemblies.
        /// </summary>
        [Test]
        public void Resolve_OrdinaryRoot_ReadsItsOwnScriptAssemblies()
        {
            string projectRoot = Path.Combine(_workspace, "project");

            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(projectRoot);

            string expectedDirectory = Full(projectRoot, "Library", "ScriptAssemblies");
            Assert.That(layout.IsVirtualPlayer, Is.False);
            Assert.That(layout.ProjectRoot, Is.EqualTo(Full(projectRoot)));
            Assert.That(layout.MainProjectRoot, Is.EqualTo(layout.ProjectRoot));
            Assert.That(layout.CompiledAssembliesDirectory, Is.EqualTo(expectedDirectory));
            Assert.That(layout.DllPath("A"), Is.EqualTo(Path.Combine(expectedDirectory, "A.dll")));
            Assert.That(layout.PdbPath("A"), Is.EqualTo(Path.Combine(expectedDirectory, "A.pdb")));
        }

        /// <summary>
        /// What: a Virtual Player root reads the main project's Library/ScriptAssemblies and keeps
        /// its own root as ProjectRoot.
        /// </summary>
        [Test]
        public void Resolve_VirtualPlayerRoot_ReadsTheMainProjectsScriptAssemblies()
        {
            string mainRoot = Path.Combine(_workspace, "project");
            string playerRoot = Path.Combine(mainRoot, "Library", "VP", "mppm1");

            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(playerRoot);

            string expectedDirectory = Full(mainRoot, "Library", "ScriptAssemblies");
            Assert.That(layout.IsVirtualPlayer, Is.True);
            Assert.That(layout.ProjectRoot, Is.EqualTo(Full(playerRoot)));
            Assert.That(layout.MainProjectRoot, Is.EqualTo(Full(mainRoot)));
            Assert.That(layout.CompiledAssembliesDirectory, Is.EqualTo(expectedDirectory));
            Assert.That(layout.DllPath("A"), Is.EqualTo(Path.Combine(expectedDirectory, "A.dll")));
            Assert.That(layout.PdbPath("A"), Is.EqualTo(Path.Combine(expectedDirectory, "A.pdb")));
        }

        /// <summary>
        /// What: a trailing separator on a Virtual Player root gives the same layout, and ProjectRoot
        /// does not keep the separator.
        /// </summary>
        [Test]
        public void Resolve_VirtualPlayerRootWithTrailingSeparator_GivesTheSameLayout()
        {
            string mainRoot = Path.Combine(_workspace, "project");
            string playerRoot = Path.Combine(mainRoot, "Library", "VP", "mppm1");

            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(playerRoot + Path.DirectorySeparatorChar);

            Assert.That(layout.IsVirtualPlayer, Is.True);
            Assert.That(layout.ProjectRoot, Is.EqualTo(Full(playerRoot)));
            Assert.That(layout.MainProjectRoot, Is.EqualTo(Full(mainRoot)));
            Assert.That(layout.CompiledAssembliesDirectory, Is.EqualTo(Full(mainRoot, "Library", "ScriptAssemblies")));
        }

        /// <summary>
        /// What: roots that only look like a Virtual Player root (no Library, a lowercase vp, the VP
        /// directory itself, a parent other than VP, a grandparent other than Library) are ordinary
        /// projects that read their own Library/ScriptAssemblies.
        /// </summary>
        [TestCase("VP", "mppm1")]
        [TestCase("Library", "vp", "mppm1")]
        [TestCase("Library", "VP")]
        [TestCase("Library", "Other", "mppm1")]
        [TestCase("Other", "VP", "mppm1")]
        public void Resolve_LookalikeRoot_IsAnOrdinaryProject(params string[] relativeParts)
        {
            string[] parts = new string[relativeParts.Length + 1];
            parts[0] = Path.Combine(_workspace, "project");
            Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
            string projectRoot = Path.Combine(parts);

            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(projectRoot);

            Assert.That(layout.IsVirtualPlayer, Is.False);
            Assert.That(layout.MainProjectRoot, Is.EqualTo(Full(projectRoot)));
            Assert.That(layout.CompiledAssembliesDirectory, Is.EqualTo(Full(projectRoot, "Library", "ScriptAssemblies")));
        }

        /// <summary>
        /// What: a relative Virtual Player root is made absolute before it is recognized.
        /// </summary>
        [Test]
        public void Resolve_RelativeVirtualPlayerRoot_IsRecognizedAndMadeAbsolute()
        {
            string playerRoot = Path.Combine("workspace", "project", "Library", "VP", "mppm1");

            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(playerRoot);

            Assert.That(layout.IsVirtualPlayer, Is.True);
            Assert.That(layout.ProjectRoot, Is.EqualTo(Path.GetFullPath(playerRoot)));
            Assert.That(Path.IsPathRooted(layout.ProjectRoot), Is.True);
            Assert.That(
                layout.CompiledAssembliesDirectory,
                Is.EqualTo(Full("workspace", "project", "Library", "ScriptAssemblies")));
        }

        /// <summary>
        /// What: a root with no parent (a drive or "/" alone) is an ordinary project and stays as given,
        /// instead of throwing.
        /// </summary>
        [Test]
        public void Resolve_RootWithoutAParent_IsAnOrdinaryProject()
        {
            string fileSystemRoot = Path.GetPathRoot(Path.GetTempPath());

            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(fileSystemRoot);

            Assert.That(layout.IsVirtualPlayer, Is.False);
            Assert.That(layout.ProjectRoot, Is.EqualTo(fileSystemRoot));
            Assert.That(layout.MainProjectRoot, Is.EqualTo(fileSystemRoot));
        }

        /// <summary>
        /// What: on Windows, a Virtual Player root is recognized with backslashes and with forward slashes.
        /// </summary>
        [Test]
        public void Resolve_WindowsSeparators_RecognizeAVirtualPlayer()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                Assert.Pass("Windows separator handling applies only on Windows.");
                return;
            }

            Assert.That(
                CompiledAssemblyLayout.Resolve(@"C:\workspace\project\Library\VP\mppm1").IsVirtualPlayer,
                Is.True,
                "Backslash separators must be recognized.");
            Assert.That(
                CompiledAssemblyLayout.Resolve("C:/workspace/project/Library/VP/mppm1").IsVirtualPlayer,
                Is.True,
                "Forward-slash separators must be recognized.");
        }
    }
}
