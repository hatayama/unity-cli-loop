using System;

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
        /// What: a signature-mismatch diagnostic (CS1501) appends the hint (re-signatured members
        /// need a real compile).
        /// </summary>
        [Test]
        public void ComposeShimCompileFailureMessage_SignatureMismatchDiagnostic_AppendsHint()
        {
            string message = HotReloadShimCompiler.ComposeShimCompileFailureMessage(
                new[]
                {
                    "CS1501: No overload for method 'Helper' takes 2 arguments"
                });

            Assert.That(message, Does.Contain(HotReloadConstants.NewMemberCompileHint));
            Assert.That(message, Does.Contain("CS1501"));
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

        /// <summary>
        /// What: CS0246 leads with the missing using / global using hint, then the new-member hint.
        /// </summary>
        [Test]
        public void ComposeShimCompileFailureMessage_TypeNotFound_LeadsWithMissingUsingHint()
        {
            string message = HotReloadShimCompiler.ComposeShimCompileFailureMessage(
                new[]
                {
                    "CS0246: The type or namespace name 'HotReloadGlobalAlias' could not be found"
                });

            int usingHintIndex = message.IndexOf(
                HotReloadConstants.MissingUsingCompileHint,
                StringComparison.Ordinal);
            int newMemberHintIndex = message.IndexOf(
                HotReloadConstants.NewMemberCompileHint,
                StringComparison.Ordinal);
            Assert.That(usingHintIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(newMemberHintIndex, Is.GreaterThan(usingHintIndex));
        }

        /// <summary>
        /// What: CS1061 also leads with the missing using / global using hint.
        /// </summary>
        [Test]
        public void ComposeShimCompileFailureMessage_MissingExtension_LeadsWithMissingUsingHint()
        {
            string message = HotReloadShimCompiler.ComposeShimCompileFailureMessage(
                new[]
                {
                    "CS1061: 'int' does not contain a definition for 'Forget'"
                });

            int usingHintIndex = message.IndexOf(
                HotReloadConstants.MissingUsingCompileHint,
                StringComparison.Ordinal);
            int newMemberHintIndex = message.IndexOf(
                HotReloadConstants.NewMemberCompileHint,
                StringComparison.Ordinal);
            Assert.That(usingHintIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(newMemberHintIndex, Is.GreaterThan(usingHintIndex));
        }
    }
}
