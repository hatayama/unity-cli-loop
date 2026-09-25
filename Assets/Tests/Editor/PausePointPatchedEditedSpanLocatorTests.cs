using System;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how the patched edited span locator finds the hot-reload patched method that holds
    /// an edited-file line, against the real port lookup instead of a substituted delegate.
    /// </summary>
    [TestFixture]
    public sealed class PausePointPatchedEditedSpanLocatorTests
    {
        private const string ForwardSlashFile = "Assets/Scripts/Example.cs";

        // The same file as ForwardSlashFile, spelled the way a Windows caller may pass it.
        private const string BackslashFile = "Assets\\Scripts\\Example.cs";

        private const string UnchangedFile = "Assets/Scripts/Unchanged.cs";

        private const int SpanStartLine = 20;

        private const int SpanEndLine = 30;

        /// <summary>
        /// What: a line inside a patched method's edited span, including both ends, returns that
        /// method's label and span, and the lines just outside the span return null.
        /// </summary>
        [Test]
        public void FindPatchedSpanContainingEditedLineOrNull_WhenLineIsInsideTheEditedSpan_ReturnsTheSpanInclusiveOfBothEnds()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateLookup(SpanStartLine, SpanEndLine);

                PausePointPatchedEditedSpan span =
                    PausePointPatchedEditedSpanLocator.FindPatchedSpanContainingEditedLineOrNull(ForwardSlashFile, 25);

                Assert.That(span, Is.Not.Null);
                Assert.That(span.Label, Is.EqualTo(nameof(PausePointPatchedEditedSpanLocatorTests) + "." + nameof(SpanProbe)));
                Assert.That(span.StartLine, Is.EqualTo(SpanStartLine));
                Assert.That(span.EndLine, Is.EqualTo(SpanEndLine));
                AssertSpan(
                    PausePointPatchedEditedSpanLocator.FindPatchedSpanContainingEditedLineOrNull(ForwardSlashFile, SpanStartLine));
                AssertSpan(
                    PausePointPatchedEditedSpanLocator.FindPatchedSpanContainingEditedLineOrNull(ForwardSlashFile, SpanEndLine));
                Assert.That(
                    PausePointPatchedEditedSpanLocator.FindPatchedSpanContainingEditedLineOrNull(ForwardSlashFile, SpanStartLine - 1),
                    Is.Null);
                Assert.That(
                    PausePointPatchedEditedSpanLocator.FindPatchedSpanContainingEditedLineOrNull(ForwardSlashFile, SpanEndLine + 1),
                    Is.Null);
            }
        }

        /// <summary>
        /// What: with no hot reload port installed, the locator returns null instead of failing.
        /// </summary>
        [Test]
        public void FindPatchedSpanContainingEditedLineOrNull_WhenNoHotReloadPortIsInstalled_ReturnsNull()
        {
            IHotReloadPausePointPort installedBefore = HotReloadPausePointCoordination.HotReloadSide;
            HotReloadPausePointCoordination.HotReloadSide = null;
            try
            {
                Assert.That(
                    PausePointPatchedEditedSpanLocator.FindPatchedSpanContainingEditedLineOrNull(ForwardSlashFile, 25),
                    Is.Null);
            }
            finally
            {
                HotReloadPausePointCoordination.HotReloadSide = installedBefore;
            }
        }

        /// <summary>
        /// What: an entry without a valid edited span (0-0) is skipped, so no span is returned for
        /// a line and callers fall back to text without a range.
        /// </summary>
        [Test]
        public void FindPatchedSpanContainingEditedLineOrNull_WhenTheEntryHasNoEditedSpan_ReturnsNull()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateLookup(0, 0);

                Assert.That(
                    PausePointPatchedEditedSpanLocator.FindPatchedSpanContainingEditedLineOrNull(ForwardSlashFile, 25),
                    Is.Null);
                Assert.That(
                    PausePointPatchedEditedSpanLocator.FindPatchedSpanContainingEditedLineOrNull(ForwardSlashFile, 0),
                    Is.Null);
            }
        }

        /// <summary>
        /// What: once a file changed on disk after its hot reload, a line's patched span is not
        /// returned, because the span is in the coordinates of the source the hot reload compiled;
        /// the file is matched with forward slashes, and a file that did not change keeps its span.
        /// </summary>
        [Test]
        public void FindPatchedSpanContainingEditedLineOrNull_WhenTheFileChangedOnDiskSinceTheHotReload_ReturnsNull()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateLookup(SpanStartLine, SpanEndLine);
                scope.Port.ShimSourceChangedOnDisk = file => file == ForwardSlashFile;

                Assert.That(
                    PausePointPatchedEditedSpanLocator.FindPatchedSpanContainingEditedLineOrNull(BackslashFile, 25),
                    Is.Null);
                AssertSpan(
                    PausePointPatchedEditedSpanLocator.FindPatchedSpanContainingEditedLineOrNull(UnchangedFile, 25));
            }
        }

        /// <summary>
        /// What: once a file changed on disk after its hot reload, a patched method's span is not
        /// returned either, so the patcher's refusal carries no range from the source the hot reload
        /// compiled, while a file that did not change still returns the method's span.
        /// </summary>
        [Test]
        public void FindPatchedSpanOfMethodOrNull_WhenTheFileChangedOnDiskSinceTheHotReload_ReturnsNull()
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.ShimLookupForFile = _ => CreateLookup(SpanStartLine, SpanEndLine);
                scope.Port.ShimSourceChangedOnDisk = file => file == ForwardSlashFile;
                MethodBase probe = FindSpanProbe();

                Assert.That(
                    PausePointPatchedEditedSpanLocator.FindPatchedSpanOfMethodOrNull(BackslashFile, probe),
                    Is.Null);
                AssertSpan(PausePointPatchedEditedSpanLocator.FindPatchedSpanOfMethodOrNull(UnchangedFile, probe));
            }
        }

        internal static int SpanProbe()
        {
            return 424242;
        }

        private static void AssertSpan(PausePointPatchedEditedSpan span)
        {
            Assert.That(span, Is.Not.Null);
            Assert.That(span.StartLine, Is.EqualTo(SpanStartLine));
            Assert.That(span.EndLine, Is.EqualTo(SpanEndLine));
        }

        private static MethodBase FindSpanProbe()
        {
            MethodBase probe = typeof(PausePointPatchedEditedSpanLocatorTests).GetMethod(
                nameof(SpanProbe),
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(probe, Is.Not.Null);
            return probe;
        }

        private static HotReloadShimFileLookup CreateLookup(int sourceStartLine, int sourceEndLine)
        {
            MethodBase probe = FindSpanProbe();
            return new HotReloadShimFileLookup(
                Array.Empty<byte>(),
                Array.Empty<byte>(),
                null,
                new[] { new HotReloadShimMethodLookup(probe, probe, true, sourceStartLine, sourceEndLine) });
        }
    }
}
