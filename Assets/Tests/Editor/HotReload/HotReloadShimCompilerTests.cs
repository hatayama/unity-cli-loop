using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Unit coverage for shim-compile failure message composition (hint gating).
    /// </summary>
    public class HotReloadShimCompilerTests
    {
        /// <summary>
        /// What: a missing-member diagnostic (CS0103) appends the new-member compile hint.
        /// </summary>
        [Test]
        public void ComposeShimCompileFailureMessage_MissingMemberDiagnostic_AppendsHint()
        {
            string message = HotReloadShimCompiler.ComposeShimCompileFailureMessage(
                new[]
                {
                    "CS0103: The name 'MissingHelperAddedByEdit' does not exist in the current context"
                });

            Assert.That(message, Does.Contain(HotReloadConstants.NewMemberCompileHint));
            Assert.That(message, Does.Contain("CS0103"));
        }

        /// <summary>
        /// What: a non-missing-member diagnostic (CS0229) does not get the new-member compile hint.
        /// </summary>
        [Test]
        public void ComposeShimCompileFailureMessage_NonMissingMemberDiagnostic_OmitsHint()
        {
            string error = "CS0229: Ambiguity between 'A.E' and 'A.E'";
            string message = HotReloadShimCompiler.ComposeShimCompileFailureMessage(new[] { error });

            Assert.That(message, Does.Contain(error));
            Assert.That(message, Does.Not.Contain(HotReloadConstants.NewMemberCompileHint));
        }

        /// <summary>
        /// What: a non-missing-member diagnostic whose text merely mentions CS0103 does not get the hint
        /// (only a real CS0103: prefix gates the hint).
        /// </summary>
        [Test]
        public void ComposeShimCompileFailureMessage_CodeMentionedInTextOnly_OmitsHint()
        {
            string error = "CS0229: Ambiguity between 'A.CS0103' and 'B.CS0103'";
            string message = HotReloadShimCompiler.ComposeShimCompileFailureMessage(new[] { error });

            Assert.That(message, Does.Contain(error));
            Assert.That(message, Does.Not.Contain(HotReloadConstants.NewMemberCompileHint));
        }
    }
}
