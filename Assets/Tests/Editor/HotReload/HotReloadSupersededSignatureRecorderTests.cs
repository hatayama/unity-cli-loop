using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for recording superseded compiled signatures from the entries a file
    /// actually applied, which a group run must not confuse with the whole run's output.
    /// </summary>
    public class HotReloadSupersededSignatureRecorderTests
    {
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/SupersededFixture.cs";
        private const string TypeMetadataName = "Sample.Host";
        private const string RemovedMethodLabel = "Sample.Host.Scaled(System.Int32)";
        private const string DecoyMethodLabel = "Sample.Host.Other(System.Int32)";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();

            // The recorder writes into the file's generation, so the generation must exist first.
            new HotReloadDomainTestAccess().GetOrBeginAddedMemberGeneration(FixtureProjectRelativePath);
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
        }

        /// <summary>
        /// What: a removed signature whose replacement is among the applied entries is recorded
        /// with that replacement's display name, chosen by signature rather than by taking the
        /// first replacement entry of the list.
        /// </summary>
        [Test]
        public void RecordFromAppliedEntries_WhenReplacementWasApplied_RecordsTheSupersededSignature()
        {
            HotReloadSupersededSignatureRecorder.RecordFromAppliedEntries(
                FixtureProjectRelativePath,
                new[] { CreateDecoyReplacementEntry(), CreateReplacementEntry() },
                new[] { CreateRemovedSignature() },
                Array.Empty<string>());

            bool found = HotReloadCompositionRoot.Services.Domain.TryGetSupersededReplacement(
                RemovedMethodLabel,
                out string replacementDisplayName);

            Assert.That(found, Is.True);
            Assert.That(replacementDisplayName, Is.EqualTo(RemovedMethodLabel));
            Assert.That(replacementDisplayName, Is.Not.EqualTo(DecoyMethodLabel));
        }

        /// <summary>
        /// What: a removed signature is not recorded when its replacement never reached the apply
        /// stage, which is what happens to a file that isolation dropped while a sibling applied.
        /// </summary>
        [Test]
        public void RecordFromAppliedEntries_WhenReplacementWasNotApplied_RecordsNothing()
        {
            HotReloadSupersededSignatureRecorder.RecordFromAppliedEntries(
                FixtureProjectRelativePath,
                new List<TransformWorkerEntryDto>(),
                new[] { CreateRemovedSignature() },
                Array.Empty<string>());

            bool found = HotReloadCompositionRoot.Services.Domain.TryGetSupersededReplacement(
                RemovedMethodLabel,
                out string _);

            Assert.That(found, Is.False);
        }

        /// <summary>
        /// What: a replacement the signature-change gate refused is not recorded even when the
        /// entry list still carries it.
        /// </summary>
        [Test]
        public void RecordFromAppliedEntries_WhenReplacementWasGated_RecordsNothing()
        {
            TransformWorkerEntryDto entry = CreateReplacementEntry();

            HotReloadSupersededSignatureRecorder.RecordFromAppliedEntries(
                FixtureProjectRelativePath,
                new[] { entry },
                new[] { CreateRemovedSignature() },
                new[] { HotReloadMethodKeys.BuildMethodKey(entry) });

            bool found = HotReloadCompositionRoot.Services.Domain.TryGetSupersededReplacement(
                RemovedMethodLabel,
                out string _);

            Assert.That(found, Is.False);
        }

        private static TransformWorkerEntryDto CreateReplacementEntry()
        {
            return new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = "Assets/Scripts/Host.cs",
                typeMetadataName = TypeMetadataName,
                methodName = "Scaled",
                parameterTypeFullNames = new[] { "System.Int32" },
                genericArity = 0,
                replacesCompiledMethod = true
            };
        }

        // A replacement of another compiled signature, so a recorder that picked the first
        // replacement entry instead of the matching one would report this label.
        private static TransformWorkerEntryDto CreateDecoyReplacementEntry()
        {
            return new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = "Assets/Scripts/Host.cs",
                typeMetadataName = TypeMetadataName,
                methodName = "Other",
                parameterTypeFullNames = new[] { "System.Int32" },
                genericArity = 0,
                replacesCompiledMethod = true
            };
        }

        private static TransformWorkerRemovedMethodSignatureDto CreateRemovedSignature()
        {
            return new TransformWorkerRemovedMethodSignatureDto
            {
                typeMetadataName = TypeMetadataName,
                methodName = "Scaled",
                parameterTypeFullNames = new[] { "System.Int32" },
                genericArity = 0
            };
        }
    }
}
