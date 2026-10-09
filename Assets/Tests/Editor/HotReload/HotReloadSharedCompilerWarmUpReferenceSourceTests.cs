using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for <see cref="HotReloadSharedCompilerWarmUpReferenceSource"/>: the list it
    /// hands the shared-worker warm-up is the part of the next shim compile's references that the
    /// compile binds as they are.
    /// </summary>
    public class HotReloadSharedCompilerWarmUpReferenceSourceTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        private const string HotReloadEditorAssemblyName = "UnityCLILoop.FirstPartyTools.HotReload.Editor";

        private const string MissingAssemblyName = "No.Such.Assembly";

        // Why wait: the run's reference list built in some tests writes publicized copies, which the
        // installed warm-up may be writing on a pool thread at the same time.
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
        /// Verifies empty entries, missing files and the target itself are left out, and that only a
        /// publicizable project assembly under the compiled-assemblies directory is marked for a copy.
        /// </summary>
        [Test]
        public void ClassifyCompileReferences_SortsEachKindOfReference()
        {
            string projectRoot = ProjectRoot();
            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(projectRoot);
            string fullTarget = Path.GetFullPath(layout.DllPath(TestAssemblyName));
            string targetWithOtherCase = Path.Combine(
                Path.GetDirectoryName(fullTarget),
                Path.GetFileName(fullTarget).ToUpperInvariant());
            string toolContracts = "Library/ScriptAssemblies/UnityCLILoop.ToolContracts.dll";
            string testRunner = "Library/ScriptAssemblies/UnityEngine.TestRunner.dll";
            string engine = typeof(UnityEngine.Object).Assembly.Location;
            string[] allReferences =
            {
                string.Empty,
                Path.Combine(Path.GetTempPath(), "missing-" + Path.GetRandomFileName() + ".dll"),
                targetWithOtherCase,
                toolContracts,
                testRunner,
                engine
            };

            List<ShimCompileReference> classified = HotReloadShimReferenceBuilder.ClassifyCompileReferences(
                allReferences,
                projectRoot,
                fullTarget,
                layout.CompiledAssembliesDirectory);

            Assert.That(classified.Count, Is.EqualTo(3));
            Assert.That(classified[0].FullPath, Is.EqualTo(Path.GetFullPath(Path.Combine(projectRoot, toolContracts))));
            Assert.That(classified[0].UsesRewrittenCopy, Is.True);
            Assert.That(classified[1].FullPath, Is.EqualTo(Path.GetFullPath(Path.Combine(projectRoot, testRunner))));
            Assert.That(classified[1].UsesRewrittenCopy, Is.False);
            Assert.That(classified[2].FullPath, Is.EqualTo(Path.GetFullPath(engine)));
            Assert.That(classified[2].UsesRewrittenCopy, Is.False);
        }

        /// <summary>
        /// Verifies every listed reference is one the run's shim compile binds, and that the only
        /// references the run binds beyond the list are rewritten copies.
        /// </summary>
        [Test]
        public async Task CollectAsync_ListsWhatTheShimCompileBindsAsItIs()
        {
            string projectRoot = ProjectRoot();
            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(projectRoot);
            HotReloadSharedCompilerWarmUpReferenceSource source = new HotReloadSharedCompilerWarmUpReferenceSource(
                projectRoot,
                _ => new[] { TestAssemblyName },
                HotReloadCompilationAssemblies.FindByName);

            IReadOnlyList<string> listed = await source.CollectAsync(CancellationToken.None);

            Assert.That(listed, Is.Not.Empty);
            HotReloadShimReferenceBuilder.ShimReferencePathsResult run = HotReloadShimReferenceBuilder.TryBuildShimReferencePaths(
                HotReloadCompilationAssemblies.FindByName(TestAssemblyName),
                HotReloadTypeHome.ScriptAssemblies(TestAssemblyName, layout.DllPath(TestAssemblyName)),
                false,
                false,
                Array.Empty<HotReloadTypeHome>());
            Assert.That(run.ErrorMessage, Is.Null);
            HashSet<string> runReferences = new HashSet<string>(run.References, StringComparer.Ordinal);
            foreach (string reference in listed)
            {
                Assert.That(runReferences.Contains(reference), Is.True, "not bound by the run: " + reference);
            }

            HashSet<string> listedReferences = new HashSet<string>(listed, StringComparer.Ordinal);
            string publicizedRefs = Path.GetFullPath(Path.Combine(projectRoot, HotReloadConstants.PublicizedRefsRelativeDirectory));
            string publicizedExternalRefs = Path.GetFullPath(
                Path.Combine(projectRoot, HotReloadConstants.PublicizedExternalRefsRelativeDirectory));
            foreach (string reference in run.References)
            {
                if (listedReferences.Contains(reference))
                {
                    continue;
                }

                string directory = Path.GetDirectoryName(Path.GetFullPath(reference));
                Assert.That(
                    directory == publicizedRefs || directory == publicizedExternalRefs,
                    Is.True,
                    "bound as it is by the run but not listed: " + reference);
            }
        }

        [Test]
        public async Task CollectAsync_SkipsANameUnityDoesNotList()
        {
            List<string> askedForMissing = new List<string>();
            HotReloadSharedCompilerWarmUpReferenceSource source = new HotReloadSharedCompilerWarmUpReferenceSource(
                ProjectRoot(),
                _ => new[] { MissingAssemblyName, TestAssemblyName },
                name =>
                {
                    if (name == MissingAssemblyName)
                    {
                        askedForMissing.Add(name);
                        return null;
                    }

                    return HotReloadCompilationAssemblies.FindByName(name);
                });

            IReadOnlyList<string> listed = await source.CollectAsync(CancellationToken.None);

            IReadOnlyList<string> expected = await CollectFor(new[] { TestAssemblyName });
            Assert.That(listed, Is.EqualTo(expected));
        }

        [Test]
        public async Task CollectAsync_SkipsANameWhoseCompilationAssemblyIsGone()
        {
            HotReloadSharedCompilerWarmUpReferenceSource source = new HotReloadSharedCompilerWarmUpReferenceSource(
                ProjectRoot(),
                _ => new[] { TestAssemblyName },
                _ => null);

            IReadOnlyList<string> listed = await source.CollectAsync(CancellationToken.None);

            Assert.That(listed, Is.Empty);
        }

        [Test]
        public async Task CollectAsync_WithAnEmptyLedger_ReturnsEmptyWithoutAskingUnity()
        {
            int finderCalls = 0;
            HotReloadSharedCompilerWarmUpReferenceSource source = new HotReloadSharedCompilerWarmUpReferenceSource(
                ProjectRoot(),
                _ => Array.Empty<string>(),
                name =>
                {
                    finderCalls++;
                    return HotReloadCompilationAssemblies.FindByName(name);
                });

            IReadOnlyList<string> listed = await source.CollectAsync(CancellationToken.None);

            Assert.That(listed, Is.Empty);
            Assert.That(finderCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies only the first usable name is listed. The hot-reload tool comes first because
        /// its references bound as they are are a subset of the test assembly's, so a union of
        /// both would add the test runner assemblies only the test assembly references.
        /// </summary>
        [Test]
        public async Task CollectAsync_UsesOnlyTheFirstUsableName()
        {
            IReadOnlyList<string> listed = await CollectFor(new[] { HotReloadEditorAssemblyName, TestAssemblyName });

            IReadOnlyList<string> expected = await CollectFor(new[] { HotReloadEditorAssemblyName });
            Assert.That(listed, Is.EqualTo(expected));
            foreach (string reference in listed)
            {
                string fileName = Path.GetFileName(reference);
                Assert.That(fileName, Is.Not.EqualTo("UnityEngine.TestRunner.dll"));
                Assert.That(fileName, Is.Not.EqualTo("UnityEditor.TestRunner.dll"));
            }
        }

        [Test]
        public async Task CollectAsync_AsksUnityOnTheMainThreadWhenCalledFromAPoolThread()
        {
            List<bool> askedOnMainThread = new List<bool>();
            HotReloadSharedCompilerWarmUpReferenceSource source = new HotReloadSharedCompilerWarmUpReferenceSource(
                ProjectRoot(),
                _ => new[] { TestAssemblyName },
                name =>
                {
                    askedOnMainThread.Add(MainThreadSwitcher.IsMainThread);
                    return HotReloadCompilationAssemblies.FindByName(name);
                });

            await Task.Run(() => source.CollectAsync(CancellationToken.None));

            Assert.That(askedOnMainThread, Is.EqualTo(new[] { true }));
        }

        [Test]
        public void HotReloadStartup_WiresTheWarmUpReferenceCollector()
        {
            Assert.That(SharedCompilerWarmUpCoordination.CollectWarmUpReferencePaths, Is.Not.Null);
        }

        private static Task<IReadOnlyList<string>> CollectFor(IReadOnlyList<string> ledger)
        {
            HotReloadSharedCompilerWarmUpReferenceSource source = new HotReloadSharedCompilerWarmUpReferenceSource(
                ProjectRoot(),
                _ => ledger,
                HotReloadCompilationAssemblies.FindByName);
            return source.CollectAsync(CancellationToken.None);
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }
    }
}
