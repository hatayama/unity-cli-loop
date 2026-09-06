using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers artifact output isolation without creating files during planning.
    /// </summary>
    public class HotReloadIntroducedTypeArtifactPathFactoryTests
    {
        /// <summary>
        /// Verifies that separate preparation attempts receive different assembly names and retain
        /// the DLL/PDB pair beneath the introduced-types lifetime directory.
        /// </summary>
        [Test]
        public void Create_SeparateBatches_UsesUniqueAssemblyAndPairedPaths()
        {
            HotReloadIntroducedTypeArtifactPathFactory factory =
                new HotReloadIntroducedTypeArtifactPathFactory("project", "session");

            HotReloadIntroducedTypeArtifactPaths first = factory.Create();
            HotReloadIntroducedTypeArtifactPaths second = factory.Create();

            Assert.That(first.AssemblyName, Is.Not.EqualTo(second.AssemblyName));
            Assert.That(first.DllPath, Does.Contain("IntroducedTypes"));
            Assert.That(first.DllPath, Does.EndWith(first.AssemblyName + ".dll"));
            Assert.That(first.PdbPath, Does.EndWith(first.AssemblyName + ".pdb"));
            Assert.That(first.SourcePath, Does.EndWith(first.AssemblyName + ".cs"));
            Assert.That(first.AssemblyFullName, Does.StartWith(first.AssemblyName + ", Version=0.0.0.0"));
        }
    }
}
