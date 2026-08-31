using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies retry-resolved and speculative using hints render as fixed literals.
    /// </summary>
    [TestFixture]
    public sealed class AutoInjectedNamespaceHintBuilderTests
    {
        private const string ExpectedRetryOnlyHint =
            "Performance hint: Auto-resolved 2 missing using directive(s) after compile errors: "
            + "using System.Linq; (for 'Enumerable') using UnityEngine; (for 'GameObject') "
            + "— Include them in your code to skip auto-resolution retries and improve compilation speed.";

        private const string ExpectedSpeculativeOnlyHint =
            "Note: 1 using directive(s) were speculatively pre-injected from an identifier scan: "
            + "using System.Text; (for 'StringBuilder') "
            + "— No action needed. An attribution you do not recognize means the namespace was matched "
            + "only by a type's simple name and the directive may be unnecessary.";

        /// <summary>
        /// What: retry-resolved attributions only emit the performance hint.
        /// </summary>
        [Test]
        public void BuildHints_WhenOnlyRetryResolved_ReturnsPerformanceHint()
        {
            List<AutoInjectedNamespace> items = new()
            {
                new AutoInjectedNamespace("System.Linq", "Enumerable", false),
                new AutoInjectedNamespace("UnityEngine", "GameObject", false)
            };

            List<string> hints = AutoInjectedNamespaceHintBuilder.BuildHints(items);

            Assert.That(hints, Is.EqualTo(new[] { ExpectedRetryOnlyHint }));
        }

        /// <summary>
        /// What: speculative attributions only emit the pre-injection note.
        /// </summary>
        [Test]
        public void BuildHints_WhenOnlySpeculative_ReturnsPreInjectionNote()
        {
            List<AutoInjectedNamespace> items = new()
            {
                new AutoInjectedNamespace("System.Text", "StringBuilder", true)
            };

            List<string> hints = AutoInjectedNamespaceHintBuilder.BuildHints(items);

            Assert.That(hints, Is.EqualTo(new[] { ExpectedSpeculativeOnlyHint }));
        }

        /// <summary>
        /// What: mixed attributions emit the retry hint then the speculative note.
        /// </summary>
        [Test]
        public void BuildHints_WhenBothKindsArePresent_ReturnsRetryHintThenSpeculativeNote()
        {
            List<AutoInjectedNamespace> items = new()
            {
                new AutoInjectedNamespace("System.Text", "StringBuilder", true),
                new AutoInjectedNamespace("System.Linq", "Enumerable", false),
                new AutoInjectedNamespace("UnityEngine", "GameObject", false)
            };

            List<string> hints = AutoInjectedNamespaceHintBuilder.BuildHints(items);

            Assert.That(hints, Is.EqualTo(new[] { ExpectedRetryOnlyHint, ExpectedSpeculativeOnlyHint }));
        }

        /// <summary>
        /// What: an empty attribution list emits no hint lines.
        /// </summary>
        [Test]
        public void BuildHints_WhenEmpty_ReturnsNoHints()
        {
            List<string> hints = AutoInjectedNamespaceHintBuilder.BuildHints(new List<AutoInjectedNamespace>());

            Assert.That(hints, Is.Empty);
        }
    }
}
