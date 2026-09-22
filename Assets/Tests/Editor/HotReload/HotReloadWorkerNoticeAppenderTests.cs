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
                HotReloadSnapshotMissReason.NoSnapshotFile,
                false,
                false,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings,
                null);

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
                HotReloadSnapshotMissReason.NoSnapshotFile,
                false,
                false,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings,
                null);

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
            HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                CreateFileOutput(Array.Empty<string>()),
                Array.Empty<TransformWorkerSkippedDto>(),
                1,
                HotReloadSnapshotMissReason.NoSnapshotFile,
                true,
                false,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings,
                null);

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(
                warnings[0],
                Is.EqualTo(
                    "Broken.cs declares a type hot reload introduced (assembly Some.Assembly), so "
                    + "it has no compiled baseline until 'uloop compile'. This is expected: hot "
                    + "reload tracks the introduced type from its own recorded declaration. Any "
                    + "other type in this file has no baseline either and is patched in full."));
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
            HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                CreateFileOutput(Array.Empty<string>()),
                Array.Empty<TransformWorkerSkippedDto>(),
                1,
                HotReloadSnapshotMissReason.NoSnapshotFile,
                true,
                false,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings,
                null);

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
                HotReloadSnapshotMissReason.NoSnapshotFile,
                false,
                false,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings,
                null);

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("patching all methods"));
        }

        /// <summary>
        /// What: a file the PDB lists no document for is told no compile gives it a baseline,
        /// instead of being asked to run uloop compile, which would change nothing for it.
        /// </summary>
        [Test]
        public void AppendWorkerNotices_WhenThePdbHasNoDocumentForTheFile_SaysNoCompileGivesItABaseline()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();

            HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                CreateFileOutput(Array.Empty<string>()),
                Array.Empty<TransformWorkerSkippedDto>(),
                1,
                HotReloadSnapshotMissReason.NoDocumentInPdb,
                false,
                false,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings,
                null);

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(
                warnings[0],
                Is.EqualTo(
                    "Broken.cs (assembly Some.Assembly) has no compiled method body, so there is no "
                    + "baseline for edited-method detection; patching all methods. This is expected "
                    + "for files that only declare types without bodies."));
        }

        /// <summary>
        /// What: a file that declares an introduced type keeps the introduced-type explanation
        /// even when the PDB has no document for it, and gets only that one warning.
        /// </summary>
        [Test]
        public void AppendWorkerNotices_WhenAFileWithoutAPdbDocumentDeclaresAnIntroducedType_ExplainsTheIntroducedType()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();

            HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                CreateFileOutput(Array.Empty<string>()),
                Array.Empty<TransformWorkerSkippedDto>(),
                1,
                HotReloadSnapshotMissReason.NoDocumentInPdb,
                true,
                false,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings,
                null);

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.StartWith("Broken.cs declares a type hot reload introduced"));
        }

        /// <summary>
        /// What: a verified snapshot adds no missing-baseline warning even when the file has patch
        /// candidates.
        /// </summary>
        [Test]
        public void AppendWorkerNotices_WithAVerifiedSnapshot_AddsNoMissingBaselineWarning()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();

            HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                CreateFileOutput(Array.Empty<string>()),
                Array.Empty<TransformWorkerSkippedDto>(),
                1,
                HotReloadSnapshotMissReason.None,
                false,
                false,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings,
                null);

            Assert.That(warnings, Is.Empty);
        }

        /// <summary>
        /// What: a re-applied sibling's missing baseline goes to the run's sibling summary instead
        /// of a warning of its own.
        /// </summary>
        [Test]
        public void AppendWorkerNotices_ForAReappliedSibling_RoutesTheMissingBaselineToTheSiblingSummary()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();
            HotReloadSiblingBaselineNotices siblingBaselineNotices = new HotReloadSiblingBaselineNotices();

            HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                CreateFileOutput(Array.Empty<string>()),
                Array.Empty<TransformWorkerSkippedDto>(),
                1,
                HotReloadSnapshotMissReason.NoSnapshotFile,
                false,
                false,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings,
                siblingBaselineNotices);

            Assert.That(warnings, Is.Empty);
            List<string> summary = new List<string>();
            siblingBaselineNotices.AppendTo(summary);
            Assert.That(summary.Count, Is.EqualTo(1));
            Assert.That(
                summary[0],
                Does.StartWith("1 re-applied sibling file(s) have no verified source snapshot"));
            Assert.That(summary[0], Does.Contain(ProjectRelativePath));
        }

        /// <summary>
        /// What: a file declaring a type hot reload refused to introduce gets no missing-baseline
        /// warning, because the refusal notice already says the file needs a compile and "patching
        /// all methods" would promise a patch the refusal rules out.
        /// </summary>
        [Test]
        public void AppendWorkerNotices_WhenFileDeclaresARefusedIntroducedType_AddsNoMissingBaselineWarning()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();
            HotReloadSiblingBaselineNotices siblingBaselineNotices = new HotReloadSiblingBaselineNotices();

            HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                CreateFileOutput(Array.Empty<string>()),
                Array.Empty<TransformWorkerSkippedDto>(),
                1,
                HotReloadSnapshotMissReason.NoSnapshotFile,
                false,
                true,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings,
                null);
            HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                CreateFileOutput(Array.Empty<string>()),
                Array.Empty<TransformWorkerSkippedDto>(),
                1,
                HotReloadSnapshotMissReason.NoSnapshotFile,
                false,
                true,
                ProjectRelativePath,
                AssemblyName,
                AssemblyResolvePath,
                outcomes,
                warnings,
                siblingBaselineNotices);

            Assert.That(warnings, Is.Empty, string.Join(" | ", warnings));
            List<string> summary = new List<string>();
            siblingBaselineNotices.AppendTo(summary);
            Assert.That(summary, Is.Empty, "A re-applied sibling that declares one is left out of the summary too.");
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
