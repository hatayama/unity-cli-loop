using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for <see cref="HotReloadPublicizedTargetWarmUpItem"/>: the copy it writes is
    /// the one a run after the reload reuses.
    /// </summary>
    public class HotReloadPublicizedTargetWarmUpItemTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        private static readonly DateTime MarkerWriteTimeUtc = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        // Why wait: the installed warm-up may be writing the test assembly's copy on a pool
        // thread, which would write back a copy a test deleted, or write first the copy a test
        // wants the code under test to write.
        [UnitySetUp]
        public IEnumerator WaitForTheInstalledWarmUpToStop()
        {
            Task stopped = PublicizedCopyTestCache.StopInstalledWarmUpAsync();
            while (!stopped.IsCompleted)
            {
                yield return null;
            }
        }

        /// <summary>
        /// What: for a target without a copy, the item writes the publicized copy, and the run's
        /// entry point returns that file without writing it again.
        /// </summary>
        [Test]
        public async Task RunAsync_ForATargetWithoutACopy_WritesTheCopyTheRunReuses()
        {
            PublicizedCopyTestCache.DeleteCopiesOf(TestAssemblyName, HotReloadConstants.PublicizedRefsRelativeDirectory);
            string expected = ExpectedCopyPath();
            Assert.That(File.Exists(expected), Is.False, "Precondition: the copy must be deleted.");

            await new HotReloadPublicizedTargetWarmUpItem().RunAsync(ContextFor(TestAssemblyName), CancellationToken.None);

            Assert.That(File.Exists(expected), Is.True, "copy written by the warm-up");
            // Why plant the marker between the item and the run's entry point: a run that wrote
            // the copy again would replace it, while calling the entry point twice after the item
            // would pass even when the first call wrote the copy.
            File.SetLastWriteTimeUtc(expected, MarkerWriteTimeUtc);
            string reused = ReferencePublicizer.GetOrCreatePublicizedCopy(
                RunHome(),
                PublicizerTestSearchDirectories.ForHotReloadTestAssembly());

            Assert.That(reused, Is.EqualTo(expected), "copy the run uses");
            Assert.That(File.GetLastWriteTimeUtc(expected), Is.EqualTo(MarkerWriteTimeUtc), "the run wrote the copy again");
        }

        /// <summary>
        /// What: when the copy already exists, the item leaves it as it is.
        /// </summary>
        [Test]
        public async Task RunAsync_WhenTheCopyExists_LeavesItUntouched()
        {
            string existing = ReferencePublicizer.GetOrCreatePublicizedCopy(
                RunHome(),
                PublicizerTestSearchDirectories.ForHotReloadTestAssembly());
            File.SetLastWriteTimeUtc(existing, MarkerWriteTimeUtc);

            await new HotReloadPublicizedTargetWarmUpItem().RunAsync(ContextFor(TestAssemblyName), CancellationToken.None);

            Assert.That(File.GetLastWriteTimeUtc(existing), Is.EqualTo(MarkerWriteTimeUtc));
        }

        /// <summary>
        /// What: a ledger name that no compilation assembly has is skipped without an exception
        /// and without writing a copy.
        /// </summary>
        [Test]
        public async Task RunAsync_ForATargetThatIsNotACompilationAssembly_CompletesWithoutWriting()
        {
            PublicizedCopyTestCache.DeleteCopiesOf(TestAssemblyName, HotReloadConstants.PublicizedRefsRelativeDirectory);

            await new HotReloadPublicizedTargetWarmUpItem().RunAsync(ContextFor("Uloop.NoSuchAssembly"), CancellationToken.None);

            Assert.That(File.Exists(ExpectedCopyPath()), Is.False);
        }

        /// <summary>
        /// What: a cancellation seen before the first unit throws and writes no copy.
        /// </summary>
        [Test]
        public void WritePublicizedCopies_WhenCancelledBeforeTheFirstUnit_ThrowsAndWritesNothing()
        {
            PublicizedCopyTestCache.DeleteCopiesOf(TestAssemblyName, HotReloadConstants.PublicizedRefsRelativeDirectory);
            HotReloadPublicizedTargetRequest request = new HotReloadPublicizedTargetRequest(
                RunHome(),
                PublicizerTestSearchDirectories.ForHotReloadTestAssembly());
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => HotReloadPublicizedTargetWarmUpItem.WritePublicizedCopies(new[] { request }, cts.Token));

            Assert.That(File.Exists(ExpectedCopyPath()), Is.False);
        }

        /// <summary>
        /// What: Cecil's resolution failure is an IOException, so the warm-up records a failed copy
        /// as a failed item and runs the items after it. A Cecil version that breaks this would let
        /// the failure stop the later items.
        /// </summary>
        [Test]
        public void AssemblyResolutionException_IsAnIOException_SoAFailedCopyLetsTheLaterItemsRun()
        {
            Assert.That(typeof(Mono.Cecil.AssemblyResolutionException).IsSubclassOf(typeof(IOException)), Is.True);
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string TestAssemblyDllPath()
        {
            return CompiledAssemblyLayout.Resolve(ProjectRoot()).DllPath(TestAssemblyName);
        }

        private static string ExpectedCopyPath()
        {
            return Path.Combine(
                ProjectRoot(),
                HotReloadConstants.PublicizedRefsRelativeDirectory,
                TestAssemblyName + "-" + HotReloadSourceSnapshotter.ReadAssemblyMvid(TestAssemblyDllPath())
                    + HotReloadConstants.CompiledAssemblyExtension);
        }

        private static HotReloadWarmUpContext ContextFor(string assemblyName)
        {
            string dllPath = TestAssemblyDllPath();
            return new HotReloadWarmUpContext(
                ProjectRoot(),
                new[]
                {
                    new HotReloadWarmUpTarget(
                        assemblyName,
                        dllPath,
                        Path.ChangeExtension(dllPath, ".pdb"),
                        Array.Empty<string>())
                });
        }

        // The home a run uses for the target right after a reload.
        private static HotReloadTypeHome RunHome()
        {
            return HotReloadTypeHome.ScriptAssembliesUnderProject(ProjectRoot(), TestAssemblyName);
        }
    }
}
