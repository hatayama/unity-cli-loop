using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the compiled-line-map warning text for files that have active hot-reload patches.
    /// </summary>
    [TestFixture]
    public sealed class PausePointCompiledLineMapWarningTests
    {
        private const string ForwardSlashFile = "Assets/Scripts/Example.cs";

        private const string ExpectedWarning =
            "'Assets/Scripts/Example.cs' has active hot-reload patches. For methods this reload did not patch, --line "
            + "resolves against the last compiled source, not the edited file. Verify "
            + "ResolvedMethod and ResolvedLineText, or run 'uloop compile' and re-enable.";

        /// <summary>
        /// Verifies an active-patch file produces the compiled-line-map warning with forward slashes.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapWarningOrEmpty_WhenPatchesAreActive_ReturnsFormattedWarning()
        {
            string warning = PausePointUseCase.BuildCompiledLineMapWarningOrEmpty(true, ForwardSlashFile);

            Assert.That(warning, Is.EqualTo(ExpectedWarning));
        }

        /// <summary>
        /// Verifies a backslash path is normalized before it is interpolated into the warning.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapWarningOrEmpty_WhenFileUsesBackslashes_NormalizesToForwardSlashes()
        {
            string warning = PausePointUseCase.BuildCompiledLineMapWarningOrEmpty(true, "Assets\\Scripts\\Example.cs");

            Assert.That(warning, Is.EqualTo(ExpectedWarning));
        }

        /// <summary>
        /// Verifies the helper stays silent when the file has no active hot-reload patches.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapWarningOrEmpty_WhenPatchesAreInactive_ReturnsEmpty()
        {
            string warning = PausePointUseCase.BuildCompiledLineMapWarningOrEmpty(false, ForwardSlashFile);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }
    }
}
