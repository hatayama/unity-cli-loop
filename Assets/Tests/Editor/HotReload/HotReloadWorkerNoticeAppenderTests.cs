using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for how worker-side per-file notices become outcomes and warnings.
    /// </summary>
    public class HotReloadWorkerNoticeAppenderTests
    {
        private const string ProjectRelativePath = "Assets/Scripts/Broken.cs";
        private const string AssemblyName = "Some.Assembly";
        private const string AssemblyResolvePath = "Library/ScriptAssemblies/Some.Assembly.dll";
        private const string ParseErrorText = "Broken.cs(3,1): error CS1022: Type or namespace definition, or end-of-file expected";

        /// <summary>
        /// A file output carrying parse errors adds exactly one Failed outcome named
        /// "(file)" and does not put the parse error text into the warnings.
        /// </summary>
        [Test]
        public void AppendWorkerNotices_WithParseErrors_AddsFileFailedOutcomeAndNoWarning()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();

            HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                CreateFileOutput(new[] { ParseErrorText }),
                Array.Empty<TransformWorkerSkippedDto>(),
                0,
                null,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings);

            Assert.That(outcomes.Count, Is.EqualTo(1));
            Assert.That(outcomes[0].Method, Is.EqualTo("(file)"));
            Assert.That(outcomes[0].Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Failed));
            Assert.That(outcomes[0].Reason, Does.Contain("CS1022"));
            Assert.That(warnings, Is.Empty);
        }

        /// <summary>
        /// A file output without parse errors adds no Failed outcome at all.
        /// </summary>
        [Test]
        public void AppendWorkerNotices_WithoutParseErrors_AddsNoFailedOutcome()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();

            HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                CreateFileOutput(Array.Empty<string>()),
                Array.Empty<TransformWorkerSkippedDto>(),
                0,
                null,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings);

            Assert.That(outcomes, Is.Empty);
            Assert.That(warnings, Is.Empty);
        }

        /// <summary>
        /// What: a file that declares a type hot reload already introduced is told its missing
        /// baseline is expected, instead of being warned that all methods are being patched. Such
        /// a file has no compiled baseline until 'uloop compile', so the generic warning read as
        /// a fault on every reload of it.
        /// </summary>
        [Test]
        public void AppendWorkerNotices_WhenFileDeclaresAnIntroducedType_ExplainsTheMissingBaseline()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();
            TransformWorkerFileOutputDto fileOutput = CreateFileOutput(Array.Empty<string>());
            fileOutput.introducedTypeReuses = new[]
            {
                new TransformWorkerIntroducedTypeReuseDto { metadataName = "Example.Retained" }
            };

            HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                fileOutput,
                Array.Empty<TransformWorkerSkippedDto>(),
                1,
                null,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings);

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(
                warnings[0],
                Is.EqualTo(
                    "Broken.cs declares a type hot reload introduced (assembly Some.Assembly), so "
                    + "it has no compiled baseline until 'uloop compile'; edited members are "
                    + "detected from the retained declaration instead. This is expected."));
        }

        /// <summary>
        /// What: a file that introduces a type in this very run gets the same explanation, because
        /// the reason it has no baseline is identical to a file whose type is already introduced.
        /// </summary>
        [Test]
        public void AppendWorkerNotices_WhenFileIntroducesATypeInThisRun_ExplainsTheMissingBaseline()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();
            TransformWorkerFileOutputDto fileOutput = CreateFileOutput(Array.Empty<string>());
            fileOutput.introducedTypes = new[]
            {
                new TransformWorkerIntroducedTypeDto { metadataName = "Example.Fresh" }
            };

            HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                fileOutput,
                Array.Empty<TransformWorkerSkippedDto>(),
                1,
                null,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings);

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("declares a type hot reload introduced"));
            Assert.That(warnings[0], Does.Not.Contain("patching all methods"));
        }

        /// <summary>
        /// What: a file with no introduced type of its own keeps the original missing-baseline
        /// warning, because there a baseline really is what edited-method detection is missing.
        /// </summary>
        [Test]
        public void AppendWorkerNotices_WhenFileHasNoIntroducedType_KeepsTheNoSnapshotWarning()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();

            HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                CreateFileOutput(Array.Empty<string>()),
                Array.Empty<TransformWorkerSkippedDto>(),
                1,
                null,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings);

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("patching all methods"));
        }

        private static TransformWorkerFileOutputDto CreateFileOutput(string[] parseErrors)
        {
            return new TransformWorkerFileOutputDto
            {
                projectRelativePath = ProjectRelativePath,
                sourceContentSha256 = "aaaa",
                parseErrors = parseErrors,
                declarationDriftWarnings = Array.Empty<string>(),
                removedMembers = Array.Empty<TransformWorkerRemovedMemberDto>(),
                removedMethodSignatures = Array.Empty<TransformWorkerRemovedMethodSignatureDto>()
            };
        }
    }
}
