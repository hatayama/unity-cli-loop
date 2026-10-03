using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the legacy EditorDelay.DelayFrame call rewrite to EditorFrameWaiter.
    /// </summary>
    public sealed class ThirdPartyToolMigrationEditorDelayRulesTests
    {
        private const string BareMigratedCallPrefix =
            "EditorFrameWaiter.WaitFramesOrTimeoutAsync(";
        private const string BareTimeoutExpression =
            "UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS";

        /// <summary>
        /// Verifies that a call opened inside a block comment and closed in code stays untouched while the real call after it is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorDelayFrameCallsInCode_WhenCallStartsInBlockComment_RewritesOnlyCodeCall()
        {
            const string source = "M(/* EditorDelay.DelayFrame( */ 1);\nEditorDelay.DelayFrame(2);";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationEditorDelayRules.ReplaceLegacyEditorDelayFrameCallsInCode(
                    source,
                    Array.Empty<string>(),
                    true,
                    false);

            Assert.That(
                content,
                Is.EqualTo("M(/* EditorDelay.DelayFrame( */ 1);\n" + BareMigratedCallPrefix + "2, " + BareTimeoutExpression + ");"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a call without a closing parenthesis is skipped while a complete call before it is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorDelayFrameCallsInCode_WhenLaterCallIsUnclosed_RewritesOnlyClosedCall()
        {
            const string source = "EditorDelay.DelayFrame(2);\nEditorDelay.DelayFrame(3";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationEditorDelayRules.ReplaceLegacyEditorDelayFrameCallsInCode(
                    source,
                    Array.Empty<string>(),
                    true,
                    false);

            Assert.That(
                content,
                Is.EqualTo(BareMigratedCallPrefix + "2, " + BareTimeoutExpression + ");\nEditorDelay.DelayFrame(3"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a call with more arguments than DelayFrame accepts is left unchanged while a valid call is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorDelayFrameCallsInCode_WhenCallHasThreeArguments_LeavesThatCallUnchanged()
        {
            const string source = "EditorDelay.DelayFrame(1, ct, extra);\nEditorDelay.DelayFrame(2);";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationEditorDelayRules.ReplaceLegacyEditorDelayFrameCallsInCode(
                    source,
                    Array.Empty<string>(),
                    true,
                    false);

            Assert.That(
                content,
                Is.EqualTo("EditorDelay.DelayFrame(1, ct, extra);\n" + BareMigratedCallPrefix + "2, " + BareTimeoutExpression + ");"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a call naming frameCount twice is left unchanged while a valid call is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorDelayFrameCallsInCode_WhenFrameCountIsNamedTwice_LeavesThatCallUnchanged()
        {
            const string source = "EditorDelay.DelayFrame(frameCount: 1, frameCount: 2);\nEditorDelay.DelayFrame(2);";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationEditorDelayRules.ReplaceLegacyEditorDelayFrameCallsInCode(
                    source,
                    Array.Empty<string>(),
                    true,
                    false);

            Assert.That(
                content,
                Is.EqualTo("EditorDelay.DelayFrame(frameCount: 1, frameCount: 2);\n" + BareMigratedCallPrefix + "2, " + BareTimeoutExpression + ");"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a call naming cancellationToken twice is left unchanged while a valid call is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorDelayFrameCallsInCode_WhenCancellationTokenIsNamedTwice_LeavesThatCallUnchanged()
        {
            const string source =
                "EditorDelay.DelayFrame(cancellationToken: a, cancellationToken: b);\nEditorDelay.DelayFrame(2);";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationEditorDelayRules.ReplaceLegacyEditorDelayFrameCallsInCode(
                    source,
                    Array.Empty<string>(),
                    true,
                    false);

            Assert.That(
                content,
                Is.EqualTo("EditorDelay.DelayFrame(cancellationToken: a, cancellationToken: b);\n" + BareMigratedCallPrefix + "2, " + BareTimeoutExpression + ");"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that named frameCount and cancellationToken arguments are reordered into the positional EditorFrameWaiter form.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorDelayFrameCallsInCode_WhenArgumentsAreNamed_MapsThemToPositionalArguments()
        {
            const string source = "EditorDelay.DelayFrame(cancellationToken: ct, frameCount: 3);";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationEditorDelayRules.ReplaceLegacyEditorDelayFrameCallsInCode(
                    source,
                    Array.Empty<string>(),
                    true,
                    false);

            Assert.That(content, Is.EqualTo(BareMigratedCallPrefix + "3, " + BareTimeoutExpression + ", ct);"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a fully qualified legacy call is rewritten to fully qualified current names even when bare migration is disabled.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorDelayFrameCallsInCode_WhenCallIsFullyQualified_RewritesToQualifiedCurrentNames()
        {
            const string source = "io.github.hatayama.uLoopMCP.EditorDelay.DelayFrame(2);";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationEditorDelayRules.ReplaceLegacyEditorDelayFrameCallsInCode(
                    source,
                    Array.Empty<string>(),
                    false,
                    false);

            Assert.That(
                content,
                Is.EqualTo(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.EditorFrameWaiter.WaitFramesOrTimeoutAsync(2, " +
                    "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS);"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a call through a known legacy namespace alias keeps the alias, while an unknown alias is left unchanged.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorDelayFrameCallsInCode_WhenCallUsesLegacyAlias_RewritesThroughAliasOnly()
        {
            const string source = "Other.EditorDelay.DelayFrame(1);\nLegacy.EditorDelay.DelayFrame(2);";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationEditorDelayRules.ReplaceLegacyEditorDelayFrameCallsInCode(
                    source,
                    new[] { "Legacy" },
                    false,
                    false);

            Assert.That(
                content,
                Is.EqualTo(
                    "Other.EditorDelay.DelayFrame(1);\n" +
                    "Legacy.EditorFrameWaiter.WaitFramesOrTimeoutAsync(2, Legacy.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS);"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }
    }
}
