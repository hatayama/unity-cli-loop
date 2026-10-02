using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the TimerDelay and MainThreadSwitcher cancellation-token argument rewrites.
    /// </summary>
    public sealed class ThirdPartyToolMigrationDelayRulesTests
    {
        /// <summary>
        /// Verifies that a TimerDelay call opened inside a block comment and closed in code stays untouched while the real call after it is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyTimerDelayNamedArgumentsInCode_WhenCallStartsInBlockComment_RewritesOnlyCodeCall()
        {
            const string source =
                "M(/* TimerDelay.Wait(1, cancellationToken: */ ct);\nTimerDelay.Wait(2, cancellationToken: ct);";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationDelayRules.ReplaceLegacyTimerDelayNamedArgumentsInCode(
                    source,
                    Array.Empty<string>(),
                    true);

            Assert.That(
                content,
                Is.EqualTo("M(/* TimerDelay.Wait(1, cancellationToken: */ ct);\nTimerDelay.Wait(2, ct: ct);"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that an unclosed TimerDelay call is skipped while a complete call before it is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyTimerDelayNamedArgumentsInCode_WhenLaterCallIsUnclosed_RewritesOnlyClosedCall()
        {
            const string source =
                "TimerDelay.Wait(1, cancellationToken: ct);\nTimerDelay.Wait(2, cancellationToken: ct";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationDelayRules.ReplaceLegacyTimerDelayNamedArgumentsInCode(
                    source,
                    Array.Empty<string>(),
                    true);

            Assert.That(
                content,
                Is.EqualTo("TimerDelay.Wait(1, ct: ct);\nTimerDelay.Wait(2, cancellationToken: ct"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a TimerDelay call without a named cancellationToken is neither rewritten nor counted.
        /// </summary>
        [Test]
        public void ReplaceLegacyTimerDelayNamedArgumentsInCode_WhenNoCancellationTokenIsNamed_ReturnsSourceWithZeroCount()
        {
            const string source = "TimerDelay.Wait(1,ct);";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationDelayRules.ReplaceLegacyTimerDelayNamedArgumentsInCode(
                    source,
                    Array.Empty<string>(),
                    true);

            Assert.That(content, Is.EqualTo("TimerDelay.Wait(1,ct);"));
            Assert.That(replacementCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a fully qualified TimerDelay call is rewritten even when bare migration is disabled.
        /// </summary>
        [Test]
        public void ReplaceLegacyTimerDelayNamedArgumentsInCode_WhenCallIsFullyQualified_RewritesWithoutBareMigration()
        {
            const string source = "io.github.hatayama.uLoopMCP.TimerDelay.Wait(1, cancellationToken: ct);";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationDelayRules.ReplaceLegacyTimerDelayNamedArgumentsInCode(
                    source,
                    Array.Empty<string>(),
                    false);

            Assert.That(content, Is.EqualTo("io.github.hatayama.uLoopMCP.TimerDelay.Wait(1, ct: ct);"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a TimerDelay call through a known legacy alias is rewritten while one through an unknown alias is not.
        /// </summary>
        [Test]
        public void ReplaceLegacyTimerDelayNamedArgumentsInCode_WhenCallUsesLegacyAlias_RewritesOnlyKnownAlias()
        {
            const string source =
                "Other.TimerDelay.Wait(1, cancellationToken: ct);\nLegacy.TimerDelay.Wait(2, cancellationToken: ct);";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationDelayRules.ReplaceLegacyTimerDelayNamedArgumentsInCode(
                    source,
                    new[] { "Legacy" },
                    false);

            Assert.That(
                content,
                Is.EqualTo("Other.TimerDelay.Wait(1, cancellationToken: ct);\nLegacy.TimerDelay.Wait(2, ct: ct);"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that an unclosed MainThreadSwitcher call is skipped while a complete call before it is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyMainThreadSwitcherCallsInCode_WhenLaterCallIsUnclosed_RewritesOnlyClosedCall()
        {
            const string source =
                "MainThreadSwitcher.SwitchToMainThread(cancellationToken: ct);\n" +
                "MainThreadSwitcher.SwitchToMainThread(cancellationToken: ct";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationDelayRules.ReplaceLegacyMainThreadSwitcherCallsInCode(
                    source,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    true,
                    Array.Empty<string>());

            Assert.That(
                content,
                Is.EqualTo(
                    "MainThreadSwitcher.SwitchToMainThread(ct: ct);\n" +
                    "MainThreadSwitcher.SwitchToMainThread(cancellationToken: ct"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a legacy fully qualified MainThreadSwitcher call is rewritten even when bare migration is disabled.
        /// </summary>
        [Test]
        public void ReplaceLegacyMainThreadSwitcherCallsInCode_WhenCallIsLegacyQualified_RewritesWithoutBareMigration()
        {
            const string source = "io.github.hatayama.uLoopMCP.MainThreadSwitcher.SwitchToMainThread(cancellationToken: ct);";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationDelayRules.ReplaceLegacyMainThreadSwitcherCallsInCode(
                    source,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    false,
                    Array.Empty<string>());

            Assert.That(
                content,
                Is.EqualTo("io.github.hatayama.uLoopMCP.MainThreadSwitcher.SwitchToMainThread(ct: ct);"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a MainThreadSwitcher call qualified with the current Application namespace is rewritten even when bare migration is disabled.
        /// </summary>
        [Test]
        public void ReplaceLegacyMainThreadSwitcherCallsInCode_WhenCallIsCurrentApplicationQualified_RewritesWithoutBareMigration()
        {
            const string source =
                "io.github.hatayama.UnityCliLoop.Application.MainThreadSwitcher.SwitchToMainThread(cancellationToken: ct);";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationDelayRules.ReplaceLegacyMainThreadSwitcherCallsInCode(
                    source,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    false,
                    Array.Empty<string>());

            Assert.That(
                content,
                Is.EqualTo("io.github.hatayama.UnityCliLoop.Application.MainThreadSwitcher.SwitchToMainThread(ct: ct);"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }
    }
}
