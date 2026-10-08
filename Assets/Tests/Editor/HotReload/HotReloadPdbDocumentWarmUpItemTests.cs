using System;
using System.IO;
using System.Threading;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Contract of the PDB document warm-up item: it preloads each target's document list into
    /// the index it is given, once. Uses an index that persists into a private temp directory,
    /// so the project's persisted lists are never read.
    /// </summary>
    public sealed class HotReloadPdbDocumentWarmUpItemTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        private string _persistenceDirectory;

        [SetUp]
        public void SetUp()
        {
            _persistenceDirectory = Path.Combine(Path.GetTempPath(), "uloop-pdb-warm-up-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_persistenceDirectory))
            {
                Directory.Delete(_persistenceDirectory, recursive: true);
            }
        }

        /// <summary>
        /// What: a target's dll and PDB are read once, and preloading again reads nothing.
        /// </summary>
        [Test]
        public void PreloadDocuments_ReadsThePdbOnce_AndAgainReadsNothing()
        {
            HotReloadPdbDocumentIndex index = new HotReloadPdbDocumentIndex(_persistenceDirectory);
            string dllPath = Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                TestAssemblyName + HotReloadConstants.CompiledAssemblyExtension);
            HotReloadWarmUpTarget[] targets =
            {
                new HotReloadWarmUpTarget(TestAssemblyName, dllPath, Path.ChangeExtension(dllPath, ".pdb"), Array.Empty<string>())
            };

            HotReloadPdbDocumentWarmUpItem.PreloadDocuments(targets, index, CancellationToken.None);
            int afterFirst = index.LoadCount;
            HotReloadPdbDocumentWarmUpItem.PreloadDocuments(targets, index, CancellationToken.None);

            Assert.That(afterFirst, Is.EqualTo(1), "reads after the first preload");
            Assert.That(index.LoadCount, Is.EqualTo(1), "reads after the second preload");
        }
    }
}
