using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for the compiled-assembly cache behind the caller scan of a
    /// hot reload run: a run reads each dll it scans at most once, and the next run reads none
    /// of them again.
    /// </summary>
    public class HotReloadCallSiteCacheE2ETests
    {
        private const string FixtureFileName = "HotReloadCallSiteCacheE2EFixture.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;
        private const string ReadBody = "return 1;";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: the first run reads each dll its caller scan covers at most once, and a second run
        /// on the same file reads none of them again, because the scanned dlls stay cached.
        /// </summary>
        [Test]
        public async Task Run_SecondRunOnTheSameFile_ReadsNoCompiledAssemblyAgain()
        {
            string fixturePath = FixturePath();
            string source = File.ReadAllText(fixturePath);
            Assert.That(source, Does.Contain(ReadBody), "Precondition: the Read body anchor must exist.");
            int scannedAssemblyCount = CountScannedAssemblies();
            // Why clear: entries cached by earlier tests would let the first run read nothing,
            // which could not tell a cached dll from a scan that never ran.
            HotReloadCompiledCallSiteCache.Shared.Clear();

            int loadsBeforeFirst = HotReloadCompiledCallSiteCache.Shared.LoadCount;
            HotReloadOrchestratorResult first = await RunWithReadReturningAsync(fixturePath, source, 2);
            int loadsAfterFirst = HotReloadCompiledCallSiteCache.Shared.LoadCount;
            Assert.That(
                new HotReloadCallSiteCacheE2EFixture().Read(),
                Is.EqualTo(2),
                "Precondition: the first run must patch Read, so its caller scan runs.\n" + FormatOutcomes(first));

            HotReloadOrchestratorResult second = await RunWithReadReturningAsync(fixturePath, source, 3);
            int loadsAfterSecond = HotReloadCompiledCallSiteCache.Shared.LoadCount;
            Assert.That(
                new HotReloadCallSiteCacheE2EFixture().Read(),
                Is.EqualTo(3),
                "Precondition: the second run must patch Read, so its caller scan runs.\n" + FormatOutcomes(second));

            Assert.That(
                loadsAfterFirst - loadsBeforeFirst,
                Is.InRange(1, scannedAssemblyCount),
                "The first run reads each dll it scans at most once.");
            Assert.That(loadsAfterSecond - loadsAfterFirst, Is.EqualTo(0), "The second run reads no dll again.");
        }

        private static async Task<HotReloadOrchestratorResult> RunWithReadReturningAsync(
            string fixturePath,
            string source,
            int value)
        {
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "CallSiteCacheE2E.cs",
                source.Replace(ReadBody, "return " + value + ";"));
            HotReloadOrchestratorResult result = null;
            try
            {
                result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                    new[] { fixturePath },
                    editedPath,
                    CancellationToken.None);
            }
            catch (OperationCanceledException exception)
            {
                // Why fail here: the test framework records an async test that ends canceled as
                // passed, which would hide a run that never finished.
                Assert.Fail("The run was canceled: " + exception.Message);
            }

            return result;
        }

        // The caller scan covers the fixture's assembly and every assembly that references its dll.
        private static int CountScannedAssemblies()
        {
            string targetAssemblyName = Path.GetFileNameWithoutExtension(
                CompilationPipeline.GetAssemblyNameFromScriptPath(FixtureProjectRelativePath));
            string targetDllFileName = targetAssemblyName + ".dll";
            int count = 0;
            foreach (UnityCompilationAssembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == targetAssemblyName
                    || (assembly.allReferences ?? Array.Empty<string>()).Any(
                        reference => Path.GetFileName(reference) == targetDllFileName))
                {
                    count++;
                }
            }

            Assert.That(count, Is.GreaterThan(0), "The fixture's assembly must be in the compilation pipeline.");
            return count;
        }

        private static string FixturePath()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " @" + outcome.FilePath + " :: " + outcome.Reason);
            }

            lines.AddRange(result.Warnings ?? new List<string>());
            return string.Join("\n", lines);
        }
    }
}
