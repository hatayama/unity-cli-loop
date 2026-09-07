using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies that the retained-artifact records and the target assembly identity of the first
    /// worker run reach the retry run as well. The isolation retry and the signature-change gate
    /// share one retry entry point, so a retry that lost them would bind the retained types back
    /// to their source and emit types a loaded assembly already holds.
    /// </summary>
    public sealed class HotReloadRetainedArtifactPropagationTests
    {
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
        /// What: a retry inherits the artifact records, so an artifact the worker cannot trust
        /// fails the retry run instead of letting isolation continue against a guessed binding.
        /// </summary>
        [Test]
        public async Task IsolationRetry_UntrustedArtifactRecord_FailsRetry()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("RetryUntrustedArtifact", EditedSource);
            TransformWorkerIntroducedTypeArtifactDto mismatched =
                fixture.CreateRecordedArtifact(fixture.RetainedFingerprint);
            mismatched.assemblyFullName =
                HotReloadRetainedArtifactFixture.ReadAssemblyFullName(fixture.TargetAssemblyPath);

            HotReloadShimIsolation.IsolationRetryRunResult retry = await RunRetryAsync(
                fixture.BuildTransformInput(new[] { mismatched }),
                fixture,
                BuildExclusions(
                    new[]
                    {
                        BuildMethodKey("Example.Caller", "Read"),
                        BuildMethodKey("Example.Retained", "Twice")
                    }));

            Assert.That(retry.Isolation, Is.Null);
            Assert.That(retry.FailureMessage, Does.Contain("Retry worker failed"));
        }

        /// <summary>
        /// What: a retry inherits the artifact records and the target assembly identity, so the
        /// retained declaration is still recognized and its members produce no retry rows. Without
        /// the identity the recomputed fingerprint could not match the record.
        /// </summary>
        [Test]
        public async Task IsolationRetry_RetainedRecord_ProducesNoRowsForRetainedType()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("RetryRetainedRecord", EditedSource);
            TransformWorkerInputDto workerInput = fixture.BuildTransformInput(
                new[] { fixture.CreateRecordedArtifact(fixture.RetainedFingerprint) });

            HotReloadShimIsolation.IsolationRetryRunResult retry = await RunRetryAsync(
                workerInput,
                fixture,
                BuildExclusions(new[] { BuildMethodKey("Example.Caller", "Read") }));

            Assert.That(retry.Isolation, Is.Not.Null, retry.FailureMessage);
            Assert.That(retry.Isolation.RetryEntries, Is.Empty);
            AssertNoOutcomeMentions(retry.Isolation.SkippedCallerOutcomes, "Twice");
        }

        /// <summary>
        /// What: a retry always runs the transform operation, even when the first run was a
        /// preparation run, so its per-file rows carry transformed methods and not descriptors.
        /// </summary>
        [Test]
        public async Task IsolationRetry_PreparationInput_StillRetriesAsTransform()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("RetryOperation", EditedSource);
            TransformWorkerInputDto workerInput = fixture.BuildPrepareInput();

            HotReloadShimIsolation.IsolationRetryRunResult retry = await RunRetryAsync(
                workerInput,
                fixture,
                BuildExclusions(
                    new[]
                    {
                        BuildMethodKey("Example.Caller", "Read"),
                        BuildMethodKey("Example.Retained", "Twice")
                    }));

            Assert.That(retry.Isolation, Is.Not.Null, retry.FailureMessage);
            Assert.That(retry.Isolation.RetryFiles.Length, Is.EqualTo(1));
            Assert.That(retry.Isolation.RetryFiles[0].introducedTypes, Is.Null.Or.Empty);
        }

        /// <summary>
        /// What: an artifact whose file the run already references is not added a second time, and
        /// two records naming the same file collapse to one entry, so every record keeps a single
        /// reference and the compilation never holds one assembly identity twice.
        /// </summary>
        [Test]
        public async Task ArtifactReferences_PathAlreadyReferenced_IsAddedOnce()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("ArtifactReferenceReuse", EditedSource);
            string artifactDirectory = Path.GetDirectoryName(fixture.ArtifactPath);
            List<string> references = new List<string> { fixture.ArtifactPath };

            HotReloadShimReferenceBuilder.AppendIntroducedTypeArtifactReferences(
                references,
                new[]
                {
                    CreateArtifactReference(Path.Combine(artifactDirectory, ".", "RetainedArtifact.dll")),
                    CreateArtifactReference(fixture.ArtifactPath),
                    CreateArtifactReference(fixture.TargetAssemblyPath)
                });

            Assert.That(references.Count, Is.EqualTo(2));
            Assert.That(references[0], Is.EqualTo(fixture.ArtifactPath));
            Assert.That(references[1], Is.EqualTo(Path.GetFullPath(fixture.TargetAssemblyPath)));
        }

        /// <summary>
        /// What: a record without a reference path contributes nothing, so a malformed record
        /// cannot put an empty entry into a compilation's reference list.
        /// </summary>
        [Test]
        public void ArtifactReferences_RecordWithoutPath_ContributesNothing()
        {
            List<string> collected = HotReloadShimReferenceBuilder.CollectIntroducedTypeArtifactReferencePaths(
                new[] { CreateArtifactReference(string.Empty), null });

            Assert.That(collected, Is.Empty);
        }

        private static TransformWorkerIntroducedTypeArtifactDto CreateArtifactReference(string referencePath)
        {
            return new TransformWorkerIntroducedTypeArtifactDto
            {
                assemblyFullName = "Artifact",
                referencePath = referencePath,
                types = Array.Empty<TransformWorkerIntroducedTypeArtifactTypeDto>()
            };
        }

        private static async Task<HotReloadShimIsolation.IsolationRetryRunResult> RunRetryAsync(
            TransformWorkerInputDto workerInput,
            HotReloadRetainedArtifactFixture fixture,
            HotReloadShimIsolation.IsolationExclusions exclusions)
        {
            return await HotReloadShimIsolation.RunIsolationRetryAsync(
                workerInput,
                exclusions,
                new List<HotReloadMethodOutcome>(),
                new List<HotReloadMethodOutcome>(),
                HotReloadRetainedArtifactFixture.FindCompilationAssembly(),
                fixture.TargetAssemblyPath,
                workerInput.defines,
                Array.Empty<TransformWorkerSkippedDto>(),
                HotReloadGroupFilePaths.ForSingleFile(
                    fixture.ProjectRelativePath,
                    fixture.TargetAssemblyPath),
                HotReloadConstants.VibeLogIsolationTriggerShimCompileFailure,
                "retained-artifact-propagation",
                CancellationToken.None);
        }

        private static HotReloadShimIsolation.IsolationExclusions BuildExclusions(string[] excludedMethodKeys)
        {
            return new HotReloadShimIsolation.IsolationExclusions(
                excludedMethodKeys,
                Array.Empty<string>(),
                Array.Empty<TransformWorkerEntryDto>());
        }

        private static string BuildMethodKey(string typeMetadataName, string methodName)
        {
            return HotReloadMethodKeys.BuildMethodKeyParts(
                typeMetadataName,
                methodName,
                Array.Empty<string>(),
                0);
        }

        private static void AssertNoOutcomeMentions(
            IReadOnlyList<HotReloadMethodOutcome> outcomes,
            string memberName)
        {
            foreach (HotReloadMethodOutcome outcome in outcomes)
            {
                Assert.That(outcome.Method ?? string.Empty, Does.Not.Contain(memberName));
            }
        }
    }
}
