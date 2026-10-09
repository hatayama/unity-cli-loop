using System;
using System.IO;
using System.Threading;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Contract of the referenced-method set warm-up item: it preloads the set of every
    /// referencing dll of every target into the index it is given, once. Uses an index that
    /// persists into a private temp directory, so the project's persisted sets are never read.
    /// </summary>
    public sealed class HotReloadReferencedMethodSetWarmUpItemTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string CrossAssemblyCallerAssemblyName = "UnityCLILoop.Tests.Editor.HotReload.CallSiteCrossAssembly";

        private string _persistenceDirectory;

        [SetUp]
        public void SetUp()
        {
            _persistenceDirectory = Path.Combine(Path.GetTempPath(), "uloop-set-warm-up-" + Guid.NewGuid().ToString("N"));
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
        /// What: each referencing dll is read once, and preloading again reads none of them.
        /// </summary>
        [Test]
        public void PreloadSets_ReadsEachReferencingDllOnce_AndAgainReadsNothing()
        {
            HotReloadReferencedMethodIndex index = new HotReloadReferencedMethodIndex(_persistenceDirectory);
            HotReloadWarmUpTarget[] targets =
            {
                new HotReloadWarmUpTarget(
                    TestAssemblyName,
                    DllPath(TestAssemblyName),
                    Path.ChangeExtension(DllPath(TestAssemblyName), ".pdb"),
                    new[] { DllPath(CrossAssemblyCallerAssemblyName), DllPath(TestAssemblyName) })
            };

            HotReloadReferencedMethodSetWarmUpItem.PreloadSets(targets, index, CancellationToken.None);
            int afterFirst = index.LoadCount;
            HotReloadReferencedMethodSetWarmUpItem.PreloadSets(targets, index, CancellationToken.None);

            Assert.That(afterFirst, Is.EqualTo(2), "reads after the first preload");
            Assert.That(index.LoadCount, Is.EqualTo(2), "reads after the second preload");
        }

        /// <summary>
        /// What: a cancelled token throws before the first referencing dll, so the item is not reported done, and reads nothing.
        /// </summary>
        [Test]
        public void PreloadSets_WhenCancelled_ThrowsAndReadsNothing()
        {
            HotReloadReferencedMethodIndex index = new HotReloadReferencedMethodIndex(_persistenceDirectory);
            HotReloadWarmUpTarget[] targets =
            {
                new HotReloadWarmUpTarget(
                    TestAssemblyName,
                    DllPath(TestAssemblyName),
                    Path.ChangeExtension(DllPath(TestAssemblyName), ".pdb"),
                    new[] { DllPath(CrossAssemblyCallerAssemblyName) })
            };

            Assert.Throws<OperationCanceledException>(
                () => HotReloadReferencedMethodSetWarmUpItem.PreloadSets(targets, index, new CancellationToken(true)));

            Assert.That(index.LoadCount, Is.EqualTo(0), "reads");
        }

        private static string DllPath(string assemblyName)
        {
            return Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + HotReloadConstants.CompiledAssemblyExtension);
        }
    }
}
