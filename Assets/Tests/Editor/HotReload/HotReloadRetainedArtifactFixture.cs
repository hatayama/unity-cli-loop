using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using Mono.Cecil;
using Mono.Cecil.Cil;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Builds the edited source, the compiled target assembly and the retained artifact assembly a
    /// retained-binding test runs against, plus the worker inputs that reference them, so both the
    /// worker-level and the propagation tests describe the same world.
    /// </summary>
    internal sealed class HotReloadRetainedArtifactFixture
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        // A second edited file of the same group, so a test can show that a run-level failure
        // takes the whole group down rather than reporting one file's diagnostics.
        private const string SiblingSource =
            "namespace Example { public class Sibling { public int Get() { return 3; } } }";

        private HotReloadRetainedArtifactFixture(
            string sourcePath,
            string projectRelativePath,
            string siblingSourcePath,
            string siblingProjectRelativePath,
            string targetAssemblyPath,
            string targetAssemblyName,
            string targetAssemblyMvid,
            string artifactPath)
        {
            SourcePath = sourcePath;
            ProjectRelativePath = projectRelativePath;
            SiblingSourcePath = siblingSourcePath;
            SiblingProjectRelativePath = siblingProjectRelativePath;
            TargetAssemblyPath = targetAssemblyPath;
            TargetAssemblyName = targetAssemblyName;
            TargetAssemblyMvid = targetAssemblyMvid;
            ArtifactPath = artifactPath;
            RetainedFingerprint = string.Empty;
        }

        public string SourcePath { get; }

        public string ProjectRelativePath { get; }

        public string TargetAssemblyPath { get; }

        public string TargetAssemblyName { get; }

        public string TargetAssemblyMvid { get; }

        public string ArtifactPath { get; }

        public string SiblingSourcePath { get; }

        public string SiblingProjectRelativePath { get; }

        public string RetainedFingerprint { get; private set; }

        public static async Task<HotReloadRetainedArtifactFixture> CreateAsync(
            string name,
            string editedSource)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(
                projectRoot,
                "Library",
                "UloopHotReload",
                "TestSources",
                "RetainedDeclaration",
                name);
            Directory.CreateDirectory(directory);
            string sourcePath = Path.Combine(directory, "Edited.cs");
            File.WriteAllText(sourcePath, editedSource);
            string siblingSourcePath = Path.Combine(directory, "Sibling.cs");
            File.WriteAllText(siblingSourcePath, SiblingSource);
            string targetAssemblyPath = Path.Combine(directory, "RetainedTarget.dll");
            string targetAssemblyMvid = CreateTargetAssembly(targetAssemblyPath);
            string artifactPath = Path.Combine(directory, "RetainedArtifact.dll");
            CreateArtifactAssembly(artifactPath);
            HotReloadRetainedArtifactFixture fixture = new HotReloadRetainedArtifactFixture(
                sourcePath,
                "Assets/RetainedDeclaration/" + name + "/Edited.cs",
                siblingSourcePath,
                "Assets/RetainedDeclaration/" + name + "/Sibling.cs",
                targetAssemblyPath,
                "RetainedTarget",
                targetAssemblyMvid,
                artifactPath);
            // The recorded fingerprint has to be the value planning really produced for this
            // source, so it is read back from a prepare run rather than restated by the test.
            fixture.RetainedFingerprint = await fixture.ReadPlannedFingerprintAsync();
            return fixture;
        }

        public TransformWorkerInputDto BuildTransformInput(
            TransformWorkerIntroducedTypeArtifactDto[] artifacts)
        {
            return BuildInput(null, artifacts, includeSibling: false);
        }

        /// <summary>
        /// Builds a transform input for both edited files of the group, so a test can assert what
        /// a run-level failure does to a group of more than one file.
        /// </summary>
        public TransformWorkerInputDto BuildGroupTransformInput(
            TransformWorkerIntroducedTypeArtifactDto[] artifacts)
        {
            return BuildInput(null, artifacts, includeSibling: true);
        }

        public TransformWorkerInputDto BuildPrepareInput()
        {
            return BuildInput(
                "prepareIntroducedTypes",
                Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                includeSibling: false);
        }

        public TransformWorkerIntroducedTypeArtifactDto CreateRecordedArtifact(
            string declarationFingerprint)
        {
            return new TransformWorkerIntroducedTypeArtifactDto
            {
                assemblyFullName = ReadAssemblyFullName(ArtifactPath),
                referencePath = ArtifactPath,
                types = new[]
                {
                    new TransformWorkerIntroducedTypeArtifactTypeDto
                    {
                        metadataName = "Example.Retained",
                        originalAssemblyName = TargetAssemblyName,
                        originalAssemblyMvid = TargetAssemblyMvid,
                        ownerProjectRelativePath = ProjectRelativePath,
                        declarationFingerprint = declarationFingerprint
                    }
                }
            };
        }

        public static string ReadAssemblyFullName(string path)
        {
            using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path))
            {
                return assembly.Name.FullName;
            }
        }

        private TransformWorkerInputDto BuildInput(
            string operation,
            TransformWorkerIntroducedTypeArtifactDto[] artifacts,
            bool includeSibling)
        {
            UnityEditor.Compilation.Assembly compilationAssembly = FindCompilationAssembly();
            List<string> referencePaths = new List<string>();
            foreach (string reference in compilationAssembly.allReferences)
            {
                if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                {
                    referencePaths.Add(Path.GetFullPath(reference));
                }
            }

            referencePaths.Add(Path.GetFullPath(TargetAssemblyPath));
            List<TransformWorkerSourceDto> sources = new List<TransformWorkerSourceDto>
            {
                new TransformWorkerSourceDto
                {
                    sourcePath = SourcePath,
                    projectRelativePath = ProjectRelativePath
                }
            };
            if (includeSibling)
            {
                sources.Add(
                    new TransformWorkerSourceDto
                    {
                        sourcePath = SiblingSourcePath,
                        projectRelativePath = SiblingProjectRelativePath
                    });
            }

            return new TransformWorkerInputDto
            {
                operation = operation,
                sources = sources.ToArray(),
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = referencePaths.ToArray(),
                targetTypesAssemblyPath = TargetAssemblyPath,
                targetAssemblyName = TargetAssemblyName,
                targetAssemblyMvid = TargetAssemblyMvid,
                assemblySourcePaths = Array.Empty<string>(),
                changedSiblingSourcePaths = Array.Empty<string>(),
                introducedTypeArtifacts = artifacts
            };
        }

        private async Task<string> ReadPlannedFingerprintAsync()
        {
            TransformWorkerClientResult prepared =
                await TransformWorkerClient.RunAsync(BuildPrepareInput(), CancellationToken.None);
            Assert.That(prepared.Success, Is.True, prepared.ErrorMessage);
            foreach (TransformWorkerIntroducedTypeDto introducedType in prepared.Output.files[0].introducedTypes)
            {
                if (introducedType.metadataName == "Example.Retained")
                {
                    return introducedType.declarationFingerprint;
                }
            }

            Assert.Fail("Planning did not report the retained type.");
            return null;
        }

        private static string CreateTargetAssembly(string path)
        {
            AssemblyNameDefinition assemblyName = new AssemblyNameDefinition("RetainedTarget", new Version(1, 0, 0, 0));
            using (AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(assemblyName, "RetainedTarget", ModuleKind.Dll))
            {
                TypeDefinition caller = new TypeDefinition(
                    "Example",
                    "Caller",
                    CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                    assembly.MainModule.TypeSystem.Object);
                MethodDefinition read = new MethodDefinition(
                    "Read",
                    CecilMethodAttributes.Public | CecilMethodAttributes.HideBySig,
                    assembly.MainModule.TypeSystem.Int32);
                ILProcessor processor = read.Body.GetILProcessor();
                processor.Append(processor.Create(OpCodes.Ldc_I4_0));
                processor.Append(processor.Create(OpCodes.Ret));
                caller.Methods.Add(read);
                assembly.MainModule.Types.Add(caller);
                assembly.Write(path);
            }

            using (ModuleDefinition module = ModuleDefinition.ReadModule(path))
            {
                return module.Mvid.ToString();
            }
        }

        private static void CreateArtifactAssembly(string path)
        {
            AssemblyNameDefinition assemblyName = new AssemblyNameDefinition("RetainedArtifact", new Version(1, 0, 0, 0));
            using (AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(assemblyName, "RetainedArtifact", ModuleKind.Dll))
            {
                TypeDefinition retained = new TypeDefinition(
                    "Example",
                    "Retained",
                    CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                    assembly.MainModule.TypeSystem.Object);
                FieldDefinition valueField = new FieldDefinition(
                    "Value",
                    Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static,
                    assembly.MainModule.TypeSystem.Int32);
                retained.Fields.Add(valueField);
                assembly.MainModule.Types.Add(retained);
                assembly.Write(path);
            }
        }

        public static UnityEditor.Compilation.Assembly FindCompilationAssembly()
        {
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == TestAssemblyName)
                {
                    return assembly;
                }
            }

            Assert.Fail("Compilation assembly was not found.");
            return null;
        }
    }
}
