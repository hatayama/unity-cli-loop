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
    /// Verifies that a declaration a retained artifact already serves is taken out of the tree the
    /// transform binds against, only when the artifact record still describes the edited source,
    /// and that removing it does not move the source lines the shim reports.
    /// </summary>
    public sealed class TransformWorkerRetainedDeclarationTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        private const string EditedSource =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public static int Twice()\n"
            + "        {\n"
            + "            return Value * 2;\n"
            + "        }\n"
            + "    }\n"
            + "\n"
            + "    public class Caller\n"
            + "    {\n"
            + "        public int Read()\n"
            + "        {\n"
            + "            return Retained.Value + 1;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        /// <summary>
        /// What: the shim reports the line the edited file really has for a method that follows a
        /// removed declaration, so taking the declaration out does not shift the mapping.
        /// </summary>
        [Test]
        public async Task Transform_RetainedDeclarationRemoved_KeepsSourceLineNumbers()
        {
            RetainedFixture fixture = await CreateFixtureAsync("KeepsSourceLineNumbers");

            TransformWorkerClientResult withoutRecord = await RunAsync(fixture, Array.Empty<TransformWorkerIntroducedTypeArtifactDto>());
            TransformWorkerClientResult withRecord = await RunAsync(fixture, new[] { CreateRecordedArtifact(fixture, fixture.RetainedFingerprint) });

            Assert.That(withoutRecord.Success, Is.True, withoutRecord.ErrorMessage);
            Assert.That(withRecord.Success, Is.True, withRecord.ErrorMessage);
            Assert.That(FindReadEntry(withRecord).sourceStartLine, Is.EqualTo(FindReadEntry(withoutRecord).sourceStartLine));
            Assert.That(withRecord.Output.shimSource, Does.Contain("Retained.Value + 1"));
        }

        /// <summary>
        /// What: the retained type stops being transformed as a source declaration once its record
        /// matches, so the run no longer reports rows for its members.
        /// </summary>
        [Test]
        public async Task Transform_RetainedDeclarationRemoved_ReportsNoRowsForRetainedType()
        {
            RetainedFixture fixture = await CreateFixtureAsync("ReportsNoRows");

            TransformWorkerClientResult withoutRecord = await RunAsync(fixture, Array.Empty<TransformWorkerIntroducedTypeArtifactDto>());
            TransformWorkerClientResult withRecord = await RunAsync(fixture, new[] { CreateRecordedArtifact(fixture, fixture.RetainedFingerprint) });

            Assert.That(withoutRecord.Success, Is.True, withoutRecord.ErrorMessage);
            Assert.That(withRecord.Success, Is.True, withRecord.ErrorMessage);
            Assert.That(CountRowsMentioning(withoutRecord, "Twice"), Is.GreaterThan(0));
            Assert.That(CountRowsMentioning(withRecord, "Twice"), Is.EqualTo(0));
        }

        /// <summary>
        /// What: a record whose fingerprint no longer matches the edited source leaves the
        /// declaration in place, because the source is then newer than the artifact.
        /// </summary>
        [Test]
        public async Task Transform_RecordFingerprintDoesNotMatch_KeepsDeclarationInBinding()
        {
            RetainedFixture fixture = await CreateFixtureAsync("FingerprintMismatch");

            TransformWorkerClientResult tampered = await RunAsync(
                fixture,
                new[] { CreateRecordedArtifact(fixture, new string('0', 64)) });

            Assert.That(tampered.Success, Is.True, tampered.ErrorMessage);
            Assert.That(CountRowsMentioning(tampered, "Twice"), Is.GreaterThan(0));
        }

        /// <summary>
        /// What: an artifact record the worker cannot trust fails the whole run instead of
        /// producing a shim, because the caller advances to revert and compile on success.
        /// </summary>
        [Test]
        public async Task Transform_ArtifactIdentityMismatch_FailsRun()
        {
            RetainedFixture fixture = await CreateFixtureAsync("IdentityMismatch");
            TransformWorkerIntroducedTypeArtifactDto mismatched =
                CreateRecordedArtifact(fixture, fixture.RetainedFingerprint);
            mismatched.assemblyFullName = ReadAssemblyFullName(fixture.TargetAssemblyPath);

            TransformWorkerClientResult result = await RunAsync(fixture, new[] { mismatched });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Output, Is.Null.Or.Property("entries").Empty);
        }

        private static TransformWorkerEntryDto FindReadEntry(TransformWorkerClientResult result)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == "Read")
                {
                    return entry;
                }
            }

            Assert.Fail("No shim entry was emitted for the caller method.");
            return null;
        }

        private static int CountRowsMentioning(TransformWorkerClientResult result, string memberName)
        {
            int count = 0;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == memberName)
                {
                    count++;
                }
            }

            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains(memberName))
                {
                    count++;
                }
            }

            return count;
        }

        private static TransformWorkerIntroducedTypeArtifactDto CreateRecordedArtifact(
            RetainedFixture fixture,
            string declarationFingerprint)
        {
            return new TransformWorkerIntroducedTypeArtifactDto
            {
                assemblyFullName = ReadAssemblyFullName(fixture.ArtifactPath),
                referencePath = fixture.ArtifactPath,
                types = new[]
                {
                    new TransformWorkerIntroducedTypeArtifactTypeDto
                    {
                        metadataName = "Example.Retained",
                        originalAssemblyName = fixture.TargetAssemblyName,
                        originalAssemblyMvid = fixture.TargetAssemblyMvid,
                        ownerProjectRelativePath = fixture.ProjectRelativePath,
                        declarationFingerprint = declarationFingerprint
                    }
                }
            };
        }

        private static async Task<TransformWorkerClientResult> RunAsync(
            RetainedFixture fixture,
            TransformWorkerIntroducedTypeArtifactDto[] artifacts)
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

            referencePaths.Add(Path.GetFullPath(fixture.TargetAssemblyPath));
            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sources = new[]
                {
                    new TransformWorkerSourceDto
                    {
                        sourcePath = fixture.SourcePath,
                        projectRelativePath = fixture.ProjectRelativePath
                    }
                },
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = referencePaths.ToArray(),
                targetTypesAssemblyPath = fixture.TargetAssemblyPath,
                targetAssemblyName = fixture.TargetAssemblyName,
                targetAssemblyMvid = fixture.TargetAssemblyMvid,
                assemblySourcePaths = Array.Empty<string>(),
                changedSiblingSourcePaths = Array.Empty<string>(),
                introducedTypeArtifacts = artifacts
            };
            return await TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        private static async Task<TransformWorkerClientResult> RunPrepareAsync(RetainedFixture fixture)
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

            referencePaths.Add(Path.GetFullPath(fixture.TargetAssemblyPath));
            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                operation = "prepareIntroducedTypes",
                sources = new[]
                {
                    new TransformWorkerSourceDto
                    {
                        sourcePath = fixture.SourcePath,
                        projectRelativePath = fixture.ProjectRelativePath
                    }
                },
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = referencePaths.ToArray(),
                targetTypesAssemblyPath = fixture.TargetAssemblyPath,
                targetAssemblyName = fixture.TargetAssemblyName,
                targetAssemblyMvid = fixture.TargetAssemblyMvid,
                assemblySourcePaths = Array.Empty<string>(),
                changedSiblingSourcePaths = Array.Empty<string>(),
                introducedTypeArtifacts = Array.Empty<TransformWorkerIntroducedTypeArtifactDto>()
            };
            return await TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        private static async Task<RetainedFixture> CreateFixtureAsync(string name)
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
            File.WriteAllText(sourcePath, EditedSource);
            string targetAssemblyPath = Path.Combine(directory, "RetainedTarget.dll");
            string targetAssemblyMvid = CreateTargetAssembly(targetAssemblyPath);
            string artifactPath = Path.Combine(directory, "RetainedArtifact.dll");
            CreateArtifactAssembly(artifactPath);
            RetainedFixture fixture = new RetainedFixture(
                sourcePath,
                "Assets/RetainedDeclaration/" + name + "/Edited.cs",
                targetAssemblyPath,
                "RetainedTarget",
                targetAssemblyMvid,
                artifactPath);
            // The recorded fingerprint has to be the value planning really produced for this
            // source, so it is read back from a prepare run rather than restated by the test.
            fixture.RetainedFingerprint = await ReadPlannedFingerprintAsync(fixture);
            return fixture;
        }

        private static async Task<string> ReadPlannedFingerprintAsync(RetainedFixture fixture)
        {
            TransformWorkerClientResult prepared = await RunPrepareAsync(fixture);
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

        private static string ReadAssemblyFullName(string path)
        {
            using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path))
            {
                return assembly.Name.FullName;
            }
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

            Assert.Fail("Compilation assembly was not found.");
            return null;
        }

        private sealed class RetainedFixture
        {
            public RetainedFixture(
                string sourcePath,
                string projectRelativePath,
                string targetAssemblyPath,
                string targetAssemblyName,
                string targetAssemblyMvid,
                string artifactPath)
            {
                SourcePath = sourcePath;
                ProjectRelativePath = projectRelativePath;
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

            public string RetainedFingerprint { get; set; }
        }
    }
}
