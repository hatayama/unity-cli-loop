using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the "every requested method was Skipped" conclusion, siblings excluded.
    /// </summary>
    public sealed class HotReloadRequestedFileOutcomeSummaryTests
    {
        private const string RequestedPath = "Assets/Requested.cs";
        private const string OtherRequestedPath = "Assets/OtherRequested.cs";
        private const string SiblingPath = "Assets/Sibling.cs";

        /// <summary>
        /// What: two requested files that were both Skipped report true even though a sibling
        /// file was re-applied with an Added member.
        /// </summary>
        [Test]
        public void AreAllRequestedOutcomesSkipped_RequestedFilesSkippedAndSiblingAdded_ReturnsTrue()
        {
            bool allSkipped = HotReloadRequestedFileOutcomeSummary.AreAllRequestedOutcomesSkipped(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Skipped("Requested.M", "reason", RequestedPath),
                    HotReloadMethodOutcome.Skipped("Other.M", "reason", OtherRequestedPath),
                    HotReloadMethodOutcome.Added("Sibling.N", SiblingPath)
                },
                new[] { SiblingPath },
                path => path);

            Assert.That(allSkipped, Is.True);
        }

        /// <summary>
        /// What: a requested file with a Patched method reports false.
        /// </summary>
        [Test]
        public void AreAllRequestedOutcomesSkipped_RequestedFileHasAPatchedMethod_ReturnsFalse()
        {
            bool allSkipped = HotReloadRequestedFileOutcomeSummary.AreAllRequestedOutcomesSkipped(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Skipped("Requested.M", "reason", RequestedPath),
                    HotReloadMethodOutcome.Patched("Requested.N", RequestedPath)
                },
                new string[0],
                path => path);

            Assert.That(allSkipped, Is.False);
        }

        /// <summary>
        /// What: a run whose only outcomes belong to re-applied siblings reports false, because
        /// there is no requested-file outcome to conclude anything from.
        /// </summary>
        [Test]
        public void AreAllRequestedOutcomesSkipped_OnlySiblingOutcomes_ReturnsFalse()
        {
            bool allSkipped = HotReloadRequestedFileOutcomeSummary.AreAllRequestedOutcomesSkipped(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Added("Sibling.N", SiblingPath)
                },
                new[] { SiblingPath },
                path => path);

            Assert.That(allSkipped, Is.False);
        }

        /// <summary>
        /// What: a sibling listed by its absolute path is still excluded when the outcome spells
        /// the same file project-relative, so the requested file alone decides the answer.
        /// </summary>
        [Test]
        public void AreAllRequestedOutcomesSkipped_SiblingSpelledAbsolute_StillExcludesIt()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absoluteSiblingPath = Path.Combine(
                projectRoot,
                SiblingPath.Replace('/', Path.DirectorySeparatorChar));

            bool allSkipped = HotReloadRequestedFileOutcomeSummary.AreAllRequestedOutcomesSkipped(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Skipped("Requested.M", "reason", RequestedPath),
                    HotReloadMethodOutcome.Added("Sibling.N", SiblingPath)
                },
                new[] { absoluteSiblingPath },
                ToProjectRelativeScriptPath);

            Assert.That(allSkipped, Is.True);
        }

        /// <summary>
        /// What: an outcome with no file is ignored, so a requested file whose methods were all
        /// Skipped still reports true and the normalizer is never asked about an empty path.
        /// </summary>
        [Test]
        public void AreAllRequestedOutcomesSkipped_OutcomeWithoutAFile_IsIgnored()
        {
            bool allSkipped = HotReloadRequestedFileOutcomeSummary.AreAllRequestedOutcomesSkipped(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Skipped("Requested.M", "reason", RequestedPath),
                    HotReloadMethodOutcome.Patched("(file)", string.Empty)
                },
                new string[0],
                path =>
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        Assert.Fail("The normalizer must not be asked about an outcome with no file.");
                    }

                    return path;
                });

            Assert.That(allSkipped, Is.True);
        }

        /// <summary>
        /// What: a run whose only Skipped outcome belongs to no file reports false, because no
        /// requested file was seen at all.
        /// </summary>
        [Test]
        public void AreAllRequestedOutcomesSkipped_OnlyOutcomesWithoutAFile_ReturnsFalse()
        {
            bool allSkipped = HotReloadRequestedFileOutcomeSummary.AreAllRequestedOutcomesSkipped(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Skipped("(file)", "reason", string.Empty)
                },
                new string[0],
                path => path);

            Assert.That(allSkipped, Is.False);
        }

        /// <summary>
        /// What: a sibling listed project-relative is still excluded when the outcome spells the
        /// same file absolute, which is the direction the apply response actually produces.
        /// </summary>
        [Test]
        public void AreAllRequestedOutcomesSkipped_OutcomeSpelledAbsolute_StillExcludesTheSibling()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absoluteSiblingPath = Path.Combine(
                projectRoot,
                SiblingPath.Replace('/', Path.DirectorySeparatorChar));

            bool allSkipped = HotReloadRequestedFileOutcomeSummary.AreAllRequestedOutcomesSkipped(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Skipped("Requested.M", "reason", RequestedPath),
                    HotReloadMethodOutcome.Added("Sibling.N", absoluteSiblingPath)
                },
                new[] { SiblingPath },
                ToProjectRelativeScriptPath);

            Assert.That(allSkipped, Is.True);
        }

        private static string ToProjectRelativeScriptPath(string path)
        {
            // The capture is normally filled by the run entry point; this test calls the
            // normalizer directly, so it has to fill it itself.
            HotReloadCompositionRoot.Services.PackageRootCapture.CaptureCurrent();
            return HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(
                HotReloadCompositionRoot.Services.PackageRootCapture,
                path);
        }
    }
}
