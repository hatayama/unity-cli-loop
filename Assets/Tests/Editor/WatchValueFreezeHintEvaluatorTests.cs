using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies WatchValueFreezeHintEvaluator's detection of a watch value that looks frozen.
    /// </summary>
    [TestFixture]
    public sealed class WatchValueFreezeHintEvaluatorTests
    {
        [Test]
        public void EvaluateFreezeHint_WhenHistoryHasFewerEntriesThanTheThreshold_ReturnsEmpty()
        {
            // Tests that a short history (not enough evaluations to judge staleness) never hints.
            List<WatchHistoryResponse> history = CreateHistory("1", "1");

            string hint = WatchValueFreezeHintEvaluator.EvaluateFreezeHint(history);

            Assert.That(hint, Is.Empty);
        }

        [Test]
        public void EvaluateFreezeHint_WhenRecentValuesAreIdentical_ReturnsHint()
        {
            // Tests the core case: the last several evaluations returned the same value.
            List<WatchHistoryResponse> history = CreateHistory("1", "1", "1");

            string hint = WatchValueFreezeHintEvaluator.EvaluateFreezeHint(history);

            Assert.That(hint, Is.Not.Empty);
            Assert.That(hint, Does.Contain("pause point"));
        }

        [Test]
        public void EvaluateFreezeHint_WhenRecentValuesDiffer_ReturnsEmpty()
        {
            // Tests that a genuinely changing value is never flagged as frozen.
            List<WatchHistoryResponse> history = CreateHistory("1", "2", "3");

            string hint = WatchValueFreezeHintEvaluator.EvaluateFreezeHint(history);

            Assert.That(hint, Is.Empty);
        }

        [Test]
        public void EvaluateFreezeHint_WhenOnlyAnOlderEntryDiffers_ReturnsHintFromRecentWindowOnly()
        {
            // Tests that the check looks at the trailing window, not the whole history: an old
            // change that happened before the most recent repeats must not suppress the hint.
            List<WatchHistoryResponse> history = CreateHistory("0", "1", "1", "1");

            string hint = WatchValueFreezeHintEvaluator.EvaluateFreezeHint(history);

            Assert.That(hint, Is.Not.Empty);
        }

        [Test]
        public void EvaluateFreezeHint_WhenARecentEvaluationFailed_ReturnsEmpty()
        {
            // Tests that evaluation errors are not mistaken for a frozen value; a failure is a
            // distinct problem the freeze hint should not paper over.
            List<WatchHistoryResponse> history = new List<WatchHistoryResponse>
            {
                CreateEntry("1", success: true, truncated: false),
                CreateEntry("1", success: true, truncated: false),
                CreateEntry(string.Empty, success: false, truncated: false)
            };

            string hint = WatchValueFreezeHintEvaluator.EvaluateFreezeHint(history);

            Assert.That(hint, Is.Empty);
        }

        [Test]
        public void EvaluateFreezeHint_WhenHistoryIsNull_ReturnsEmpty()
        {
            // Tests the defensive null-history path used before any evaluation has occurred.
            string hint = WatchValueFreezeHintEvaluator.EvaluateFreezeHint(null);

            Assert.That(hint, Is.Empty);
        }

        [Test]
        public void EvaluateFreezeHint_WhenFrozenAndRecentEntryIsTruncated_AppendsTruncationNote()
        {
            // Verifies truncated identical previews warn that cap-hidden changes are invisible.
            List<WatchHistoryResponse> history = new List<WatchHistoryResponse>
            {
                CreateEntry("1", success: true, truncated: false),
                CreateEntry("1", success: true, truncated: true),
                CreateEntry("1", success: true, truncated: false)
            };

            string hint = WatchValueFreezeHintEvaluator.EvaluateFreezeHint(history);

            Assert.That(hint, Is.Not.Empty);
            Assert.That(
                hint,
                Does.Contain(
                    "Note: the compared values are truncated previews - changes beyond the element or length cap are invisible to this comparison."));
        }

        [Test]
        public void EvaluateFreezeHint_WhenFrozenWithoutTruncation_DoesNotAppendTruncationNote()
        {
            // Verifies the truncation caveat is omitted when every compared preview is complete.
            List<WatchHistoryResponse> history = CreateHistory("1", "1", "1");

            string hint = WatchValueFreezeHintEvaluator.EvaluateFreezeHint(history);

            Assert.That(hint, Is.Not.Empty);
            Assert.That(hint, Does.Not.Contain("truncated previews"));
        }

        private static List<WatchHistoryResponse> CreateHistory(params string[] values)
        {
            List<WatchHistoryResponse> history = new List<WatchHistoryResponse>();
            foreach (string value in values)
            {
                history.Add(CreateEntry(value, success: true, truncated: false));
            }

            return history;
        }

        private static WatchHistoryResponse CreateEntry(string value, bool success, bool truncated)
        {
            return new WatchHistoryResponse
            {
                Success = success,
                Value = value,
                Truncated = truncated
            };
        }
    }
}
