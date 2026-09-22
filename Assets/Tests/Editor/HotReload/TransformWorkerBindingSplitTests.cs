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
        private const string RegistryFileName = "HotReloadBindingSplitRegistry.cs";
        private const string HostTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadBindingSplitHost";
        private const string RegistryTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadBindingSplitRegistry";
        private const string PayloadTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadBindingSplitPayload";
        private const string NestedRegistryFileName = "HotReloadBindingSplitNestedRegistry.cs";
        // The metadata form, which is how the worker reports a nested type.
        private const string NestedRegistryTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadBindingSplitNestedRegistry/Inner";
        private const string ExtensionsFileName = "HotReloadBindingSplitPayloadExtensions.cs";
        private const string InsertionAnchor = "        public int Handled => _handled;";
        private const string WireMethod =
            "\n\n        public void Wire()\n        {\n            _registry.Register(p => Handle(p));\n        }";
        private const string WireMethodThatAlsoCounts =
            "\n\n        public void Wire()\n        {\n            _registry.Register(p =>\n            {\n"
            + "                _handled++;\n                Handle(p);\n            });\n        }";
        private const string CallExtensionMethod =
            "\n\n        public int Twice(HotReloadBindingSplitPayload payload)\n        {\n"
            + "            return payload.Doubled();\n        }";
        private const string PeekThroughIndexerMethod =
            "\n\n        public int Peek()\n        {\n"
            + "            HotReloadBindingSplitPayload payload = _registry[3];\n            return payload.Value;\n        }";
        private const string WireWithNullAndTypoMethod =
            "\n\n        public void Wire()\n        {\n            _registry.Register(null);\n"
            + "            UndeclaredName();\n        }";
        private const string WireThroughNestedRegistryMethod =
            "\n\n        public void Wire()\n        {\n"
            + "            new HotReloadBindingSplitNestedRegistry.Inner().Register(p => Handle(p));\n        }";

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
                new[] { WithMethod(ReadOnDisk(HostFileName), WireMethod) });

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
                new[] { WithMethod(ReadOnDisk(HostFileName), WireMethod), ReadOnDisk(PayloadFileName) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "Wire"), Is.Null, "Wire must not be applied.");
            TransformWorkerSkippedDto skipped = FindSkipped(result, "Wire");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for Wire.\n" + FormatSkipped(result));
            Assert.That(skipped.reason.code, Is.EqualTo(HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature));
        }

        /// <summary>
        /// What: a lambda that also reaches a private field it can bind still gets the method
        /// skipped, because the field access sends the body down the delegation path and that path
        /// must not skip the check that the whole body bound.
        /// </summary>
        [Test]
        public async Task Run_HostWithThePayloadFile_SkipsTheMethodEvenWhenItsLambdaAlsoTouchesAField()
        {
            TransformWorkerClientResult result = await RunAsync(
                new[] { HostFileName, PayloadFileName },
                new[] { WithMethod(ReadOnDisk(HostFileName), WireMethodThatAlsoCounts), ReadOnDisk(PayloadFileName) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "Wire"), Is.Null, "Wire must not be applied.");
            TransformWorkerSkippedDto skipped = FindSkipped(result, "Wire");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for Wire.\n" + FormatSkipped(result));
            Assert.That(skipped.reason.code, Is.EqualTo(HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature));
        }

        /// <summary>
        /// What: when the added method cannot bind because this run declares the payload from source
        /// while a compiled API still takes the compiled payload, the skipped row names the compiled
        /// type whose signature holds the old payload and the file that declares it, so the reader
        /// knows which file to pass as well instead of only being told to compile.
        /// </summary>
        [Test]
        public async Task Run_HostWithThePayloadFile_NamesTheFileDeclaringTheCompiledSignature()
        {
            TransformWorkerClientResult result = await RunAsync(
                new[] { HostFileName, PayloadFileName },
                new[] { WithMethod(ReadOnDisk(HostFileName), WireMethod), ReadOnDisk(PayloadFileName) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerSkippedDto skipped = FindSkipped(result, "Wire");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for Wire.\n" + FormatSkipped(result));
            Assert.That(skipped.reason.code, Is.EqualTo(HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature));
            string text = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(text, Does.Contain("'" + PayloadTypeMetadataName + "'"), text);
            Assert.That(text, Does.Contain("'" + RegistryTypeMetadataName + "'"), text);
            Assert.That(text, Does.Contain("Pass 'Assets/Tests/Editor/HotReload/" + RegistryFileName + "'"), text);
            Assert.That(text, Does.Contain("uloop compile"), text);
        }

        /// <summary>
        /// What: passing the file that declares the compiled API as well lets the added method bind
        /// against the payload this run declares, so the method is applied, which is the recovery
        /// the skipped row recommends.
        /// </summary>
        [Test]
        public async Task Run_HostWithThePayloadAndTheRegistryFiles_AddsTheMethod()
        {
            TransformWorkerClientResult result = await RunAsync(
                new[] { HostFileName, PayloadFileName, RegistryFileName },
                new[]
                {
                    WithMethod(ReadOnDisk(HostFileName), WireMethod),
                    ReadOnDisk(PayloadFileName),
                    ReadOnDisk(RegistryFileName)
                });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertNoSkippedMethodNamed(result, "Wire");
            TransformWorkerEntryDto entry = FindEntry(result, "Wire");
            Assert.That(entry, Is.Not.Null, "Missing entry for Wire.\n" + FormatSkipped(result));
            Assert.That(entry.patchKind, Is.EqualTo(HotReloadConstants.PatchKindAddedMethod));
        }

        /// <summary>
        /// What: when the compiled API that still takes the compiled payload is a nested type, the
        /// skipped row names it in its metadata form and still finds the file declaring it, because
        /// the name the worker sends and the name the compiled assembly is read by must agree.
        /// </summary>
        [Test]
        public async Task Run_HostWithThePayloadFile_NamesTheFileDeclaringANestedCompiledSignature()
        {
            TransformWorkerClientResult result = await RunAsync(
                new[] { HostFileName, PayloadFileName },
                new[] { WithMethod(ReadOnDisk(HostFileName), WireThroughNestedRegistryMethod), ReadOnDisk(PayloadFileName) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerSkippedDto skipped = FindSkipped(result, "Wire");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for Wire.\n" + FormatSkipped(result));
            Assert.That(skipped.reason.code, Is.EqualTo(HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature));
            string text = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(text, Does.Contain("'" + NestedRegistryTypeMetadataName + "'"), text);
            Assert.That(text, Does.Contain("Pass 'Assets/Tests/Editor/HotReload/" + NestedRegistryFileName + "'"), text);
        }

        /// <summary>
        /// What: when the compiled type is the receiver of a compiled extension method, which the
        /// reduced call does not list among its parameters, the skipped row still names the file
        /// declaring the extension, so a split through the receiver is not missed.
        /// </summary>
        [Test]
        public async Task Run_HostWithThePayloadFile_NamesTheFileDeclaringACompiledExtensionOnThePayload()
        {
            TransformWorkerClientResult result = await RunAsync(
                new[] { HostFileName, PayloadFileName },
                new[] { WithMethod(ReadOnDisk(HostFileName), CallExtensionMethod), ReadOnDisk(PayloadFileName) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerSkippedDto skipped = FindSkipped(result, "Twice");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for Twice.\n" + FormatSkipped(result));
            string text = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(skipped.reason.code, Is.EqualTo(HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature), text);
            Assert.That(text, Does.Contain("Pass 'Assets/Tests/Editor/HotReload/" + ExtensionsFileName + "'"), text);
        }

        /// <summary>
        /// What: a compiled indexer that returns the compiled payload is found as the signature
        /// holding the split, so the skipped row names the registry and its file.
        /// </summary>
        [Test]
        public async Task Run_HostWithThePayloadFile_NamesTheFileDeclaringACompiledIndexer()
        {
            TransformWorkerClientResult result = await RunAsync(
                new[] { HostFileName, PayloadFileName },
                new[] { WithMethod(ReadOnDisk(HostFileName), PeekThroughIndexerMethod), ReadOnDisk(PayloadFileName) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerSkippedDto skipped = FindSkipped(result, "Peek");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for Peek.\n" + FormatSkipped(result));
            string text = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(skipped.reason.code, Is.EqualTo(HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature), text);
            Assert.That(text, Does.Contain("'" + RegistryTypeMetadataName + "'"), text);
            Assert.That(text, Does.Contain("Pass 'Assets/Tests/Editor/HotReload/" + RegistryFileName + "'"), text);
        }

        /// <summary>
        /// What: a body that fails only on a typo keeps the plain unbound reason even though it also
        /// calls a compiled API naming the payload, because that call binds and passing its file
        /// would not fix the typo.
        /// </summary>
        [Test]
        public async Task Run_HostWithThePayloadFile_KeepsThePlainReasonWhenOnlyATypoFailsToBind()
        {
            TransformWorkerClientResult result = await RunAsync(
                new[] { HostFileName, PayloadFileName },
                new[] { WithMethod(ReadOnDisk(HostFileName), WireWithNullAndTypoMethod), ReadOnDisk(PayloadFileName) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerSkippedDto skipped = FindSkipped(result, "Wire");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for Wire.\n" + FormatSkipped(result));
            Assert.That(
                skipped.reason.code,
                Is.EqualTo(HotReloadWorkerReasonCode.AddedMethodBodyUnbound),
                HotReloadWorkerReasonText.Render(skipped.reason));
        }

        private static string WithMethod(string hostSource, string method)
        {
            Assert.That(hostSource, Does.Contain(InsertionAnchor), "Precondition: anchor must exist.");
            return hostSource.Replace(InsertionAnchor, InsertionAnchor + method, StringComparison.Ordinal);
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
