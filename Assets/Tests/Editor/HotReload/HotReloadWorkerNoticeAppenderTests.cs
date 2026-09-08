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
