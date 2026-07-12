using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies watch expression registration, ordered frame evaluation, bounded history, and re-entry protection.
    /// </summary>
    [TestFixture]
    public sealed class WatchExpressionRegistryTests
    {
        /// <summary>
        /// Verifies registration evaluates an immediate baseline frame.
        /// </summary>
        [Test]
        public void Register_AddsImmediateBaselineToHistory()
        {
            FakeWatchEditorStateProvider stateProvider = new(10, true, true);
            WatchExpressionRegistry registry = new(stateProvider);

            WatchRegistrationResult result = registry.Register(
                "speed",
                "speed",
                new SequenceWatchExpressionEvaluator(3),
                20);

            Assert.That(result.Success, Is.True);
            WatchExpressionHistoryEntry entry = registry.GetHistory("speed").Single();
            Assert.That(entry.FrameCount, Is.EqualTo(10));
            Assert.That(entry.Result.Value, Is.EqualTo(3));
        }

        /// <summary>
        /// Verifies changed frames evaluate watches in registration order only once per frame.
        /// </summary>
        [Test]
        public void EvaluateIfFrameChanged_EvaluatesInRegistrationOrderAndSkipsSameFrame()
        {
            FakeWatchEditorStateProvider stateProvider = new(10, true, true);
            List<string> evaluationOrder = new();
            WatchExpressionRegistry registry = new(stateProvider);
            registry.Register("first", "first", new RecordingWatchExpressionEvaluator("first", evaluationOrder), 20);
            registry.Register("second", "second", new RecordingWatchExpressionEvaluator("second", evaluationOrder), 20);
            evaluationOrder.Clear();

            stateProvider.FrameCount = 11;
            bool evaluated = registry.EvaluateIfFrameChanged();
            bool evaluatedAgain = registry.EvaluateIfFrameChanged();

            Assert.That(evaluated, Is.True);
            Assert.That(evaluatedAgain, Is.False);
            Assert.That(evaluationOrder, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(registry.GetHistory("first"), Has.Count.EqualTo(2));
            Assert.That(registry.GetHistory("second"), Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Verifies registering a watch after a frame advances does not skip existing watches on that frame.
        /// </summary>
        [Test]
        public void Register_AfterFrameAdvance_DoesNotSkipExistingWatchEvaluation()
        {
            FakeWatchEditorStateProvider stateProvider = new(10, true, true);
            WatchExpressionRegistry registry = new(stateProvider);
            registry.Register("first", "first", new SequenceWatchExpressionEvaluator(1, 2), 20);

            stateProvider.FrameCount = 11;
            registry.Register("second", "second", new SequenceWatchExpressionEvaluator(3), 20);
            registry.EvaluateIfFrameChanged();

            Assert.That(registry.GetHistory("first"), Has.Count.EqualTo(2));
            Assert.That(registry.GetHistory("second"), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Verifies a frame change while not paused does not evaluate watches before a paused evaluation.
        /// </summary>
        [Test]
        public void EvaluateIfFrameChanged_WhenNotPaused_DoesNotEvaluate()
        {
            FakeWatchEditorStateProvider stateProvider = new(10, true, false);
            SequenceWatchExpressionEvaluator evaluator = new(1, 2);
            WatchExpressionRegistry registry = new(stateProvider);
            registry.Register("speed", "speed", evaluator, 20);

            stateProvider.FrameCount = 11;
            bool evaluatedWhileNotPaused = registry.EvaluateIfFrameChanged();

            Assert.That(evaluatedWhileNotPaused, Is.False);
            Assert.That(registry.GetHistory("speed"), Has.Count.EqualTo(1));
            Assert.That(evaluator.EvaluationCount, Is.EqualTo(1));

            stateProvider.IsPaused = true;
            bool evaluatedAfterPause = registry.EvaluateIfFrameChanged();

            Assert.That(evaluatedAfterPause, Is.True);
            Assert.That(registry.GetHistory("speed"), Has.Count.EqualTo(2));
            Assert.That(evaluator.EvaluationCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies each watch keeps only its configured history tail and dropped count.
        /// </summary>
        [Test]
        public void History_WhenLimitIsExceeded_DropsOldestEntry()
        {
            FakeWatchEditorStateProvider stateProvider = new(10, true, true);
            WatchExpressionRegistry registry = new(stateProvider);
            registry.Register("speed", "speed", new SequenceWatchExpressionEvaluator(0, 1, 2), 2);

            stateProvider.FrameCount = 11;
            registry.EvaluateIfFrameChanged();
            stateProvider.FrameCount = 12;
            registry.EvaluateIfFrameChanged();

            IReadOnlyList<WatchExpressionHistoryEntry> history = registry.GetHistory("speed");
            Assert.That(history.Select(entry => entry.FrameCount), Is.EqualTo(new[] { 11, 12 }));
            Assert.That(registry.GetHistoryDroppedCount("speed"), Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies evaluator re-entry does not start a nested registry evaluation.
        /// </summary>
        [Test]
        public void EvaluateIfFrameChanged_WhenEvaluatorReentersRegistry_DoesNotEvaluateNestedFrame()
        {
            FakeWatchEditorStateProvider stateProvider = new(10, true, true);
            WatchExpressionRegistry registry = new(stateProvider);
            ReenteringWatchExpressionEvaluator evaluator = new(() => registry.EvaluateIfFrameChanged());
            registry.Register("speed", "speed", evaluator, 20);

            stateProvider.FrameCount = 11;
            bool evaluated = registry.EvaluateIfFrameChanged();

            Assert.That(evaluated, Is.True);
            Assert.That(evaluator.EvaluationCount, Is.EqualTo(2));
            Assert.That(registry.GetHistory("speed"), Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Verifies clearing all watches removes every registered expression.
        /// </summary>
        [Test]
        public void ClearAll_RemovesAllRegisteredExpressions()
        {
            FakeWatchEditorStateProvider stateProvider = new(10, true, true);
            WatchExpressionRegistry registry = new(stateProvider);
            registry.Register("first", "first", new SequenceWatchExpressionEvaluator(1), 20);
            registry.Register("second", "second", new SequenceWatchExpressionEvaluator(2), 20);

            int clearedCount = registry.ClearAll();

            Assert.That(clearedCount, Is.EqualTo(2));
            Assert.That(registry.GetEntries(), Is.Empty);
        }

        /// <summary>
        /// Verifies clearing one watch leaves the other watch evaluating on later frames.
        /// </summary>
        [Test]
        public void Clear_RemovesOnlySelectedExpression()
        {
            FakeWatchEditorStateProvider stateProvider = new(10, true, true);
            SequenceWatchExpressionEvaluator firstEvaluator = new(1, 2);
            SequenceWatchExpressionEvaluator secondEvaluator = new(3, 4);
            WatchExpressionRegistry registry = new(stateProvider);
            registry.Register("first", "first", firstEvaluator, 20);
            registry.Register("second", "second", secondEvaluator, 20);

            bool cleared = registry.Clear("first");
            stateProvider.FrameCount = 11;
            bool evaluated = registry.EvaluateIfFrameChanged();

            Assert.That(cleared, Is.True);
            Assert.That(evaluated, Is.True);
            Assert.That(registry.GetHistory("first"), Is.Empty);
            Assert.That(registry.GetHistory("second"), Has.Count.EqualTo(2));
            Assert.That(secondEvaluator.EvaluationCount, Is.EqualTo(2));
        }

        private sealed class FakeWatchEditorStateProvider : IWatchEditorStateProvider
        {
            public FakeWatchEditorStateProvider(int frameCount, bool isPlaying, bool isPaused)
            {
                FrameCount = frameCount;
                IsPlaying = isPlaying;
                IsPaused = isPaused;
            }

            public int FrameCount { get; set; }
            public bool IsPlaying { get; set; }
            public bool IsPaused { get; set; }
            public System.DateTime UtcNow => new(2026, 6, 3, 0, 0, 0, System.DateTimeKind.Utc);
        }

        private sealed class SequenceWatchExpressionEvaluator : IWatchExpressionEvaluator
        {
            private readonly Queue<object> _values;
            private object _lastValue;

            public SequenceWatchExpressionEvaluator(params object[] values)
            {
                _values = new Queue<object>(values);
                _lastValue = values.Length > 0 ? values[values.Length - 1] : null;
            }

            public int EvaluationCount { get; private set; }

            public WatchEvaluationResult Evaluate()
            {
                EvaluationCount++;
                if (_values.Count > 0)
                {
                    _lastValue = _values.Dequeue();
                }

                return WatchEvaluationResult.SuccessResult(_lastValue);
            }
        }

        private sealed class RecordingWatchExpressionEvaluator : IWatchExpressionEvaluator
        {
            private readonly string _name;
            private readonly List<string> _evaluationOrder;

            public RecordingWatchExpressionEvaluator(string name, List<string> evaluationOrder)
            {
                _name = name;
                _evaluationOrder = evaluationOrder;
            }

            public WatchEvaluationResult Evaluate()
            {
                _evaluationOrder.Add(_name);
                return WatchEvaluationResult.SuccessResult(_name);
            }
        }

        private sealed class ReenteringWatchExpressionEvaluator : IWatchExpressionEvaluator
        {
            private readonly System.Action _reenter;
            private bool _hasReentered;

            public ReenteringWatchExpressionEvaluator(System.Action reenter)
            {
                _reenter = reenter;
            }

            public int EvaluationCount { get; private set; }

            public WatchEvaluationResult Evaluate()
            {
                EvaluationCount++;
                if (!_hasReentered)
                {
                    _hasReentered = true;
                    _reenter();
                }

                return WatchEvaluationResult.SuccessResult(EvaluationCount);
            }
        }
    }
}
