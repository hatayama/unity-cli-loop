using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies inactive conditional regions do not leak into emitted shim source.
    /// </summary>
    public sealed class TransformWorkerDisabledTextTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string ProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadDisabledTextFixture.cs";

        /// <summary>
        /// What: disabled text before a method is omitted while the neighboring shim is emitted.
        /// </summary>
        [Test]
        public async Task Emit_InactiveIfRegionBeforeMethod_DisabledTextDoesNotLeakIntoShim()
        {
            TransformWorkerClientResult result = await RunWorkerOnFixtureAsync();
            string shimSource = result.Output.shimSource;

            Assert.That(shimSource, Does.Not.Contain("_uloopDisabledGuardedField"));
            Assert.That(shimSource, Does.Not.Contain("UloopDisabledGuardedMethod"));
            Assert.That(shimSource, Does.Contain("GuardedNeighborMethod__shim"));
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnFixtureAsync()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string sourcePath = Path.Combine(projectRoot, ProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                ProjectRelativePath,
                snapshotSource: null);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.shimSource, Is.Not.Null.And.Not.Empty);
            return result;
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnSourceAsync(
            string sourcePath,
            string projectRelativePath,
            string snapshotSource)
        {
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

            List<string> referencePaths = new List<string>();
            if (compilationAssembly.allReferences != null)
            {
                foreach (string reference in compilationAssembly.allReferences)
                {
                    if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                    {
                        referencePaths.Add(Path.GetFullPath(reference));
                    }
                }
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            if (!referencePaths.Contains(fullTarget))
            {
                referencePaths.Add(fullTarget);
            }

            List<string> assemblySourcePaths = new List<string>();
            if (compilationAssembly.sourceFiles != null)
            {
                foreach (string sourceFile in compilationAssembly.sourceFiles)
                {
                    assemblySourcePaths.Add(Path.GetFullPath(sourceFile));
                }
            }

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sourcePath = sourcePath,
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = referencePaths.ToArray(),
                targetTypesAssemblyPath = targetDllPath,
                snapshotSource = snapshotSource,
                projectRelativePath = projectRelativePath,
                assemblySourcePaths = assemblySourcePaths.ToArray()
            };

            return await TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }
    }
}
