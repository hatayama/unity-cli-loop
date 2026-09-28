using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;

using Mono.Cecil;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using ReflectionAssembly = System.Reflection.Assembly;
using ReflectionAssemblyName = System.Reflection.AssemblyName;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies the references an introduced-type artifact compiles against: the worker's raw
    /// references while the runtime grant is unavailable, and internals-only copies of the target
    /// assembly and its retained artifacts once it is available.
    /// </summary>
    public sealed class HotReloadIntroducedTypeArtifactReferenceBuilderTests
    {
        private const string ExposureFailurePrefix = "Exposing internal members of the referenced assemblies failed: ";
        private const string MissingImageAssertion = "home.DllPath must point to an existing DLL.";

        private static readonly string CorlibPath = typeof(object).Assembly.Location;
        private static readonly string CecilPath = typeof(AssemblyDefinition).Assembly.Location;

        private readonly List<InternalsExposureTestImage> _images = new List<InternalsExposureTestImage>();
        private HotReloadDomainTestScope _scope;
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            _projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (InternalsExposureTestImage image in _images)
            {
                foreach (string copyPath in ExposedCopiesOf(image))
                {
                    File.Delete(copyPath);
                }

                image.Dispose();
            }

            _images.Clear();
            _scope.Dispose();
        }

        /// <summary>
        /// Verifies an unavailable grant keeps the worker's raw references, retained artifacts
        /// included, and writes no internals-only copy.
        /// </summary>
        [Test]
        public void Build_UnavailableGrant_UsesRawReferences()
        {
            InternalsExposureTestImage target = CreateImage();
            InternalsExposureTestImage retained = CreateImage();
            TransformWorkerInputDto input = CreateInput(target, ActivateRetainedArtifact(retained));

            HotReloadArtifactCompileReferences references = Build(input, target, false);

            Assert.That(references.Success, Is.True);
            Assert.That(references.ExposesInternals, Is.False);
            Assert.That(references.ErrorMessage, Is.Empty);
            Assert.That(
                references.Paths,
                Is.EqualTo(new[] { CorlibPath, TargetPath(target), CecilPath, Path.GetFullPath(retained.DllPath) }));
            Assert.That(
                references.Paths,
                Is.EqualTo(HotReloadIntroducedTypeArtifactReferenceBuilder.BuildRawReferencePaths(input)));
            Assert.That(ExposedCopiesOf(target), Is.Empty);
            Assert.That(ExposedCopiesOf(retained), Is.Empty);
        }

        /// <summary>
        /// Verifies an available grant replaces only the target reference, in place, by a copy whose
        /// internal type is public and whose private member stays private, while every other
        /// reference keeps its raw path and position.
        /// </summary>
        [Test]
        public void Build_AvailableGrant_ExposesOnlyTarget()
        {
            InternalsExposureTestImage target = CreateImage();

            HotReloadArtifactCompileReferences references = Build(CreateInput(target), target, true);

            Assert.That(references.Success, Is.True);
            Assert.That(references.ExposesInternals, Is.True);
            Assert.That(references.ErrorMessage, Is.Empty);
            Assert.That(references.Paths, Is.EqualTo(new[] { CorlibPath, ExposedCopyPath(target), CecilPath }));
            AssertInternalsOnlyCopy(references.Paths[1]);
        }

        /// <summary>
        /// Verifies a retained artifact of the target is referenced through its internals-only copy
        /// and never also through its raw image, which would reference one assembly identity twice.
        /// </summary>
        [Test]
        public void Build_AvailableGrant_ExposesRetainedArtifacts()
        {
            InternalsExposureTestImage target = CreateImage();
            InternalsExposureTestImage retained = CreateImage();
            TransformWorkerInputDto input = CreateInput(target, ActivateRetainedArtifact(retained));

            HotReloadArtifactCompileReferences references = Build(input, target, true);

            Assert.That(
                references.Paths,
                Is.EqualTo(new[] { CorlibPath, ExposedCopyPath(target), CecilPath, ExposedCopyPath(retained) }));
            Assert.That(references.Paths, Has.No.Member(Path.GetFullPath(retained.DllPath)));
            AssertInternalsOnlyCopy(references.Paths[3]);
        }

        /// <summary>
        /// Verifies worker references that lack the target assembly are rejected before a copy of the
        /// target or of a retained artifact is written.
        /// </summary>
        [Test]
        public void Build_MissingTarget_ThrowsBeforeCopying()
        {
            InternalsExposureTestImage target = CreateImage();
            InternalsExposureTestImage retained = CreateImage();
            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                referencePaths = new[] { CorlibPath, CecilPath },
                introducedTypeArtifacts = new[] { ActivateRetainedArtifact(retained) }
            };

            Assert.Throws<InvalidOperationException>(() => Build(input, target, true));

            Assert.That(ExposedCopiesOf(target), Is.Empty);
            Assert.That(ExposedCopiesOf(retained), Is.Empty);
        }

        /// <summary>
        /// Verifies absent, empty, null, and path-less artifact records add nothing, even when a
        /// path-less record names an artifact this domain retains, matching the raw references.
        /// </summary>
        [TestCase("missing")]
        [TestCase("empty")]
        [TestCase("blank-records")]
        public void Build_EmptyArtifacts_PreservesOtherReferences(string artifactsKind)
        {
            InternalsExposureTestImage target = CreateImage();
            InternalsExposureTestImage retained = CreateImage();
            TransformWorkerIntroducedTypeArtifactDto[] artifacts = null;
            if (artifactsKind == "empty")
            {
                artifacts = new TransformWorkerIntroducedTypeArtifactDto[0];
            }
            else if (artifactsKind == "blank-records")
            {
                string retainedName = ActivateRetainedArtifact(retained).assemblyFullName;
                artifacts = new[]
                {
                    null,
                    new TransformWorkerIntroducedTypeArtifactDto { assemblyFullName = retainedName, referencePath = string.Empty },
                    new TransformWorkerIntroducedTypeArtifactDto { assemblyFullName = retainedName, referencePath = null }
                };
            }

            HotReloadArtifactCompileReferences references = Build(CreateInput(target, artifacts), target, true);

            Assert.That(references.Paths, Is.EqualTo(new[] { CorlibPath, ExposedCopyPath(target), CecilPath }));
            Assert.That(ExposedCopiesOf(retained), Is.Empty);
        }

        /// <summary>
        /// Verifies an artifact record this domain does not retain keeps its raw path, once, whether
        /// its assembly name is unknown or missing, leaving the compiler to judge it.
        /// </summary>
        [Test]
        public void Build_UnresolvedArtifact_KeepsRawPath()
        {
            InternalsExposureTestImage target = CreateImage();
            string unknownName = "NotRetained_" + Guid.NewGuid().ToString("N");
            string unknownPath = Path.Combine(Application.temporaryCachePath, unknownName + ".dll");
            string namelessPath = Path.Combine(Application.temporaryCachePath, "Nameless_" + unknownName + ".dll");
            TransformWorkerInputDto input = CreateInput(
                target,
                new TransformWorkerIntroducedTypeArtifactDto { assemblyFullName = unknownName, referencePath = unknownPath },
                new TransformWorkerIntroducedTypeArtifactDto { assemblyFullName = null, referencePath = namelessPath },
                new TransformWorkerIntroducedTypeArtifactDto
                {
                    assemblyFullName = unknownName,
                    referencePath = Path.Combine(Application.temporaryCachePath, ".", unknownName + ".dll")
                });

            HotReloadArtifactCompileReferences references = Build(input, target, true);

            Assert.That(references.ExposesInternals, Is.True);
            Assert.That(
                references.Paths,
                Is.EqualTo(new[]
                {
                    CorlibPath,
                    ExposedCopyPath(target),
                    CecilPath,
                    Path.GetFullPath(unknownPath),
                    Path.GetFullPath(namelessPath)
                }));
        }

        /// <summary>
        /// Verifies a copy whose metadata needs an assembly Cecil cannot resolve fails the build with
        /// the resolution message instead of falling back to raw references, for the target and for a
        /// retained artifact alike, and leaves no cached copy of the failed image behind.
        /// </summary>
        [TestCase("target")]
        [TestCase("retained")]
        public void Build_ReferenceResolutionFailure_ReturnsFailed(string failingImage)
        {
            string externalName = "UnresolvableEnumFixture_" + Guid.NewGuid().ToString("N");
            InternalsExposureTestImage unresolvable = CreateImageWithConstantOfMissingEnum(externalName);
            InternalsExposureTestImage target = failingImage == "target" ? unresolvable : CreateImage();
            TransformWorkerInputDto input = failingImage == "target"
                ? CreateInput(target)
                : CreateInput(target, ActivateRetainedArtifact(unresolvable));

            HotReloadArtifactCompileReferences references = Build(input, target, true);

            Assert.That(references.Success, Is.False);
            Assert.That(references.Paths, Is.Null);
            Assert.That(references.ExposesInternals, Is.False);
            Assert.That(references.ErrorMessage, Does.StartWith(ExposureFailurePrefix));
            Assert.That(references.ErrorMessage, Does.Contain(externalName));
            Assert.That(ExposedCopiesOf(unresolvable), Is.Empty);
        }

        /// <summary>
        /// Verifies building internals-only references leaves the shared worker input untouched, so
        /// the worker runs and the shim compilation keep binding against the raw references.
        /// </summary>
        [Test]
        public void Build_DoesNotMutateWorkerInput()
        {
            InternalsExposureTestImage target = CreateImage();
            InternalsExposureTestImage retained = CreateImage();
            TransformWorkerIntroducedTypeArtifactDto retainedRecord = ActivateRetainedArtifact(retained);
            string retainedFullName = retainedRecord.assemblyFullName;
            string unresolvedPath = Path.Combine(Application.temporaryCachePath, ".", "Unresolved.dll");
            TransformWorkerIntroducedTypeArtifactDto unresolvedRecord = new TransformWorkerIntroducedTypeArtifactDto
            {
                assemblyFullName = "NotRetained_" + Guid.NewGuid().ToString("N"),
                referencePath = unresolvedPath
            };
            string[] referencePaths = { CorlibPath, TargetPath(target), CecilPath };
            TransformWorkerIntroducedTypeArtifactDto[] artifacts = { retainedRecord, unresolvedRecord };
            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                referencePaths = referencePaths,
                introducedTypeArtifacts = artifacts
            };

            HotReloadArtifactCompileReferences references = Build(input, target, true);

            Assert.That(references.ExposesInternals, Is.True);
            Assert.That(references.Paths, Is.Not.SameAs(referencePaths));
            Assert.That(input.referencePaths, Is.SameAs(referencePaths));
            Assert.That(referencePaths, Is.EqualTo(new[] { CorlibPath, TargetPath(target), CecilPath }));
            Assert.That(input.introducedTypeArtifacts, Is.SameAs(artifacts));
            Assert.That(artifacts, Is.EqualTo(new[] { retainedRecord, unresolvedRecord }));
            Assert.That(retainedRecord.assemblyFullName, Is.EqualTo(retainedFullName));
            Assert.That(retainedRecord.referencePath, Is.EqualTo(retained.DllPath));
            Assert.That(unresolvedRecord.referencePath, Is.EqualTo(unresolvedPath));
        }

        /// <summary>
        /// Verifies the target is found by the full-path rule that already removes duplicate
        /// references, so another spelling of the target path is still replaced by its copy and the
        /// raw image is not left beside it.
        /// </summary>
        [TestCase("dot-segment")]
        [TestCase("file-name-case")]
        [TestCase("alternate-separator")]
        public void Build_PathComparison_PreservesPlatformPolicy(string spelling)
        {
            InternalsExposureTestImage target = CreateImage();
            string targetPath = TargetPath(target);
            string directory = Path.GetDirectoryName(targetPath);
            string fileName = Path.GetFileName(targetPath);
            string spelledTarget = spelling == "dot-segment"
                ? directory + Path.DirectorySeparatorChar + "." + Path.DirectorySeparatorChar + fileName
                : spelling == "file-name-case"
                    ? Path.Combine(directory, fileName.ToUpperInvariant())
                    : SpellWithAlternateSeparator(targetPath);
            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                referencePaths = new[] { CorlibPath, spelledTarget, CecilPath }
            };

            HotReloadArtifactCompileReferences references = Build(input, target, true);

            Assert.That(references.Paths, Is.EqualTo(new[] { CorlibPath, ExposedCopyPath(target), CecilPath }));
        }

        /// <summary>
        /// Verifies an image deleted after worker preparation fails the internals-only build with its
        /// IO exception instead of a raw fallback, and changes neither the worker input nor the
        /// artifacts the domain keeps active.
        /// </summary>
        [TestCase("target")]
        [TestCase("retained")]
        public void Build_ReferenceDisappearedAfterPreparation_PropagatesIoFailure(string deletedImage)
        {
            InternalsExposureTestImage target = CreateImage();
            InternalsExposureTestImage retained = CreateImage();
            TransformWorkerIntroducedTypeArtifactDto retainedRecord = ActivateRetainedArtifact(retained);
            TransformWorkerInputDto input = CreateInput(target, retainedRecord);
            string[] referencePaths = input.referencePaths;
            string[] referencePathsBefore = (string[])referencePaths.Clone();
            HotReloadIntroducedTypeRegistry registry = HotReloadCompositionRoot.Services.Domain.IntroducedTypes;
            int activeCountBefore = registry.ActiveCount;
            InternalsExposureTestImage deleted = deletedImage == "target" ? target : retained;
            File.Delete(deleted.DllPath);
            LogAssert.Expect(LogType.Assert, MissingImageAssertion);

            Assert.Catch<IOException>(() => Build(input, target, true));

            Assert.That(input.referencePaths, Is.SameAs(referencePaths));
            Assert.That(referencePaths, Is.EqualTo(referencePathsBefore));
            Assert.That(input.introducedTypeArtifacts, Is.EqualTo(new[] { retainedRecord }));
            Assert.That(registry.ActiveCount, Is.EqualTo(activeCountBefore));
            Assert.That(ExposedCopiesOf(deleted), Is.Empty);
        }

        /// <summary>
        /// Verifies raw references keep a path whose image disappeared after worker preparation,
        /// leaving the compiler to report it exactly as before.
        /// </summary>
        [TestCase("target")]
        [TestCase("retained")]
        public void Build_RawReferenceDisappeared_KeepsPath(string deletedImage)
        {
            InternalsExposureTestImage target = CreateImage();
            InternalsExposureTestImage retained = CreateImage();
            TransformWorkerInputDto input = CreateInput(target, ActivateRetainedArtifact(retained));
            File.Delete(deletedImage == "target" ? target.DllPath : retained.DllPath);

            HotReloadArtifactCompileReferences references = Build(input, target, false);

            Assert.That(references.ExposesInternals, Is.False);
            Assert.That(
                references.Paths,
                Is.EqualTo(new[] { CorlibPath, TargetPath(target), CecilPath, Path.GetFullPath(retained.DllPath) }));
        }

        /// <summary>
        /// Verifies raw and exposed results keep a read-only copy of their paths and report exposure
        /// only through the factory that built them.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SuccessfulReferences_CopyPathsAndReportExposureByFactory(bool exposed)
        {
            List<string> paths = new List<string> { "First.dll", "Second.dll" };

            HotReloadArtifactCompileReferences references = exposed
                ? HotReloadArtifactCompileReferences.Exposed(paths)
                : HotReloadArtifactCompileReferences.Raw(paths);
            paths.Add("Third.dll");

            Assert.That(references.Success, Is.True);
            Assert.That(references.ExposesInternals, Is.EqualTo(exposed));
            Assert.That(references.ErrorMessage, Is.Empty);
            Assert.That(references.Paths, Is.EqualTo(new[] { "First.dll", "Second.dll" }));
            IList<string> writable = (IList<string>)references.Paths;
            Assert.Throws<NotSupportedException>(() => writable.Add("Fourth.dll"));
            Assert.Throws<NotSupportedException>(() => writable[0] = "Replaced.dll");
        }

        /// <summary>
        /// Verifies a successful result cannot be built without paths.
        /// </summary>
        [Test]
        public void SuccessfulReferences_NullPaths_AreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => HotReloadArtifactCompileReferences.Raw(null));
            Assert.Throws<ArgumentNullException>(() => HotReloadArtifactCompileReferences.Exposed(null));
        }

        /// <summary>
        /// Verifies a failed result always carries its reason and never paths or exposure.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void Failed_EmptyReason_IsRejected(string reason)
        {
            Assert.Throws<ArgumentException>(() => HotReloadArtifactCompileReferences.Failed(reason));

            HotReloadArtifactCompileReferences failed = HotReloadArtifactCompileReferences.Failed("Resolution failed.");
            Assert.That(failed.Success, Is.False);
            Assert.That(failed.Paths, Is.Null);
            Assert.That(failed.ExposesInternals, Is.False);
            Assert.That(failed.ErrorMessage, Is.EqualTo("Resolution failed."));
        }

        private HotReloadArtifactCompileReferences Build(
            TransformWorkerInputDto input,
            InternalsExposureTestImage target,
            bool exposeInternals)
        {
            return HotReloadIntroducedTypeArtifactReferenceBuilder.Build(
                input,
                target.Home,
                HotReloadCompositionRoot.Services.Domain,
                _projectRoot,
                exposeInternals);
        }

        // The candidate is internal and holds one private member, so a copy shows at once whether it
        // lifted only the assembly restriction.
        private InternalsExposureTestImage CreateImage(
            Action<TypeDefinition> configure = null,
            IAssemblyResolver writeResolver = null)
        {
            InternalsExposureTestImage image = new InternalsExposureTestImage(
                candidate =>
                {
                    HideCandidate(candidate);
                    configure?.Invoke(candidate);
                },
                writeResolver);
            _images.Add(image);
            return image;
        }

        private InternalsExposureTestImage CreateImageWithConstantOfMissingEnum(string externalName)
        {
            InternalsExposureTestImage image =
                InternalsExposureTestImage.CreateWithConstantOfMissingEnum(externalName, HideCandidate);
            _images.Add(image);
            return image;
        }

        private static void HideCandidate(TypeDefinition candidate)
        {
            candidate.Attributes = TypeAttributes.NotPublic | TypeAttributes.BeforeFieldInit;
            InternalsExposureTestImage.AddReadMethod(candidate, "Secret", MethodAttributes.Private);
        }

        private static TransformWorkerIntroducedTypeArtifactDto ActivateRetainedArtifact(InternalsExposureTestImage image)
        {
            string assemblyName = image.Definition.Name.Name;
            ReflectionAssembly loaded = AssemblyBuilder.DefineDynamicAssembly(
                new ReflectionAssemblyName(assemblyName),
                AssemblyBuilderAccess.Run);
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                loaded,
                image.DllPath,
                Path.ChangeExtension(image.DllPath, ".pdb"),
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    new HotReloadIntroducedTypeDescriptor(
                        "TargetAssembly",
                        "target-mvid",
                        "Retained." + assemblyName,
                        "Assets/" + assemblyName + ".cs",
                        assemblyName,
                        "class Candidate { }")
                });
            HotReloadIntroducedTypeRegistry registry = HotReloadCompositionRoot.Services.Domain.IntroducedTypes;
            registry.RegisterPrepared(artifact);
            registry.Activate(artifact);
            return new TransformWorkerIntroducedTypeArtifactDto
            {
                assemblyFullName = artifact.AssemblyFullName,
                referencePath = artifact.DllPath
            };
        }

        private static TransformWorkerInputDto CreateInput(
            InternalsExposureTestImage target,
            params TransformWorkerIntroducedTypeArtifactDto[] artifacts)
        {
            return new TransformWorkerInputDto
            {
                referencePaths = new[] { CorlibPath, TargetPath(target), CecilPath },
                introducedTypeArtifacts = artifacts
            };
        }

        private static string TargetPath(InternalsExposureTestImage image)
        {
            return Path.GetFullPath(image.DllPath);
        }

        private string ExposedCopyPath(InternalsExposureTestImage image)
        {
            return Path.Combine(
                _projectRoot,
                HotReloadConstants.InternalsExposedRefsRelativeDirectory,
                image.Definition.Name.Name + "-" + image.Definition.MainModule.Mvid.ToString("N") + ".dll");
        }

        private string[] ExposedCopiesOf(InternalsExposureTestImage image)
        {
            string directory = Path.Combine(_projectRoot, HotReloadConstants.InternalsExposedRefsRelativeDirectory);
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, image.Definition.Name.Name + "-*")
                : Array.Empty<string>();
        }

        private static void AssertInternalsOnlyCopy(string copyPath)
        {
            using AssemblyDefinition copy = AssemblyDefinition.ReadAssembly(copyPath);
            TypeDefinition candidate = copy.MainModule.GetType("Candidate");
            Assert.That(candidate.IsPublic, Is.True, "The internal type must be public in the copy.");
            Assert.That(
                candidate.Methods.Single(method => method.Name == "Secret").IsPrivate,
                Is.True,
                "A private member must stay private in the copy.");
        }

        private static string SpellWithAlternateSeparator(string path)
        {
            if (Path.AltDirectorySeparatorChar == Path.DirectorySeparatorChar)
            {
                Assert.Ignore("Only a platform with two directory separators can spell one path both ways.");
            }

            return path.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
