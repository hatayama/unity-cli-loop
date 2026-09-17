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

        /// <summary>
        /// What: a missing namespace member (CS0234) appends the hint that says a type introduced
        /// into another assembly by this reload is not visible from here yet.
        /// </summary>
        [Test]
        public void ComposeShimCompileFailureMessage_WhenCs0234_AppendsTheOtherAssemblyIntroducedTypeHint()
        {
            string message = HotReloadShimCompiler.ComposeShimCompileFailureMessage(
                new[]
                {
                    "CS0234: The type or namespace name 'Added' does not exist in the namespace 'Sample'"
                });

            Assert.That(message, Does.Contain("CS0234"));
            Assert.That(
                message,
                Does.Contain(HotReloadConstants.IntroducedTypeOtherAssemblyCompileHint));
        }

        /// <summary>
        /// What: diagnostics that ask for both existing hints keep those hints in their order and
        /// pick up the other-assembly hint exactly once, after them.
        /// </summary>
        [Test]
        public void ComposeShimCompileFailureMessage_WhenCs0246AndCs0103Together_KeepsExistingHintOrderAndAppendsTheOtherAssemblyHintOnce()
        {
            string message = HotReloadShimCompiler.ComposeShimCompileFailureMessage(
                new[]
                {
                    "CS0246: The type or namespace name 'Added' could not be found",
                    "CS0103: The name 'Added' does not exist in the current context"
                });

            int usingHintIndex = message.IndexOf(
                HotReloadConstants.MissingUsingCompileHint,
                StringComparison.Ordinal);
            int newMemberHintIndex = message.IndexOf(
                HotReloadConstants.NewMemberCompileHint,
                StringComparison.Ordinal);
            int otherAssemblyHintIndex = message.IndexOf(
                HotReloadConstants.IntroducedTypeOtherAssemblyCompileHint,
                StringComparison.Ordinal);
            Assert.That(usingHintIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(newMemberHintIndex, Is.GreaterThan(usingHintIndex));
            Assert.That(otherAssemblyHintIndex, Is.GreaterThan(newMemberHintIndex));
            Assert.That(
                CountOccurrences(message, HotReloadConstants.IntroducedTypeOtherAssemblyCompileHint),
                Is.EqualTo(1));
        }

        /// <summary>
        /// What: an unresolved type name (CS0246) on its own appends the other-assembly hint, so
        /// the hint does not depend on another diagnostic being present.
        /// </summary>
        [Test]
        public void ComposeShimCompileFailureMessage_WhenCs0246Only_AppendsTheOtherAssemblyIntroducedTypeHint()
        {
            string message = HotReloadShimCompiler.ComposeShimCompileFailureMessage(
                new[]
                {
                    "CS0246: The type or namespace name 'Added' could not be found"
                });

            Assert.That(
                message,
                Does.Contain(HotReloadConstants.IntroducedTypeOtherAssemblyCompileHint));
        }

        /// <summary>
        /// What: an unresolved simple name (CS0103) on its own appends the other-assembly hint, so
        /// the hint does not depend on another diagnostic being present.
        /// </summary>
        [Test]
        public void ComposeShimCompileFailureMessage_WhenCs0103Only_AppendsTheOtherAssemblyIntroducedTypeHint()
        {
            string message = HotReloadShimCompiler.ComposeShimCompileFailureMessage(
                new[]
                {
                    "CS0103: The name 'Added' does not exist in the current context"
                });

            Assert.That(
                message,
                Does.Contain(HotReloadConstants.IntroducedTypeOtherAssemblyCompileHint));
        }

        /// <summary>
        /// What: a missing member of a known type (CS0117) says nothing about another assembly, so
        /// it keeps the hints it had.
        /// </summary>
        [Test]
        public void ComposeShimCompileFailureMessage_WhenCs0117Only_DoesNotAppendTheOtherAssemblyHint()
        {
            string message = HotReloadShimCompiler.ComposeShimCompileFailureMessage(
                new[]
                {
                    "CS0117: 'Sample' does not contain a definition for 'Added'"
                });

            Assert.That(message, Does.Contain(HotReloadConstants.NewMemberCompileHint));
            Assert.That(
                message,
                Does.Not.Contain(HotReloadConstants.IntroducedTypeOtherAssemblyCompileHint));
        }

        /// <summary>
        /// Counts how many times one hint appears in a composed message, so a test can pin that a
        /// hint several diagnostics ask for is still written once.
        /// </summary>
        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            int index = text.IndexOf(value, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
            }

            return count;
        }
    }
}
