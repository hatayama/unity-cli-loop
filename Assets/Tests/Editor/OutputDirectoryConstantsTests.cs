#nullable enable
using System.IO;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies output-directory constants stay separator-free so Path.Combine supplies the platform separator.
    /// </summary>
    [TestFixture]
    public sealed class OutputDirectoryConstantsTests
    {
        private const string NoEmbeddedSeparatorMessage =
            "Segments must not embed a separator; Path.Combine supplies the platform separator on Windows.";

        /// <summary>
        /// Verifies OUTPUT_ROOT_DIR equals Path.Combine of ULOOP_DIR and OUTPUTS_DIR.
        /// </summary>
        [Test]
        public void OutputRootDir_IsBuiltFromSeparatorFreeSegments()
        {
            string expected = Path.Combine(UnityCliLoopConstants.ULOOP_DIR, UnityCliLoopConstants.OUTPUTS_DIR);
            Assert.That(UnityCliLoopConstants.OUTPUT_ROOT_DIR, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies each output-directory segment constant contains neither '/' nor '\'.
        /// </summary>
        [Test]
        public void OutputDirectorySegments_ContainNoPathSeparators()
        {
            AssertSegmentHasNoPathSeparator(nameof(UnityCliLoopConstants.ULOOP_DIR), UnityCliLoopConstants.ULOOP_DIR);
            AssertSegmentHasNoPathSeparator(nameof(UnityCliLoopConstants.OUTPUTS_DIR), UnityCliLoopConstants.OUTPUTS_DIR);
            AssertSegmentHasNoPathSeparator(nameof(UnityCliLoopConstants.TEST_RESULTS_DIR), UnityCliLoopConstants.TEST_RESULTS_DIR);
            AssertSegmentHasNoPathSeparator(nameof(UnityCliLoopConstants.HIERARCHY_RESULTS_DIR), UnityCliLoopConstants.HIERARCHY_RESULTS_DIR);
            AssertSegmentHasNoPathSeparator(nameof(UnityCliLoopConstants.FIND_GAMEOBJECTS_RESULTS_DIR), UnityCliLoopConstants.FIND_GAMEOBJECTS_RESULTS_DIR);
            AssertSegmentHasNoPathSeparator(nameof(UnityCliLoopConstants.SCREENSHOTS_DIR), UnityCliLoopConstants.SCREENSHOTS_DIR);
            AssertSegmentHasNoPathSeparator(nameof(UnityCliLoopConstants.VIBE_LOGS_DIR), UnityCliLoopConstants.VIBE_LOGS_DIR);
            AssertSegmentHasNoPathSeparator(nameof(RecordInputConstants.INPUT_RECORDINGS_DIR), RecordInputConstants.INPUT_RECORDINGS_DIR);
        }

        private static void AssertSegmentHasNoPathSeparator(string name, string value)
        {
            Assert.That(value, Does.Not.Contain("/"), NoEmbeddedSeparatorMessage + " (" + name + ")");
            Assert.That(value, Does.Not.Contain("\\"), NoEmbeddedSeparatorMessage + " (" + name + ")");
        }
    }
}
