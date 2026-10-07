using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for recognizing a Multiplayer Play Mode Virtual Player by its project root,
    /// and for the missing-assembly reason worded for it.
    /// </summary>
    public class HotReloadVirtualPlayerProjectTests
    {
        private static readonly string VirtualPlayerRoot =
            Path.Combine("workspace", "project", "Library", "VP", "mppm0a1b2c3d");

        private static readonly string OrdinaryProjectRoot = Path.Combine("workspace", "project");

        /// <summary>
        /// What: a player directory directly under Library/VP is recognized as a Virtual Player root.
        /// </summary>
        [Test]
        public void IsVirtualPlayerProjectRoot_PlayerDirectoryUnderLibraryVP_ReturnsTrue()
        {
            Assert.That(HotReloadVirtualPlayerProject.IsVirtualPlayerProjectRoot(VirtualPlayerRoot), Is.True);
        }

        /// <summary>
        /// What: a trailing separator does not shift the parent lookup up by one directory.
        /// </summary>
        [Test]
        public void IsVirtualPlayerProjectRoot_TrailingSeparator_ReturnsTrue()
        {
            string rootWithTrailingSeparator = VirtualPlayerRoot + Path.DirectorySeparatorChar;

            Assert.That(
                HotReloadVirtualPlayerProject.IsVirtualPlayerProjectRoot(rootWithTrailingSeparator),
                Is.True);
        }

        /// <summary>
        /// What: an ordinary project root is not taken for a Virtual Player.
        /// </summary>
        [Test]
        public void IsVirtualPlayerProjectRoot_OrdinaryProjectRoot_ReturnsFalse()
        {
            Assert.That(HotReloadVirtualPlayerProject.IsVirtualPlayerProjectRoot(OrdinaryProjectRoot), Is.False);
        }

        /// <summary>
        /// What: the Library/VP directory itself is not a Virtual Player root.
        /// </summary>
        [Test]
        public void IsVirtualPlayerProjectRoot_TheVPDirectoryItself_ReturnsFalse()
        {
            string virtualPlayersDirectory = Path.Combine("workspace", "project", "Library", "VP");

            Assert.That(
                HotReloadVirtualPlayerProject.IsVirtualPlayerProjectRoot(virtualPlayersDirectory),
                Is.False);
        }

        /// <summary>
        /// What: a directory under Library whose parent is not named VP is not a Virtual Player root.
        /// </summary>
        [Test]
        public void IsVirtualPlayerProjectRoot_ParentIsNotVP_ReturnsFalse()
        {
            string projectRoot = Path.Combine("workspace", "project", "Library", "Other", "mppm0a1b2c3d");

            Assert.That(HotReloadVirtualPlayerProject.IsVirtualPlayerProjectRoot(projectRoot), Is.False);
        }

        /// <summary>
        /// What: a directory under a VP directory that is not inside Library is not a Virtual Player root.
        /// </summary>
        [Test]
        public void IsVirtualPlayerProjectRoot_GrandparentIsNotLibrary_ReturnsFalse()
        {
            string projectRoot = Path.Combine("workspace", "project", "Other", "VP", "mppm0a1b2c3d");

            Assert.That(HotReloadVirtualPlayerProject.IsVirtualPlayerProjectRoot(projectRoot), Is.False);
        }

        /// <summary>
        /// What: a path without a parent or without a grandparent, including one made only of a
        /// separator, is answered false instead of throwing.
        /// </summary>
        [Test]
        public void IsVirtualPlayerProjectRoot_PathTooShortToHaveAGrandparent_ReturnsFalse()
        {
            string playerDirectoryOnly = "mppm0a1b2c3d";
            string virtualPlayersAndPlayerDirectory = Path.Combine("VP", "mppm0a1b2c3d");
            string separatorOnly = Path.DirectorySeparatorChar.ToString();

            Assert.That(
                HotReloadVirtualPlayerProject.IsVirtualPlayerProjectRoot(playerDirectoryOnly),
                Is.False,
                "A path without a parent must not be taken for a Virtual Player root.");
            Assert.That(
                HotReloadVirtualPlayerProject.IsVirtualPlayerProjectRoot(virtualPlayersAndPlayerDirectory),
                Is.False,
                "A path without a grandparent must not be taken for a Virtual Player root.");
            Assert.That(
                HotReloadVirtualPlayerProject.IsVirtualPlayerProjectRoot(separatorOnly),
                Is.False,
                "A path made only of a separator must not be taken for a Virtual Player root.");
        }

        /// <summary>
        /// What: on Windows, a Virtual Player root is recognized with backslashes and with forward slashes.
        /// </summary>
        [Test]
        public void IsVirtualPlayerProjectRoot_WindowsSeparators_ReturnsTrue()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                Assert.Pass("Windows separator handling applies only on Windows.");
                return;
            }

            Assert.That(
                HotReloadVirtualPlayerProject.IsVirtualPlayerProjectRoot(@"C:\workspace\project\Library\VP\mppm0a1b2c3d"),
                Is.True,
                "Backslash separators must be recognized.");
            Assert.That(
                HotReloadVirtualPlayerProject.IsVirtualPlayerProjectRoot("C:/workspace/project/Library/VP/mppm0a1b2c3d"),
                Is.True,
                "Forward-slash separators must be recognized.");
        }

        /// <summary>
        /// What: for a Virtual Player, the reason says hot reload cannot patch it and that a compile
        /// brings the edit in, instead of asking for a compile first.
        /// </summary>
        [Test]
        public void DescribeMissingCompiledAssembly_VirtualPlayerRoot_SaysHotReloadCannotPatchAVirtualPlayer()
        {
            string dllPath = Path.Combine(VirtualPlayerRoot, "Library", "ScriptAssemblies", "Sample.dll");

            string reason = HotReloadVirtualPlayerProject.DescribeMissingCompiledAssembly(VirtualPlayerRoot, dllPath);

            Assert.That(reason, Does.Contain(dllPath));
            Assert.That(reason, Does.Contain("Virtual Player"));
            Assert.That(reason, Does.Contain("main Editor"));
            Assert.That(reason, Does.Contain("compile"));
            Assert.That(reason, Does.Not.Contain("Compile the project first"));
        }

        /// <summary>
        /// What: for an ordinary project, the reason keeps the compile-first text unchanged.
        /// </summary>
        [Test]
        public void DescribeMissingCompiledAssembly_OrdinaryRoot_KeepsTheCompileFirstText()
        {
            string dllPath = Path.Combine(OrdinaryProjectRoot, "Library", "ScriptAssemblies", "Sample.dll");

            string reason = HotReloadVirtualPlayerProject.DescribeMissingCompiledAssembly(OrdinaryProjectRoot, dllPath);

            Assert.That(
                reason,
                Is.EqualTo("Compiled assembly not found at '" + dllPath + "'. Compile the project first."));
        }
    }
}
