using System;
using System.IO;
using System.Linq;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for completing the reasons that name compiled types with the files
    /// declaring them, read from the target assembly's debug data.
    /// </summary>
    public class TransformWorkerCompiledTypeFileCompleterTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        // Any other assembly of this project: its PDB carries a different build id.
        private const string OtherAssemblyName = "Assembly-CSharp";
        private const string RegistryTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadBindingSplitRegistry";
        private const string RegistryProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadBindingSplitRegistry.cs";
        private const string MissingTypeMetadataName = "Example.Kinds";
        private const string WorkerPlacedPath = "Assets/Scripts/Kinds.cs";
        // An embedded package whose folder name differs from its package name, so its PDB records
        // the script under a path that is not the asset path.
        private const string PackageFixtureAssemblyName = "UnityCLILoop.Tests.HotReloadPackageFixture";
        private const string PackageFixtureTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.PackageFixture.HotReloadPackageFixture";
        private const string PackageFixtureRootAssetPath = "Packages/io.github.hatayama.uloop.hotreload-package-fixture";
        private const string PackageFixtureAssetPath =
            "Packages/io.github.hatayama.uloop.hotreload-package-fixture/Runtime/HotReloadPackageFixture.cs";
        private const string PackageFixturePhysicalPath =
            "Packages/uloop-hotreload-package-fixture/Runtime/HotReloadPackageFixture.cs";

        /// <summary>
        /// What: a split reason carried as the detail of another reason, as when a member reads an
        /// added property whose own body split, is completed with the declaring file too, so the
        /// composed row renders instead of throwing on a missing value.
        /// </summary>
        [Test]
        public void Complete_SplitReasonAsDetail_CompletesTheDetailWithTheDeclaringFile()
        {
            TransformWorkerReasonDto split = SplitReason();
            TransformWorkerOutputDto output = new TransformWorkerOutputDto
            {
                skipped = new[]
                {
                    new TransformWorkerSkippedDto
                    {
                        method = "Example.Reader.Read()",
                        reason = new TransformWorkerReasonDto
                        {
                            code = HotReloadWorkerReasonCode.AddedPropertyUnavailableAddedProperty,
                            detail = split
                        }
                    }
                }
            };

            CompleterWithNoPackageRoots().Complete(
                new TransformWorkerInputDto { targetTypesAssemblyPath = TargetAssemblyPath() },
                output);

            string text = HotReloadWorkerReasonText.Render(output.skipped[0].reason);
            Assert.That(text, Does.Contain("declared in '" + RegistryProjectRelativePath + "'"), text);
        }

        /// <summary>
        /// What: when the PDB beside the target assembly belongs to another build, the reason names
        /// the type alone instead of failing the reload, because the file only sharpens the hint.
        /// </summary>
        [Test]
        public void Complete_PdbOfAnotherBuild_NamesTheTypeInsteadOfThrowing()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CompletedTypeFileMismatch_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string dllPath = Path.Combine(directory, TestAssemblyName + ".dll");
                File.Copy(TargetAssemblyPath(), dllPath);
                File.Copy(OtherAssemblyPdbPath(), Path.ChangeExtension(dllPath, ".pdb"));
                TransformWorkerReasonDto split = SplitReason();
                TransformWorkerOutputDto output = new TransformWorkerOutputDto
                {
                    skipped = new[] { new TransformWorkerSkippedDto { method = "Example.Host.Wire()", reason = split } }
                };

                CompleterWithNoPackageRoots().Complete(
                    new TransformWorkerInputDto { targetTypesAssemblyPath = dllPath },
                    output);

                string text = HotReloadWorkerReasonText.Render(split);
                Assert.That(text, Does.Contain("declared in the file that declares '" + RegistryTypeMetadataName + "'"), text);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        /// What: a type the PDB places leaves its file in declaringFiles beside the sentence value.
        /// </summary>
        [Test]
        public void Complete_TypeThePdbPlaces_RecordsItsFileAsDeclaringFile()
        {
            TransformWorkerReasonDto split = SplitReason();

            Complete(split);

            Assert.That(split.declaringFiles, Is.EqualTo(new[] { RegistryProjectRelativePath }));
        }

        /// <summary>
        /// What: a type the PDB cannot place is named in the sentence and left out of
        /// declaringFiles, which becomes empty rather than staying unresolved.
        /// </summary>
        [Test]
        public void Complete_TypeThePdbCannotPlace_LeavesDeclaringFilesEmpty()
        {
            TransformWorkerReasonDto split = SplitReason();
            split.typeMetadataNames = new[] { MissingTypeMetadataName };

            Complete(split);

            Assert.That(split.declaringFiles, Is.Empty);
            Assert.That(split.args[3], Is.EqualTo("the file that declares '" + MissingTypeMetadataName + "'"));
        }

        /// <summary>
        /// What: files the worker already placed are kept and named in the sentence, even for a
        /// type the PDB cannot place, such as an enum with no method body.
        /// </summary>
        [Test]
        public void Complete_FilesTheWorkerPlaced_AreKeptAndNamedInTheSentence()
        {
            TransformWorkerReasonDto reason = new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.AddedMethodCallsIntroducedMemberBoundToCompiledType,
                args = new[] { "CS0266", "'Example.Behaviours'", "'" + MissingTypeMetadataName + "'" },
                typeMetadataNames = new[] { MissingTypeMetadataName },
                declaringFiles = new[] { WorkerPlacedPath }
            };

            Complete(reason);

            Assert.That(reason.declaringFiles, Is.EqualTo(new[] { WorkerPlacedPath }));
            Assert.That(reason.args[3], Is.EqualTo("'" + WorkerPlacedPath + "'"));
        }

        /// <summary>
        /// What: a type declared in an embedded package's script is named by the script's asset
        /// path, the form a reload takes, rather than by the folder path its PDB records.
        /// </summary>
        [Test]
        public void Complete_TypeDeclaredInAPackageSource_NamesTheAssetPath()
        {
            TransformWorkerReasonDto split = SplitReasonNaming(PackageFixtureTypeMetadataName);
            ScriptPackageRoot fixtureRoot = ScriptPackageRoots.ReadCurrent()
                .FirstOrDefault(root => root.AssetPath == PackageFixtureRootAssetPath);
            if (fixtureRoot == null)
            {
                Assert.Fail("The package fixture is not registered: " + PackageFixtureRootAssetPath);
            }

            CompleteFrom(
                PackageFixtureAssemblyPath(),
                new FixedPackageRootCapture(new[] { fixtureRoot }),
                split);

            Assert.That(split.declaringFiles, Is.EqualTo(new[] { PackageFixtureAssetPath }));
            string text = HotReloadWorkerReasonText.Render(split);
            Assert.That(text, Does.Contain("declared in '" + PackageFixtureAssetPath + "'"), text);
        }

        /// <summary>
        /// What: with no package roots captured, the same type is named by its folder path relative
        /// to the project, without the ./ the PDB records it with.
        /// </summary>
        [Test]
        public void Complete_TypeDeclaredInAPackageSource_WithNoPackageRoots_NamesThePhysicalPathWithoutTheDotSlash()
        {
            TransformWorkerReasonDto split = SplitReasonNaming(PackageFixtureTypeMetadataName);

            CompleteFrom(PackageFixtureAssemblyPath(), NoPackageRoots(), split);

            Assert.That(split.declaringFiles, Is.EqualTo(new[] { PackageFixturePhysicalPath }));
        }

        private static void Complete(TransformWorkerReasonDto reason)
        {
            CompleteFrom(TargetAssemblyPath(), NoPackageRoots(), reason);
        }

        private static void CompleteFrom(
            string targetTypesAssemblyPath,
            IHotReloadPackageRootCapture packageRootCapture,
            TransformWorkerReasonDto reason)
        {
            TransformWorkerOutputDto output = new TransformWorkerOutputDto
            {
                skipped = new[] { new TransformWorkerSkippedDto { method = "Example.Host.Wire()", reason = reason } }
            };
            new TransformWorkerCompiledTypeFileCompleter(packageRootCapture).Complete(
                new TransformWorkerInputDto { targetTypesAssemblyPath = targetTypesAssemblyPath },
                output);
        }

        private static TransformWorkerCompiledTypeFileCompleter CompleterWithNoPackageRoots()
        {
            return new TransformWorkerCompiledTypeFileCompleter(NoPackageRoots());
        }

        // Enough for the tests reading this test assembly's own PDB: it records Assets scripts
        // only, which no package root claims.
        private static IHotReloadPackageRootCapture NoPackageRoots()
        {
            return new FixedPackageRootCapture(Array.Empty<ScriptPackageRoot>());
        }

        private static TransformWorkerReasonDto SplitReason()
        {
            return SplitReasonNaming(RegistryTypeMetadataName);
        }

        private static TransformWorkerReasonDto SplitReasonNaming(string typeMetadataName)
        {
            return new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature,
                args = new[] { "CS1503", "'Example.Payload'", "'" + typeMetadataName + "'" },
                typeMetadataNames = new[] { typeMetadataName }
            };
        }

        private static string PackageFixtureAssemblyPath()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Library", "ScriptAssemblies", PackageFixtureAssemblyName + ".dll"));
            Assert.That(File.Exists(path), Is.True, "Package fixture dll missing: " + path);
            return path;
        }

        private static string OtherAssemblyPdbPath()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Library", "ScriptAssemblies", OtherAssemblyName + ".pdb"));
            Assert.That(File.Exists(path), Is.True, "Other assembly pdb missing: " + path);
            return path;
        }

        private static string TargetAssemblyPath()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Library", "ScriptAssemblies", TestAssemblyName + ".dll"));
            Assert.That(File.Exists(path), Is.True, "Test assembly dll missing: " + path);
            return path;
        }
    }
}
