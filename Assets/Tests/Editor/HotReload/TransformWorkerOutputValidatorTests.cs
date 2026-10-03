using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the refusal paths of <see cref="TransformWorkerOutputValidator"/>.
    /// </summary>
    public sealed class TransformWorkerOutputValidatorTests
    {
        private const string TargetAssemblyName = "Fixture.Target";
        private const string TargetAssemblyMvid = "0123456789abcdef0123456789abcdef";
        private const string OwnerPath = "Assets/Fixture/Owner.cs";
        private const string RetainedTypeName = "Fixture.RetainedType";

        /// <summary>
        /// Verifies that a retained artifact that lists no types is refused with the incomplete-record message.
        /// </summary>
        [Test]
        public void TryValidateIntroducedTypeArtifacts_ArtifactWithoutTypes_ReturnsFalseWithIncompleteRecordMessage()
        {
            TransformWorkerOutputValidator validator = new TransformWorkerOutputValidator();
            TransformWorkerInputDto input = CreateInput();
            input.introducedTypeArtifacts = new[]
            {
                new TransformWorkerIntroducedTypeArtifactDto
                {
                    assemblyFullName = "Fixture.Artifact, Version=0.0.0.0",
                    referencePath = "<PROJECT_ROOT>/Fixture.Artifact.dll",
                    types = Array.Empty<TransformWorkerIntroducedTypeArtifactTypeDto>()
                }
            };

            bool valid = validator.TryValidateIntroducedTypeArtifacts(input, out string errorMessage);

            Assert.That(valid, Is.False);
            Assert.That(
                errorMessage,
                Is.EqualTo("A retained artifact must name its assembly, its reference path and at least one type."));
        }

        /// <summary>
        /// Verifies that output without any skipped list passes the skipped-row check with an empty message.
        /// </summary>
        [Test]
        public void TryValidateSkippedRows_SkippedListOmitted_ReturnsTrueWithEmptyMessage()
        {
            TransformWorkerOutputValidator validator = new TransformWorkerOutputValidator();
            TransformWorkerOutputDto output = new TransformWorkerOutputDto { skipped = null };

            bool valid = validator.TryValidateSkippedRows(output, out string errorMessage);

            Assert.That(valid, Is.True);
            Assert.That(errorMessage, Is.Empty);
        }

        /// <summary>
        /// Verifies that a null patched row is refused by the home-assembly check.
        /// </summary>
        [Test]
        public void TryValidateHomeAssemblyNames_NullPatchedRow_ReturnsFalseWithNullRowMessage()
        {
            TransformWorkerOutputValidator validator = new TransformWorkerOutputValidator();
            TransformWorkerOutputDto output = new TransformWorkerOutputDto
            {
                entries = new TransformWorkerEntryDto[] { null }
            };

            bool valid = validator.TryValidateHomeAssemblyNames(output, out string errorMessage);

            Assert.That(valid, Is.False);
            Assert.That(errorMessage, Is.EqualTo("Transform worker output must not contain a null patched row."));
        }

        /// <summary>
        /// Verifies that a null unchanged-method row is refused by the home-assembly check.
        /// </summary>
        [Test]
        public void TryValidateHomeAssemblyNames_NullUnchangedMethodRow_ReturnsFalseWithNullRowMessage()
        {
            TransformWorkerOutputValidator validator = new TransformWorkerOutputValidator();
            TransformWorkerOutputDto output = new TransformWorkerOutputDto
            {
                entries = Array.Empty<TransformWorkerEntryDto>(),
                unchangedMethods = new TransformWorkerUnchangedMethodDto[] { null }
            };

            bool valid = validator.TryValidateHomeAssemblyNames(output, out string errorMessage);

            Assert.That(valid, Is.False);
            Assert.That(
                errorMessage,
                Is.EqualTo("Transform worker output must not contain a null unchanged-method row."));
        }

        /// <summary>
        /// Verifies that a preparation run whose output carries no file list is refused.
        /// </summary>
        [Test]
        public void TryValidateRequiredPreparationOutput_FilesOmitted_ReturnsFalseWithFilesMessage()
        {
            TransformWorkerOutputValidator validator = new TransformWorkerOutputValidator();
            TransformWorkerOutputDto output = new TransformWorkerOutputDto { files = null };

            bool valid = validator.TryValidateRequiredPreparationOutput(CreateInput(), output, out string errorMessage);

            Assert.That(valid, Is.False);
            Assert.That(errorMessage, Is.EqualTo("Preparation output must contain files."));
        }

        /// <summary>
        /// Verifies that a reuse of a retained type is accepted when the artifact list also holds a null artifact, an artifact without types and a null type entry.
        /// </summary>
        [Test]
        public void TryValidateRequiredPreparationOutput_ArtifactsWithNullParts_StillAcceptsReuseOfRetainedType()
        {
            TransformWorkerOutputValidator validator = new TransformWorkerOutputValidator();
            TransformWorkerInputDto input = CreateInput();
            input.introducedTypeArtifacts = new[]
            {
                null,
                new TransformWorkerIntroducedTypeArtifactDto { types = null },
                new TransformWorkerIntroducedTypeArtifactDto
                {
                    types = new[]
                    {
                        null,
                        new TransformWorkerIntroducedTypeArtifactTypeDto
                        {
                            metadataName = RetainedTypeName,
                            originalAssemblyName = TargetAssemblyName,
                            originalAssemblyMvid = TargetAssemblyMvid
                        }
                    }
                }
            };
            TransformWorkerFileOutputDto file = CreateFile();
            file.introducedTypeReuses = new[]
            {
                new TransformWorkerIntroducedTypeReuseDto
                {
                    metadataName = RetainedTypeName,
                    originalAssemblyName = TargetAssemblyName,
                    originalAssemblyMvid = TargetAssemblyMvid
                }
            };
            TransformWorkerOutputDto output = new TransformWorkerOutputDto { files = new[] { file } };

            bool valid = validator.TryValidateRequiredPreparationOutput(input, output, out string errorMessage);

            Assert.That(valid, Is.True);
            Assert.That(errorMessage, Is.Empty);
        }

        /// <summary>
        /// Verifies that a descriptor whose source is blank is refused with the required-fields message.
        /// </summary>
        [Test]
        public void TryValidateRequiredPreparationOutput_DescriptorWithBlankSource_ReturnsFalseWithRequiredFieldsMessage()
        {
            TransformWorkerOutputValidator validator = new TransformWorkerOutputValidator();
            TransformWorkerFileOutputDto file = CreateFile();
            file.introducedTypes = new[]
            {
                new TransformWorkerIntroducedTypeDto
                {
                    originalAssemblyName = TargetAssemblyName,
                    originalAssemblyMvid = TargetAssemblyMvid,
                    metadataName = "Fixture.NewType",
                    ownerProjectRelativePath = OwnerPath,
                    declarationFingerprint = "fingerprint",
                    source = " "
                }
            };
            TransformWorkerOutputDto output = new TransformWorkerOutputDto { files = new[] { file } };

            bool valid = validator.TryValidateRequiredPreparationOutput(CreateInput(), output, out string errorMessage);

            Assert.That(valid, Is.False);
            Assert.That(
                errorMessage,
                Is.EqualTo("Preparation descriptor metadataName, declarationFingerprint, and source are required."));
        }

        private static TransformWorkerInputDto CreateInput()
        {
            return new TransformWorkerInputDto
            {
                operation = "prepareIntroducedTypes",
                targetAssemblyName = TargetAssemblyName,
                targetAssemblyMvid = TargetAssemblyMvid
            };
        }

        private static TransformWorkerFileOutputDto CreateFile()
        {
            return new TransformWorkerFileOutputDto
            {
                projectRelativePath = OwnerPath,
                introducedTypes = Array.Empty<TransformWorkerIntroducedTypeDto>(),
                introducedTypeDiagnostics = Array.Empty<TransformWorkerReasonDto>(),
                introducedTypeReuses = Array.Empty<TransformWorkerIntroducedTypeReuseDto>()
            };
        }
    }
}
