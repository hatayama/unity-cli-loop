using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Manages ordered in-memory watch expressions and evaluates them once per paused frame.
    /// </summary>
    public sealed class WatchExpressionRegistry
    {
        public const int DefaultMaxHistory = 20;
        public const int MaxHistoryLimit = 100;

        private readonly IWatchEditorStateProvider _stateProvider;
        private readonly Dictionary<string, WatchExpressionEntry> _entriesById = new(StringComparer.Ordinal);
        private readonly List<WatchExpressionEntry> _entries = new();
        private int _lastEvaluatedFrameCount = int.MinValue;
        private bool _isEvaluating;

        public WatchExpressionRegistry(IWatchEditorStateProvider stateProvider)
        {
            _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        }

        public WatchRegistrationResult Register(
            string id,
            string expression,
            IWatchExpressionEvaluator evaluator,
            int maxHistory)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");
            Debug.Assert(!string.IsNullOrWhiteSpace(expression), "expression must not be null or empty");
            Debug.Assert(evaluator != null, "evaluator must not be null");

            if (_entriesById.ContainsKey(id))
            {
                return WatchRegistrationResult.FailureResult($"Watch expression '{id}' is already registered.");
            }

            if (maxHistory <= 0 || maxHistory > MaxHistoryLimit)
            {
                return WatchRegistrationResult.FailureResult(
                    $"maxHistory must be between 1 and {MaxHistoryLimit}.");
            }

            WatchExpressionEntry entry = new(id, expression, evaluator, maxHistory);
            _entriesById.Add(id, entry);
            _entries.Add(entry);
            int baselineFrameCount = _stateProvider.FrameCount;
            _lastEvaluatedFrameCount = baselineFrameCount;
            _isEvaluating = true;
            try
            {
                EvaluateEntry(entry, baselineFrameCount);
            }
            finally
            {
                _isEvaluating = false;
            }
            return WatchRegistrationResult.SuccessResult();
        }

        public bool Clear(string id)
        {
            if (!_entriesById.Remove(id, out WatchExpressionEntry entry))
            {
                return false;
            }

            _entries.Remove(entry);
            return true;
        }

        public int ClearAll()
        {
            int clearedCount = _entries.Count;
            _entriesById.Clear();
            _entries.Clear();
            return clearedCount;
        }

        public bool EvaluateIfFrameChanged()
        {
            if (!_stateProvider.IsPlaying || !_stateProvider.IsPaused)
            {
                return false;
            }

            int frameCount = _stateProvider.FrameCount;
            if (_isEvaluating || frameCount == _lastEvaluatedFrameCount)
            {
                return false;
            }

            _lastEvaluatedFrameCount = frameCount;
            _isEvaluating = true;
            try
            {
                foreach (WatchExpressionEntry entry in _entries)
                {
                    EvaluateEntry(entry, frameCount);
                }
            }
            finally
            {
                _isEvaluating = false;
            }

            return true;
        }

        public IReadOnlyList<WatchExpressionEntry> GetEntries()
        {
            return new List<WatchExpressionEntry>(_entries);
        }

        public IReadOnlyList<WatchExpressionHistoryEntry> GetHistory(string id)
        {
            if (!_entriesById.TryGetValue(id, out WatchExpressionEntry entry))
            {
                return Array.Empty<WatchExpressionHistoryEntry>();
            }

            return entry.CreateHistorySnapshot();
        }

        public int GetHistoryDroppedCount(string id)
        {
            return _entriesById.TryGetValue(id, out WatchExpressionEntry entry)
                ? entry.HistoryDroppedCount
                : 0;
        }

        private void EvaluateEntry(WatchExpressionEntry entry, int frameCount)
        {
            WatchEvaluationResult result = entry.Evaluator.Evaluate();
            entry.Append(frameCount, _stateProvider.UtcNow, result);
        }
    }
}
