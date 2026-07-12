using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Owns one registered watch expression and its bounded evaluation history.
    /// </summary>
    public sealed class WatchExpressionEntry
    {
        private readonly Queue<WatchExpressionHistoryEntry> _history = new();

        internal WatchExpressionEntry(
            string id,
            string expression,
            IWatchExpressionEvaluator evaluator,
            int maxHistory)
        {
            Id = id;
            Expression = expression;
            Evaluator = evaluator;
            MaxHistory = maxHistory;
        }

        public string Id { get; }
        public string Expression { get; }
        public int MaxHistory { get; }
        public int HistoryDroppedCount { get; private set; }
        internal int LastEvaluatedFrameCount { get; private set; } = int.MinValue;
        internal IWatchExpressionEvaluator Evaluator { get; }

        internal void MarkEvaluated(int frameCount)
        {
            LastEvaluatedFrameCount = frameCount;
        }

        internal void Append(int frameCount, DateTime evaluatedAtUtc, WatchEvaluationResult result)
        {
            if (_history.Count == MaxHistory)
            {
                _history.Dequeue();
                HistoryDroppedCount++;
            }

            _history.Enqueue(new WatchExpressionHistoryEntry(frameCount, evaluatedAtUtc, result));
        }

        internal IReadOnlyList<WatchExpressionHistoryEntry> CreateHistorySnapshot()
        {
            return new List<WatchExpressionHistoryEntry>(_history);
        }
    }
}
