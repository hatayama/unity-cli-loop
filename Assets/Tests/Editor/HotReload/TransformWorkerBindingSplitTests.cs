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
    /// EditMode coverage for an added method whose body the worker's compilation cannot fully bind,
    /// because another file of the same run declares from source a type the compiled assembly also
    /// holds, and a compiled API refers to the compiled one.
    /// </summary>
    public class TransformWorkerBindingSplitTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string HostFileName = "HotReloadBindingSplitHost.cs";
        private const string PayloadFileName = "HotReloadBindingSplitPayload.cs";
        private const string HostTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadBindingSplitHost";
        private const string InsertionAnchor = "        public int Handled => _handled;";
        private const string WireMethod =
            "\n\n        public void Wire()\n        {\n            _registry.Register(p => Handle(p));\n        }";

        /// <summary>
        /// What: with the host alone in the run, the lambda binds against the compiled payload and
        /// the added method is applied through the accessor rewrite, so the guard added for the
        /// split case does not refuse a body that binds.
        /// </summary>
        [Test]
        public async Task Run_HostAlone_AddsTheMethodThatRegistersALambda()
        {
            TransformWorkerClientResult result = await RunAsync(
                new[] { HostFileName },
                new[] { WithWire(ReadOnDisk(HostFileName)) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertNoSkippedMethodNamed(result, "Wire");
            TransformWorkerEntryDto entry = FindEntry(result, "Wire");
            Assert.That(entry, Is.Not.Null, "Missing entry for Wire.\n" + FormatSkipped(result));
            Assert.That(entry.patchKind, Is.EqualTo(HotReloadConstants.PatchKindAddedMethod));
        }

        /// <summary>
        /// What: with the payload file in the same run, the lambda's argument no longer binds to the
        /// private method, and the added method is skipped rather than applied as a shim that would
        /// throw a MethodAccessException on its first call.
        /// </summary>
        [Test]
        public async Task Run_HostWithThePayloadFile_SkipsTheMethodItCannotBind()
        {
            TransformWorkerClientResult result = await RunAsync(
                new[] { HostFileName, PayloadFileName },
                new[] { WithWire(ReadOnDisk(HostFileName)), ReadOnDisk(PayloadFileName) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "Wire"), Is.Null, "Wire must not be applied.");
            TransformWorkerSkippedDto skipped = FindSkipped(result, "Wire");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for Wire.\n" + FormatSkipped(result));
            Assert.That(skipped.reason.code, Is.EqualTo(HotReloadWorkerReasonCode.AddedMethodBodyUnbound));
        }

        private static string WithWire(string hostSource)
        {
            Assert.That(hostSource, Does.Contain(InsertionAnchor), "Precondition: anchor must exist.");
            return hostSource.Replace(InsertionAnchor, InsertionAnchor + WireMethod, StringComparison.Ordinal);
        }

        private static string ReadOnDisk(string fileName)
        {
            string path = Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName);
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return File.ReadAllText(path);
        }

        private static TransformWorkerEntryDto FindEntry(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.typeMetadataName == HostTypeMetadataName && entry.methodName == methodName)
                {
                    return entry;
                }
            }

            return null;
        }

        private static TransformWorkerSkippedDto FindSkipped(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains("." + methodName + "("))
                {
                    return skipped;
                }
            }

            return null;
        }

        private static void AssertNoSkippedMethodNamed(TransformWorkerClientResult result, string methodName)
        {
            Assert.That(FindSkipped(result, methodName), Is.Null, "Unexpected skip.\n" + FormatSkipped(result));
        }

        private static string FormatSkipped(TransformWorkerClientResult result)
        {
            List<string> lines = new List<string>();
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                lines.Add(skipped.method + " :: " + HotReloadWorkerReasonText.Render(skipped.reason));
            }

            return string.Join("\n", lines);
        }

        // Each file is written edited under the test sources directory and sent under its
        // project-relative path, with the source on disk as the snapshot the worker diffs against.
        private static async Task<TransformWorkerClientResult> RunAsync(string[] fileNames, string[] editedSources)
        {
            Assert.That(editedSources.Length, Is.EqualTo(fileNames.Length), "One edited source per file.");
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(projectRoot, "Library", "ScriptAssemblies", TestAssemblyName + ".dll");
            Assert.That(File.Exists(targetDllPath), Is.True, "Test assembly dll missing: " + targetDllPath);
            UnityEditor.Compilation.Assembly compilationAssembly = FindCompilationAssembly();

            TransformWorkerSourceDto[] sources = new TransformWorkerSourceDto[fileNames.Length];
            for (int index = 0; index < fileNames.Length; index++)
            {
                sources[index] = new TransformWorkerSourceDto
                {
                    sourcePath = HotReloadTestSourceWriter.WriteEditedSource(
                        "BindingSplit_" + fileNames[index],
                        editedSources[index]),
                    projectRelativePath = "Assets/Tests/Editor/HotReload/" + fileNames[index],
                    snapshotSource = ReadOnDisk(fileNames[index])
                };
            }

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sources = sources,
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = BuildAbsoluteReferencePaths(compilationAssembly.allReferences, targetDllPath),
                targetTypesAssemblyPath = targetDllPath,
                assemblySourcePaths = BuildAbsoluteAssemblySourcePaths(projectRoot, compilationAssembly.sourceFiles),
                excludedMethodKeys = Array.Empty<string>(),
                excludedAddedMethodKeys = Array.Empty<string>()
            };

            return await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        private static UnityEditor.Compilation.Assembly FindCompilationAssembly()
        {
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == TestAssemblyName)
                {
                    return assembly;
                }
            }

            Assert.Fail("CompilationPipeline assembly not found.");
            return null;
        }

        private static string[] BuildAbsoluteReferencePaths(string[] allReferences, string targetDllPath)
        {
            List<string> paths = new List<string>();
            foreach (string reference in allReferences ?? Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                {
                    paths.Add(Path.GetFullPath(reference));
                }
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            if (!paths.Contains(fullTarget))
            {
                paths.Add(fullTarget);
            }

            return paths.ToArray();
        }

        private static string[] BuildAbsoluteAssemblySourcePaths(string projectRoot, string[] sourceFiles)
        {
            List<string> paths = new List<string>();
            foreach (string sourceFile in sourceFiles ?? Array.Empty<string>())
            {
                string relative = sourceFile.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
                paths.Add(Path.GetFullPath(Path.Combine(projectRoot, relative)));
            }

            return paths.ToArray();
        }
    }
}
