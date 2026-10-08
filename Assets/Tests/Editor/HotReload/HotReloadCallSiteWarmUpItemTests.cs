using System;
using System.IO;
using System.Threading;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Contract of the call-site warm-up item: it loads each target's dll into the cache it is
    /// given once, and throws before the next dll once cancelled. Works on a copy of the test assembly in a
    /// private temp directory and a cache of its own.
    /// </summary>
    public sealed class HotReloadCallSiteWarmUpItemTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        private string _tempDirectory;
        private HotReloadCompiledCallSiteCache _cache;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "uloop-call-site-warm-up-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _cache = new HotReloadCompiledCallSiteCache(long.MaxValue);
        }

        [TearDown]
        public void TearDown()
        {
            _cache.Clear();
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        /// <summary>
        /// What: a target's dll is loaded once, and loading again into the same cache reads nothing.
        /// </summary>
        [Test]
        public void LoadCallSites_LoadsTheDllOnce_AndAgainReadsNothing()
        {
            HotReloadWarmUpTarget[] targets = { CopyTarget() };

            int first = HotReloadCallSiteWarmUpItem.LoadCallSites(targets, _cache, CancellationToken.None);
            int afterFirst = _cache.LoadCount;
            HotReloadCallSiteWarmUpItem.LoadCallSites(targets, _cache, CancellationToken.None);

            Assert.That(first, Is.EqualTo(1), "loaded targets");
            Assert.That(afterFirst, Is.EqualTo(1), "reads after the first load");
            Assert.That(_cache.LoadCount, Is.EqualTo(1), "reads after the second load");
        }

        /// <summary>
        /// What: a cancelled token throws before the first dll, so the item is not reported done, and loads nothing.
        /// </summary>
        [Test]
        public void LoadCallSites_WhenCancelled_ThrowsAndLoadsNothing()
        {
            HotReloadWarmUpTarget[] targets = { CopyTarget() };

            Assert.Throws<OperationCanceledException>(
                () => HotReloadCallSiteWarmUpItem.LoadCallSites(targets, _cache, new CancellationToken(true)));

            Assert.That(_cache.LoadCount, Is.EqualTo(0), "reads");
        }

        private HotReloadWarmUpTarget CopyTarget()
        {
            string source = Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                TestAssemblyName + HotReloadConstants.CompiledAssemblyExtension);
            string dllPath = Path.Combine(_tempDirectory, Path.GetFileName(source));
            File.Copy(source, dllPath);
            return new HotReloadWarmUpTarget(
                TestAssemblyName,
                dllPath,
                Path.ChangeExtension(dllPath, ".pdb"),
                Array.Empty<string>());
        }
    }
}
