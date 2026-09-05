using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for mapping shim compile diagnostics onto worker entries when one shim
    /// assembly hosts entries from several edited files.
    /// </summary>
    public class HotReloadShimErrorAttributionTests
    {
        /// <summary>
        /// What: two entries from different files share an original-source line range, and the
        /// error is attributed to the entry whose file matches error.File instead of being
        /// rejected as ambiguous.
        /// </summary>
        [Test]
        public void AttributeErrorsToEntries_OverlappingRangesInDifferentFiles_AttributesToMatchingFile()
        {
            TransformWorkerEntryDto firstFileEntry = new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = "Assets/Scripts/First.cs",
                sourceStartLine = 10,
                sourceEndLine = 20
            };
            TransformWorkerEntryDto secondFileEntry = new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = "Assets/Scripts/Second.cs",
                sourceStartLine = 10,
                sourceEndLine = 20
            };
            HotReloadShimCompileError error =
                new HotReloadShimCompileError("Assets/Scripts/Second.cs", 15, "CS0103: name not found");

            HotReloadShimErrorAttribution.ShimCompileErrorAttribution attribution =
                HotReloadShimErrorAttribution.AttributeErrorsToEntries(
                    new[] { firstFileEntry, secondFileEntry },
                    new List<HotReloadShimCompileError> { error });

            Assert.That(attribution, Is.Not.Null);
            Assert.That(attribution.FailedEntries, Is.EqualTo(new[] { secondFileEntry }));
            Assert.That(attribution.ErrorMessagesByEntry.ContainsKey(firstFileEntry), Is.False);
        }

        /// <summary>
        /// What: an error whose file matches no entry file is unattributable, so the whole
        /// attribution is rejected rather than pinned on an entry with a containing line range.
        /// </summary>
        [Test]
        public void AttributeErrorsToEntries_ErrorFileMatchesNoEntryFile_ReturnsNull()
        {
            TransformWorkerEntryDto entry = new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = "Assets/Scripts/First.cs",
                sourceStartLine = 10,
                sourceEndLine = 20
            };
            HotReloadShimCompileError error =
                new HotReloadShimCompileError("Assets/Scripts/Third.cs", 15, "CS0103: name not found");

            HotReloadShimErrorAttribution.ShimCompileErrorAttribution attribution =
                HotReloadShimErrorAttribution.AttributeErrorsToEntries(
                    new[] { entry },
                    new List<HotReloadShimCompileError> { error });

            Assert.That(attribution, Is.Null);
        }

        /// <summary>
        /// What: two entries of the same file whose source ranges both contain the error line
        /// make the error ambiguous, so the whole attribution is rejected instead of pinning it
        /// on the first match.
        /// </summary>
        [Test]
        public void AttributeErrorsToEntries_TwoEntriesOfSameFileContainErrorLine_ReturnsNull()
        {
            TransformWorkerEntryDto outerEntry = new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = "Assets/Scripts/First.cs",
                sourceStartLine = 10,
                sourceEndLine = 30
            };
            TransformWorkerEntryDto innerEntry = new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = "Assets/Scripts/First.cs",
                sourceStartLine = 14,
                sourceEndLine = 20
            };
            HotReloadShimCompileError error =
                new HotReloadShimCompileError("Assets/Scripts/First.cs", 15, "CS0103: name not found");

            HotReloadShimErrorAttribution.ShimCompileErrorAttribution attribution =
                HotReloadShimErrorAttribution.AttributeErrorsToEntries(
                    new[] { outerEntry, innerEntry },
                    new List<HotReloadShimCompileError> { error });

            Assert.That(attribution, Is.Null);
        }
    }
}
