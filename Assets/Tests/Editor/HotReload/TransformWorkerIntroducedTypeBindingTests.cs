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
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies that a reference which moves from a source declaration to a retained artifact
    /// assembly normalizes back to the same original identity, so the declaration fingerprint
    /// describes the definition rather than where the definition currently lives.
    /// </summary>
    public class TransformWorkerIntroducedTypeBindingTests
    {
        private const string RetainedProjectRelativePath = "Assets/Retained.cs";

        // The records these tests use only have to be well formed; what they assert is the
        // fingerprint of a dependent type, never the removal of the retained declaration.
        private const string PlaceholderDeclarationFingerprint =
            "0000000000000000000000000000000000000000000000000000000000000000";

        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        private const string DirectDependentSource =
            "namespace Example { public class Dependent { public int Read(Retained retained) { return retained.Value; } } }";

        // Reaches the retained type without naming it: the only occurrence is inside the type
        // returned by the member the declaration calls. A fingerprint that collapses a bound
        // symbol to one type records the collection type and loses the retained type.
        private const string IndirectDependentSource =
            "using System.Collections.Generic; namespace Example { public static class Holder { public static List<Retained> All() { return null; } } public class Dependent { public int Count() { return Holder.All().Count; } } }";

        /// <summary>
        /// Verifies that the fingerprint of a type is unchanged when the type it depends on stops
        /// being a source declaration and is bound from a retained artifact assembly instead.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_DependencyMovedToArtifact_KeepsFingerprint()
        {
            BindingFixture fixture = CreateFixture("DependencyMovedToArtifact", DirectDependentSource);

            TransformWorkerClientResult beforeSwitch = await TransformWorkerClient.RunAsync(
                CreateInput(fixture, includeRetainedSource: true, Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult afterSwitch = await TransformWorkerClient.RunAsync(
                CreateInput(fixture, includeRetainedSource: false, new[] { CreateRetainedArtifact(fixture) },
                    Array.Empty<string>()),
                CancellationToken.None);

            Assert.That(beforeSwitch.Success, Is.True, beforeSwitch.ErrorMessage);
            Assert.That(afterSwitch.Success, Is.True, afterSwitch.ErrorMessage);
            Assert.That(
                FindFingerprint(beforeSwitch, "Example.Dependent"),
                Is.EqualTo(FindFingerprint(afterSwitch, "Example.Dependent")),
                "Moving the dependency into a retained artifact must not change the definition.");
        }

        /// <summary>
        /// Verifies that adding an artifact the declaration does not depend on leaves the
        /// fingerprint unchanged, so an unrelated retained type cannot invalidate it.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_UnrelatedArtifactAdded_KeepsFingerprint()
        {
            BindingFixture fixture = CreateFixture("UnrelatedArtifactAdded", DirectDependentSource);
            string unrelatedPath = Path.Combine(fixture.Directory, "Unrelated.dll");
            CreateArtifactAssembly(unrelatedPath, "UnrelatedArtifact", "Example", "Unrelated");
            TransformWorkerIntroducedTypeArtifactDto unrelatedArtifact = new TransformWorkerIntroducedTypeArtifactDto
            {
                assemblyFullName = ReadAssemblyFullName(unrelatedPath),
                referencePath = unrelatedPath,
                types = new[]
                {
                    new TransformWorkerIntroducedTypeArtifactTypeDto
                    {
                        metadataName = "Example.Unrelated",
                        originalAssemblyName = fixture.TargetAssemblyName,
                        originalAssemblyMvid = fixture.TargetAssemblyMvid,
                        ownerProjectRelativePath = RetainedProjectRelativePath,
                        declarationFingerprint = PlaceholderDeclarationFingerprint
                    }
                }
            };

            TransformWorkerClientResult withoutArtifact = await TransformWorkerClient.RunAsync(
                CreateInput(fixture, includeRetainedSource: true, Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult withArtifact = await TransformWorkerClient.RunAsync(
                CreateInput(fixture, includeRetainedSource: true, new[] { unrelatedArtifact },
                    Array.Empty<string>()),
                CancellationToken.None);

            Assert.That(withoutArtifact.Success, Is.True, withoutArtifact.ErrorMessage);
            Assert.That(withArtifact.Success, Is.True, withArtifact.ErrorMessage);
            Assert.That(
                FindFingerprint(withArtifact, "Example.Dependent"),
                Is.EqualTo(FindFingerprint(withoutArtifact, "Example.Dependent")));
        }

        /// <summary>
        /// Verifies that the retained type really binds against the artifact assembly and that the
        /// record is what maps its identity back: referencing the same artifact without a record
        /// leaves the artifact assembly identity in the fingerprint, so it stops matching the run
        /// in which the type was still a source declaration.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ArtifactReferencedWithoutRecord_ChangesFingerprint()
        {
            BindingFixture fixture = CreateFixture("ArtifactWithoutRecord", DirectDependentSource);

            TransformWorkerClientResult sourceDeclared = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: true,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult recorded = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: false,
                    new[] { CreateRetainedArtifact(fixture) },
                    Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult unrecorded = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: false,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    new[] { fixture.RetainedArtifactPath }),
                CancellationToken.None);

            Assert.That(sourceDeclared.Success, Is.True, sourceDeclared.ErrorMessage);
            Assert.That(recorded.Success, Is.True, recorded.ErrorMessage);
            Assert.That(unrecorded.Success, Is.True, unrecorded.ErrorMessage);
            Assert.That(
                FindFingerprint(recorded, "Example.Dependent"),
                Is.EqualTo(FindFingerprint(sourceDeclared, "Example.Dependent")));
            Assert.That(
                FindFingerprint(unrecorded, "Example.Dependent"),
                Is.Not.EqualTo(FindFingerprint(sourceDeclared, "Example.Dependent")),
                "Without a record the artifact assembly identity must stay in the fingerprint.");
        }

        /// <summary>
        /// Verifies that the fingerprint changes when the artifact record attributes the retained
        /// type to a different original assembly, so normalization follows the recorded identity
        /// rather than erasing the dependency.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ArtifactOriginalIdentityChanged_ChangesFingerprint()
        {
            BindingFixture fixture = CreateFixture("ArtifactOriginalIdentityChanged", DirectDependentSource);
            TransformWorkerIntroducedTypeArtifactDto recorded = CreateRetainedArtifact(fixture);
            TransformWorkerIntroducedTypeArtifactDto reattributed = CreateRetainedArtifact(fixture);
            reattributed.types[0].originalAssemblyMvid = Guid.NewGuid().ToString();

            TransformWorkerClientResult first = await TransformWorkerClient.RunAsync(
                CreateInput(fixture, includeRetainedSource: false, new[] { recorded }, Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult second = await TransformWorkerClient.RunAsync(
                CreateInput(fixture, includeRetainedSource: false, new[] { reattributed }, Array.Empty<string>()),
                CancellationToken.None);

            Assert.That(first.Success, Is.True, first.ErrorMessage);
            Assert.That(second.Success, Is.True, second.ErrorMessage);
            Assert.That(
                FindFingerprint(second, "Example.Dependent"),
                Is.Not.EqualTo(FindFingerprint(first, "Example.Dependent")));
        }

        /// <summary>
        /// Verifies that a type with the same metadata name coming from an assembly no artifact
        /// record names is not normalized, so the mapping cannot be driven by a metadata name alone.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_SameNameFromUnlistedAssembly_ChangesFingerprint()
        {
            BindingFixture fixture = CreateFixture("SameNameFromUnlistedAssembly", DirectDependentSource);
            string foreignPath = Path.Combine(fixture.Directory, "ForeignRetained.dll");
            CreateRetainedArtifactAssembly(foreignPath, "ForeignRetained");

            TransformWorkerClientResult sourceDeclared = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: true,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult unlisted = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: false,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    new[] { foreignPath }),
                CancellationToken.None);

            Assert.That(sourceDeclared.Success, Is.True, sourceDeclared.ErrorMessage);
            Assert.That(unlisted.Success, Is.True, unlisted.ErrorMessage);
            Assert.That(
                FindFingerprint(unlisted, "Example.Dependent"),
                Is.Not.EqualTo(FindFingerprint(sourceDeclared, "Example.Dependent")),
                "An unlisted assembly must not be normalized just because the metadata name matches.");
        }

        /// <summary>
        /// Verifies that an artifact record claiming an identity the referenced assembly does not
        /// report produces a diagnostic instead of a descriptor.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ArtifactIdentityMismatch_ReportsNoIntroducedType()
        {
            BindingFixture fixture = CreateFixture("ArtifactIdentityMismatch", DirectDependentSource);
            TransformWorkerIntroducedTypeArtifactDto mismatched = CreateRetainedArtifact(fixture);
            mismatched.assemblyFullName = ReadAssemblyFullName(fixture.TargetAssemblyPath);

            await AssertArtifactIsRejected(fixture, mismatched);
        }

        /// <summary>
        /// Verifies that an artifact record listing a type the referenced assembly does not hold
        /// produces a diagnostic instead of a descriptor.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ArtifactMissingMetadataName_ReportsNoIntroducedType()
        {
            BindingFixture fixture = CreateFixture("ArtifactMissingMetadataName", DirectDependentSource);
            TransformWorkerIntroducedTypeArtifactDto missing = CreateRetainedArtifact(fixture);
            missing.types[0].metadataName = "Example.Absent";

            await AssertArtifactIsRejected(fixture, missing);
        }

        /// <summary>
        /// Verifies that two artifacts normalizing to the same original type are rejected, because
        /// the fingerprint would otherwise depend on which record was consulted first.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_TwoArtifactsNormalizeToSameType_ReportsNoIntroducedType()
        {
            BindingFixture fixture = CreateFixture("TwoArtifactsNormalizeToSameType", DirectDependentSource);
            string secondPath = Path.Combine(fixture.Directory, "SecondRetained.dll");
            CreateRetainedArtifactAssembly(secondPath, "SecondRetained");
            TransformWorkerIntroducedTypeArtifactDto duplicate = new TransformWorkerIntroducedTypeArtifactDto
            {
                assemblyFullName = ReadAssemblyFullName(secondPath),
                referencePath = secondPath,
                types = new[]
                {
                    new TransformWorkerIntroducedTypeArtifactTypeDto
                    {
                        metadataName = "Example.Retained",
                        originalAssemblyName = fixture.TargetAssemblyName,
                        originalAssemblyMvid = fixture.TargetAssemblyMvid,
                        ownerProjectRelativePath = RetainedProjectRelativePath,
                        declarationFingerprint = PlaceholderDeclarationFingerprint
                    }
                }
            };

            TransformWorkerClientResult result = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: false,
                    new[] { CreateRetainedArtifact(fixture), duplicate },
                    Array.Empty<string>()),
                CancellationToken.None);

            AssertPreparationWasRefused(result);
        }

        private static async Task AssertArtifactIsRejected(
            BindingFixture fixture,
            TransformWorkerIntroducedTypeArtifactDto artifact)
        {
            TransformWorkerClientResult result = await TransformWorkerClient.RunAsync(
                CreateInput(fixture, includeRetainedSource: true, new[] { artifact }, Array.Empty<string>()),
                CancellationToken.None);

            AssertPreparationWasRefused(result);
        }

        private static void AssertPreparationWasRefused(TransformWorkerClientResult result)
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            foreach (TransformWorkerFileOutputDto file in result.Output.files)
            {
                Assert.That(file.introducedTypes, Is.Empty);
                Assert.That(file.introducedTypeDiagnostics, Is.Not.Empty);
            }
        }

        /// <summary>
        /// Verifies that a declaration reaching the retained type only through a constructed
        /// generic and an array keeps its fingerprint when that type moves into an artifact.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_IndirectDependencyMovedToArtifact_KeepsFingerprint()
        {
            BindingFixture fixture = CreateFixture("IndirectDependencyMoved", IndirectDependentSource);

            TransformWorkerClientResult beforeSwitch = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: true,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult afterSwitch = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: false,
                    new[] { CreateRetainedArtifact(fixture) },
                    Array.Empty<string>()),
                CancellationToken.None);

            Assert.That(beforeSwitch.Success, Is.True, beforeSwitch.ErrorMessage);
            Assert.That(afterSwitch.Success, Is.True, afterSwitch.ErrorMessage);
            Assert.That(
                FindFingerprint(afterSwitch, "Example.Dependent"),
                Is.EqualTo(FindFingerprint(beforeSwitch, "Example.Dependent")));
        }

        /// <summary>
        /// Verifies that swapping the type behind a constructed generic and an array for a
        /// same-named type in another assembly changes the fingerprint, so a dependency that only
        /// appears inside a type argument or an element type is really recorded.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_IndirectDependencySwappedForForeignType_ChangesFingerprint()
        {
            BindingFixture fixture = CreateFixture("IndirectDependencySwapped", IndirectDependentSource);
            string foreignPath = Path.Combine(fixture.Directory, "ForeignRetained.dll");
            CreateRetainedArtifactAssembly(foreignPath, "ForeignRetained");

            TransformWorkerClientResult sourceDeclared = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: true,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult foreignBound = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: false,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    new[] { foreignPath }),
                CancellationToken.None);

            Assert.That(sourceDeclared.Success, Is.True, sourceDeclared.ErrorMessage);
            Assert.That(foreignBound.Success, Is.True, foreignBound.ErrorMessage);
            Assert.That(
                FindFingerprint(foreignBound, "Example.Dependent"),
                Is.Not.EqualTo(FindFingerprint(sourceDeclared, "Example.Dependent")));
        }

        private static string FindFingerprint(TransformWorkerClientResult result, string metadataName)
        {
            foreach (TransformWorkerFileOutputDto file in result.Output.files)
            {
                foreach (TransformWorkerIntroducedTypeDto introducedType in file.introducedTypes)
                {
                    if (introducedType.metadataName == metadataName)
                    {
                        return introducedType.declarationFingerprint;
                    }
                }
            }

            Assert.Fail("No introduced type named " + metadataName + " was reported.");
            return null;
        }

        private static BindingFixture CreateFixture(string name, string dependentSource)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(
                projectRoot,
                "Library",
                "UloopHotReload",
                "TestSources",
                "IntroducedTypeBinding",
                name);
            Directory.CreateDirectory(directory);
            string dependentSourcePath = Path.Combine(directory, "Dependent.cs");
            string retainedSourcePath = Path.Combine(directory, "Retained.cs");
            File.WriteAllText(dependentSourcePath, dependentSource);
            File.WriteAllText(
                retainedSourcePath,
                "namespace Example { public class Retained { public int Value; } }");
            string targetAssemblyPath = Path.Combine(directory, "BindingTarget.dll");
            string targetAssemblyMvid = CreateArtifactAssembly(
                targetAssemblyPath,
                "BindingTarget",
                "Example",
                "Unrelated");
            string retainedArtifactPath = Path.Combine(directory, "RetainedArtifact.dll");
            CreateRetainedArtifactAssembly(retainedArtifactPath, "RetainedArtifact");
            return new BindingFixture(
                directory,
                dependentSourcePath,
                retainedSourcePath,
                targetAssemblyPath,
                "BindingTarget",
                targetAssemblyMvid,
                retainedArtifactPath);
        }

        private static TransformWorkerIntroducedTypeArtifactDto CreateRetainedArtifact(BindingFixture fixture)
        {
            return new TransformWorkerIntroducedTypeArtifactDto
            {
                assemblyFullName = ReadAssemblyFullName(fixture.RetainedArtifactPath),
                referencePath = fixture.RetainedArtifactPath,
                types = new[]
                {
                    new TransformWorkerIntroducedTypeArtifactTypeDto
                    {
                        metadataName = "Example.Retained",
                        originalAssemblyName = fixture.TargetAssemblyName,
                        originalAssemblyMvid = fixture.TargetAssemblyMvid,
                        ownerProjectRelativePath = RetainedProjectRelativePath,
                        declarationFingerprint = PlaceholderDeclarationFingerprint
                    }
                }
            };
        }

        private static TransformWorkerInputDto CreateInput(
            BindingFixture fixture,
            bool includeRetainedSource,
            TransformWorkerIntroducedTypeArtifactDto[] artifacts,
            string[] extraReferencePaths)
        {
            UnityEditor.Compilation.Assembly compilationAssembly = FindCompilationAssembly();
            List<TransformWorkerSourceDto> sources = new List<TransformWorkerSourceDto>
            {
                new TransformWorkerSourceDto
                {
                    sourcePath = fixture.DependentSourcePath,
                    projectRelativePath = "Assets/Dependent.cs"
                }
            };
            if (includeRetainedSource)
            {
                sources.Add(
                    new TransformWorkerSourceDto
                    {
                        sourcePath = fixture.RetainedSourcePath,
                        projectRelativePath = "Assets/Retained.cs"
                    });
            }

            List<string> referencePaths = new List<string>();
            foreach (string reference in compilationAssembly.allReferences)
            {
                if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                {
                    referencePaths.Add(Path.GetFullPath(reference));
                }
            }

            referencePaths.Add(Path.GetFullPath(fixture.TargetAssemblyPath));
            foreach (string extraReferencePath in extraReferencePaths)
            {
                referencePaths.Add(Path.GetFullPath(extraReferencePath));
            }

            return new TransformWorkerInputDto
            {
                operation = "prepareIntroducedTypes",
                sources = sources.ToArray(),
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = referencePaths.ToArray(),
                targetTypesAssemblyPath = fixture.TargetAssemblyPath,
                targetAssemblyName = fixture.TargetAssemblyName,
                targetAssemblyMvid = fixture.TargetAssemblyMvid,
                assemblySourcePaths = Array.Empty<string>(),
                changedSiblingSourcePaths = Array.Empty<string>(),
                introducedTypeArtifacts = artifacts
            };
        }

        private static string CreateArtifactAssembly(
            string path,
            string assemblyName,
            string typeNamespace,
            string typeName)
        {
            AssemblyNameDefinition assemblyNameDefinition = new AssemblyNameDefinition(
                assemblyName,
                new Version(1, 0, 0, 0));
            using (AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                assemblyNameDefinition,
                assemblyName,
                ModuleKind.Dll))
            {
                TypeDefinition type = new TypeDefinition(
                    typeNamespace,
                    typeName,
                    CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                    assembly.MainModule.TypeSystem.Object);
                assembly.MainModule.Types.Add(type);
                assembly.Write(path);
            }

            using (ModuleDefinition module = ModuleDefinition.ReadModule(path))
            {
                return module.Mvid.ToString();
            }
        }

        private static void CreateRetainedArtifactAssembly(string path, string assemblyName)
        {
            AssemblyNameDefinition assemblyNameDefinition = new AssemblyNameDefinition(
                assemblyName,
                new Version(1, 0, 0, 0));
            using (AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                assemblyNameDefinition,
                assemblyName,
                ModuleKind.Dll))
            {
                TypeDefinition retainedType = new TypeDefinition(
                    "Example",
                    "Retained",
                    CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                    assembly.MainModule.TypeSystem.Object);
                FieldDefinition valueField = new FieldDefinition(
                    "Value",
                    Mono.Cecil.FieldAttributes.Public,
                    assembly.MainModule.TypeSystem.Int32);
                retainedType.Fields.Add(valueField);
                assembly.MainModule.Types.Add(retainedType);
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

        private sealed class BindingFixture
        {
            public BindingFixture(
                string directory,
                string dependentSourcePath,
                string retainedSourcePath,
                string targetAssemblyPath,
                string targetAssemblyName,
                string targetAssemblyMvid,
                string retainedArtifactPath)
            {
                Directory = directory;
                DependentSourcePath = dependentSourcePath;
                RetainedSourcePath = retainedSourcePath;
                TargetAssemblyPath = targetAssemblyPath;
                TargetAssemblyName = targetAssemblyName;
                TargetAssemblyMvid = targetAssemblyMvid;
                RetainedArtifactPath = retainedArtifactPath;
            }

            public string Directory { get; }

            public string DependentSourcePath { get; }

            public string RetainedSourcePath { get; }

            public string TargetAssemblyPath { get; }

            public string TargetAssemblyName { get; }

            public string TargetAssemblyMvid { get; }

            public string RetainedArtifactPath { get; }
        }
    }
}
