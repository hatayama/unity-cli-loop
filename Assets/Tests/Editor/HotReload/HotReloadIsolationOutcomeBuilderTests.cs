using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers how an isolation retry drops the skip rows the first pass already reported.
    /// </summary>
    public class HotReloadIsolationOutcomeBuilderTests
    {
        private const string HostPath = "Assets/Scripts/Host.cs";

        /// <summary>
        /// What: a retry row with the same method, code, and values as a first-pass row is not
        /// reported again.
        /// </summary>
        [Test]
        public void CollectRetryOnlySkippedOutcomes_SameMethodAndReason_DropsTheRetryRow()
        {
            TransformWorkerSkippedDto[] firstPass = { SkippedRow("Host.Caller()", "Host.Callee()") };
            TransformWorkerSkippedDto[] retry = { SkippedRow("Host.Caller()", "Host.Callee()") };

            List<HotReloadMethodOutcome> outcomes = Collect(firstPass, retry);

            Assert.That(outcomes, Is.Empty);
        }

        /// <summary>
        /// What: a retry row for the same method with a different value is still reported, so a
        /// method skipped for a new reason on retry surfaces.
        /// </summary>
        [Test]
        public void CollectRetryOnlySkippedOutcomes_SameMethodWithDifferentValue_KeepsTheRetryRow()
        {
            TransformWorkerSkippedDto[] firstPass = { SkippedRow("Host.Caller()", "Host.Callee()") };
            TransformWorkerSkippedDto[] retry = { SkippedRow("Host.Caller()", "Host.Other()") };

            List<HotReloadMethodOutcome> outcomes = Collect(firstPass, retry);

            Assert.That(outcomes.Count, Is.EqualTo(1));
            Assert.That(outcomes[0].Method, Is.EqualTo("Host.Caller()"));
        }

        /// <summary>
        /// What: a null value and an empty value count as the same reason, because the sentence
        /// renders both as nothing.
        /// </summary>
        [Test]
        public void CollectRetryOnlySkippedOutcomes_NullAndEmptyValue_DropsTheRetryRow()
        {
            TransformWorkerSkippedDto[] firstPass = { SkippedRow("Host.Caller()", null) };
            TransformWorkerSkippedDto[] retry = { SkippedRow("Host.Caller()", string.Empty) };

            List<HotReloadMethodOutcome> outcomes = Collect(firstPass, retry);

            Assert.That(outcomes, Is.Empty);
        }

        private static List<HotReloadMethodOutcome> Collect(
            TransformWorkerSkippedDto[] firstPass,
            TransformWorkerSkippedDto[] retry)
        {
            // The signature-change gate does not rewrite retry-only reasons, so the rows reach the
            // outcomes as the worker reported them.
            return new HotReloadIsolationOutcomeBuilder().CollectRetryOnlySkippedOutcomes(
                firstPass,
                retry,
                HotReloadGroupFilePaths.ForSingleFile(HostPath, "test.dll"),
                new HotReloadSignatureChangeGateIsolationTrigger(),
                new string[0]);
        }

        private static TransformWorkerSkippedDto SkippedRow(string method, string calledMethod)
        {
            return new TransformWorkerSkippedDto
            {
                sourceProjectRelativePath = HostPath,
                method = method,
                methodKey = method,
                reason = new TransformWorkerReasonDto
                {
                    code = HotReloadWorkerReasonCode.AddedMethodUnavailableAddedCall,
                    args = new[] { calledMethod }
                }
            };
        }
    }
}
