using System.IO;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for transform-worker bootstrap and a smoke invocation that returns entries.
    /// </summary>
    public class TransformWorkerClientTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        /// <summary>
        /// What: bootstrap compiles (or reuses a cached) worker.dll, then running the worker on the
        /// e2e fixture source returns at least one shim entry.
        /// </summary>
        [Test]
        public async Task BootstrapAndRun_OnE2EFixture_ReturnsEntries()
        {
            string fixturePath = ResolveE2EFixturePath();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                TestAssemblyName + ".dll");
            Assert.That(File.Exists(targetDllPath), Is.True, "Test assembly dll missing: " + targetDllPath);

            UnityEditor.Compilation.Assembly compilationAssembly = null;
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == TestAssemblyName)
                {
                    compilationAssembly = assembly;
                    break;
                }
            }

            Assert.That(compilationAssembly, Is.Not.Null, "CompilationPipeline assembly not found.");

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sourcePath = fixturePath,
                defines = compilationAssembly.defines ?? System.Array.Empty<string>(),
                referencePaths = compilationAssembly.allReferences,
                targetTypesAssemblyPath = targetDllPath
            };

            TransformWorkerClientResult result = await TransformWorkerClient.RunAsync(input);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output, Is.Not.Null);
            Assert.That(result.Output.entries, Is.Not.Null);
            Assert.That(result.Output.entries.Length, Is.GreaterThan(0), "Expected at least one shim entry.");
            Assert.That(result.Output.shimSource, Is.Not.Null.And.Not.Empty);

            bool foundCompute = false;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == nameof(HotReloadE2EFixture.ComputeWithPrivate))
                {
                    foundCompute = true;
                    Assert.That(entry.shimMethodName, Does.Contain("__shim"));
                    Assert.That(entry.shimTypeName, Does.Contain("UloopHotReloadShims"));
                }
            }

            Assert.That(foundCompute, Is.True, "ComputeWithPrivate entry missing from worker output.");

            bool foundBaseSkip = false;
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null
                    && skipped.method.Contains(nameof(HotReloadE2EFixture.CallsBase))
                    && skipped.reason != null
                    && skipped.reason.Contains("base"))
                {
                    foundBaseSkip = true;
                }
            }

            Assert.That(foundBaseSkip, Is.True, "CallsBase should be skipped for base. usage.");
        }

        private static string ResolveE2EFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadE2EFixtures.cs");
            Assert.That(File.Exists(path), Is.True, "E2E fixture source missing: " + path);
            return Path.GetFullPath(path);
        }
    }
}
