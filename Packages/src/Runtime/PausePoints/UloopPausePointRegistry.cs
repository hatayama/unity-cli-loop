#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Stores enabled pause point state for the current Editor domain.
    /// </summary>
    internal static class UloopPausePointRegistry
    {
        public const int DefaultTimeoutSeconds = 30;

        private static readonly Dictionary<string, UloopPausePointEntry> Entries = new();
        private static IUloopPausePointPauseController _pauseController = new UnityEditorPausePointPauseController();
        private static Func<DateTime> _nowProvider = () => DateTime.UtcNow;
        private static int _nextGeneration;
        private static int _nextHitSequence;
        private static UloopPausePointSnapshot _latestHitSnapshot;
        // One input can hit several markers in the same frame; tools need the full list,
        // not just the latest hit, to report every marker that interrupted them.
        private static readonly List<UloopPausePointSnapshot> _hitSnapshots = new();

        public static UloopPausePointSnapshot Enable(string id, int timeoutSeconds)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");
            Debug.Assert(timeoutSeconds > 0, "timeoutSeconds must be greater than zero");

            DateTime now = NowUtc();
            int generation = ++_nextGeneration;
            UloopPausePointEntry entry = new(id, timeoutSeconds, now, generation);
            Entries[id] = entry;
            ClearLatestHitSnapshotIfMatches(id);
            return entry.ToSnapshot(now, _pauseController);
        }

        public static UloopPausePointSnapshot Clear(string id)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");

            DateTime now = NowUtc();
            if (!Entries.ContainsKey(id))
            {
                return UloopPausePointSnapshot.NotEnabled(id, _pauseController);
            }

            UloopPausePointEntry entry = Entries[id];
            // Resolve expiry first so a clear after the timeout reports "expired", not a normal clear.
            entry.ExpireIfNeeded(now);
            string message = entry.Status switch
            {
                UloopPausePointStatus.Hit => "Pause point was already hit (auto-disarmed); nothing to clear.",
                UloopPausePointStatus.Expired => "Pause point had already expired before being hit; nothing to clear.",
                UloopPausePointStatus.Cleared => "Pause point was already cleared.",
                _ => "Pause point cleared."
            };
            entry.MarkCleared(message);
            ClearLatestHitSnapshotIfMatches(id);
            return entry.ToSnapshot(now, _pauseController);
        }

        public static UloopPausePointClearAllResult ClearAll()
        {
            DateTime now = NowUtc();
            int clearedCount = 0;
            foreach (UloopPausePointEntry entry in Entries.Values)
            {
                if (entry.Status == UloopPausePointStatus.Cleared)
                {
                    continue;
                }

                entry.MarkCleared();
                clearedCount++;
            }
            ClearLatestHitSnapshot();

            UloopPausePointEditorStateSnapshot editorState = UloopPausePointEditorStateSnapshot.FromController(
                _pauseController,
                UloopPausePointEditorStateCapturedAt.ClearAll);
            return new UloopPausePointClearAllResult(clearedCount, now, editorState);
        }

        public static UloopPausePointSnapshot GetStatus(string id)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");

            DateTime now = NowUtc();
            if (!Entries.ContainsKey(id))
            {
                return UloopPausePointSnapshot.NotEnabled(id, _pauseController);
            }

            UloopPausePointEntry entry = Entries[id];
            entry.ExpireIfNeeded(now);
            return entry.ToSnapshot(now, _pauseController);
        }

        public static UloopPausePointSnapshot Hit(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.Assert(false, "id must not be null or empty");
                return UloopPausePointSnapshot.NotEnabled(id ?? string.Empty, _pauseController);
            }

            DateTime now = NowUtc();
            if (!Entries.ContainsKey(id))
            {
                return UloopPausePointSnapshot.NotEnabled(id, _pauseController);
            }

            UloopPausePointEntry entry = Entries[id];
            entry.ExpireIfNeeded(now);
            if (!entry.IsEnabled)
            {
                return entry.ToSnapshot(now, _pauseController);
            }

            _pauseController.Pause();
            int hitSequence = ++_nextHitSequence;
            entry.RecordHit(now, _pauseController.IsPlaying, _pauseController.IsPaused, hitSequence);
            UloopPausePointSnapshot snapshot = entry.ToSnapshot(now, _pauseController);
            _latestHitSnapshot = snapshot;
            _hitSnapshots.RemoveAll(hitSnapshot => hitSnapshot.Id == id);
            _hitSnapshots.Add(snapshot);
            return snapshot;
        }

        public static IReadOnlyList<UloopPausePointSnapshot> GetHitSnapshots()
        {
            return _hitSnapshots;
        }

        public static UloopPausePointSnapshot GetLatestHitSnapshot()
        {
            return _latestHitSnapshot;
        }

        public static void ClearLatestHitSnapshot()
        {
            _latestHitSnapshot = null;
            _hitSnapshots.Clear();
        }

        private static void ClearLatestHitSnapshotIfMatches(string id)
        {
            _hitSnapshots.RemoveAll(hitSnapshot => hitSnapshot.Id == id);
            if (_latestHitSnapshot == null)
            {
                return;
            }

            if (_latestHitSnapshot.Id != id)
            {
                return;
            }

            _latestHitSnapshot = null;
        }

        public static void ConfigureForTests(IUloopPausePointPauseController pauseController, Func<DateTime> nowProvider)
        {
            Debug.Assert(pauseController != null, "pauseController must not be null");
            Debug.Assert(nowProvider != null, "nowProvider must not be null");

            _pauseController = pauseController;
            _nowProvider = nowProvider;
        }

        public static void ResetForTests()
        {
            Entries.Clear();
            _nextGeneration = 0;
            _nextHitSequence = 0;
            _latestHitSnapshot = null;
            _hitSnapshots.Clear();
            _pauseController = new UnityEditorPausePointPauseController();
            _nowProvider = () => DateTime.UtcNow;
        }

        private static DateTime NowUtc()
        {
            DateTime now = _nowProvider();
            return now.Kind == DateTimeKind.Utc ? now : now.ToUniversalTime();
        }
    }

    /// <summary>
    /// Names when a pause point editor-state snapshot was captured.
    /// </summary>
    internal static class UloopPausePointEditorStateCapturedAt
    {
        public const string Current = "Current";
        public const string PausePointHit = "PausePointHit";
        public const string ClearAll = "ClearAll";
    }

    /// <summary>
    /// Immutable Unity Editor state attached to pause point evidence.
    /// </summary>
    internal sealed class UloopPausePointEditorStateSnapshot
    {
        public UloopPausePointEditorStateSnapshot(bool isPlaying, bool isPaused, string capturedAt)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(capturedAt), "capturedAt must not be null or empty");

            IsPlaying = isPlaying;
            IsPaused = isPaused;
            CapturedAt = capturedAt ?? string.Empty;
        }

        public bool IsPlaying { get; }
        public bool IsPaused { get; }
        public string CapturedAt { get; }

        public static UloopPausePointEditorStateSnapshot FromController(
            IUloopPausePointPauseController pauseController,
            string capturedAt)
        {
            Debug.Assert(pauseController != null, "pauseController must not be null");

            return new UloopPausePointEditorStateSnapshot(
                pauseController.IsPlaying,
                pauseController.IsPaused,
                capturedAt);
        }
    }

    /// <summary>
    /// Provides the current pause state and performs the actual Unity Editor pause request.
    /// </summary>
    internal interface IUloopPausePointPauseController
    {
        bool IsPlaying { get; }
        bool IsPaused { get; }
        void Pause();
    }

    /// <summary>
    /// Adapts pause point hits to UnityEditor.EditorApplication state.
    /// </summary>
    internal sealed class UnityEditorPausePointPauseController : IUloopPausePointPauseController
    {
        public bool IsPlaying => EditorApplication.isPlaying;
        public bool IsPaused => EditorApplication.isPaused;

        public void Pause()
        {
            EditorApplication.isPaused = true;
        }
    }

    /// <summary>
    /// Immutable view of one pause point state returned to tools and the CLI bridge.
    /// </summary>
    internal sealed class UloopPausePointSnapshot
    {
        public UloopPausePointSnapshot(
            string id,
            string status,
            bool isEnabled,
            bool isHit,
            int hitCount,
            int timeoutSeconds,
            bool expired,
            string enabledAtUtc,
            long elapsedMilliseconds,
            long remainingMilliseconds,
            int generation,
            UloopPausePointEditorStateSnapshot editorState,
            string firstHitAtUtc,
            string lastHitAtUtc,
            int firstHitSequence,
            int lastHitSequence,
            string message,
            string recommendedNextAction)
        {
            Debug.Assert(editorState != null, "editorState must not be null");

            Id = id ?? string.Empty;
            Status = status ?? UloopPausePointStatus.NotEnabled;
            IsEnabled = isEnabled;
            IsHit = isHit;
            HitCount = hitCount;
            TimeoutSeconds = timeoutSeconds;
            Expired = expired;
            EnabledAtUtc = enabledAtUtc ?? string.Empty;
            ElapsedSinceEnabledMilliseconds = elapsedMilliseconds;
            RemainingMilliseconds = remainingMilliseconds;
            Generation = generation;
            EditorState = editorState;
            FirstHitAtUtc = firstHitAtUtc ?? string.Empty;
            LastHitAtUtc = lastHitAtUtc ?? string.Empty;
            FirstHitSequence = firstHitSequence;
            LastHitSequence = lastHitSequence;
            Message = message ?? string.Empty;
            RecommendedNextAction = recommendedNextAction ?? string.Empty;
        }

        public string Id { get; }
        public string Status { get; }
        public bool IsEnabled { get; }
        public bool IsHit { get; }
        public int HitCount { get; }
        public int TimeoutSeconds { get; }
        public bool Expired { get; }
        public string EnabledAtUtc { get; }
        public long ElapsedSinceEnabledMilliseconds { get; }
        public long RemainingMilliseconds { get; }
        public int Generation { get; }
        public UloopPausePointEditorStateSnapshot EditorState { get; }
        public string FirstHitAtUtc { get; }
        public string LastHitAtUtc { get; }
        public int FirstHitSequence { get; }
        public int LastHitSequence { get; }
        public string Message { get; }
        public string RecommendedNextAction { get; }

        public static UloopPausePointSnapshot NotEnabled(string id, IUloopPausePointPauseController pauseController)
        {
            Debug.Assert(pauseController != null, "pauseController must not be null");

            return new UloopPausePointSnapshot(
                id,
                UloopPausePointStatus.NotEnabled,
                false,
                false,
                0,
                0,
                false,
                string.Empty,
                0,
                0,
                0,
                UloopPausePointEditorStateSnapshot.FromController(
                    pauseController,
                    UloopPausePointEditorStateCapturedAt.Current),
                string.Empty,
                string.Empty,
                0,
                0,
                "Pause point is not enabled.",
                string.Empty);
        }
    }

    /// <summary>
    /// Reports the number of pause point entries cleared by a bulk clear request.
    /// </summary>
    internal sealed class UloopPausePointClearAllResult
    {
        public UloopPausePointClearAllResult(
            int clearedCount,
            DateTime clearedAtUtc,
            UloopPausePointEditorStateSnapshot editorState)
        {
            Debug.Assert(editorState != null, "editorState must not be null");

            ClearedCount = clearedCount;
            ClearedAtUtc = clearedAtUtc;
            EditorState = editorState;
        }

        public int ClearedCount { get; }
        public DateTime ClearedAtUtc { get; }
        public UloopPausePointEditorStateSnapshot EditorState { get; }
    }

    /// <summary>
    /// Owns mutable state for one enabled pause point id.
    /// </summary>
    internal sealed class UloopPausePointEntry
    {
        public UloopPausePointEntry(string id, int timeoutSeconds, DateTime enabledAtUtc, int generation)
        {
            Id = id;
            TimeoutSeconds = timeoutSeconds;
            EnabledAtUtc = enabledAtUtc;
            ExpiresAtUtc = enabledAtUtc.AddSeconds(timeoutSeconds);
            Generation = generation;
            Status = UloopPausePointStatus.Enabled;
            IsEnabled = true;
            Message = "Pause point enabled.";
        }

        public string Id { get; }
        public int TimeoutSeconds { get; }
        public DateTime EnabledAtUtc { get; }
        public DateTime ExpiresAtUtc { get; }
        public int Generation { get; }
        public string Status { get; private set; }
        public bool IsEnabled { get; private set; }
        public int HitCount { get; private set; }
        public DateTime FirstHitAtUtc { get; private set; }
        public DateTime HitAtUtc { get; private set; }
        public int FirstHitSequence { get; private set; }
        public int LastHitSequence { get; private set; }
        public bool IsPlayingAtHit { get; private set; }
        public bool IsPausedAtHit { get; private set; }
        public string Message { get; private set; }

        public void ExpireIfNeeded(DateTime nowUtc)
        {
            if (!IsEnabled)
            {
                return;
            }

            if (nowUtc < ExpiresAtUtc)
            {
                return;
            }

            IsEnabled = false;
            Status = UloopPausePointStatus.Expired;
            Message = "Pause point expired before it was hit.";
        }

        public void MarkCleared(string message = "Pause point cleared.")
        {
            IsEnabled = false;
            Status = UloopPausePointStatus.Cleared;
            Message = message;
        }

        public void RecordHit(DateTime nowUtc, bool isPlaying, bool isPaused, int hitSequence)
        {
            Debug.Assert(hitSequence > 0, "hitSequence must be greater than zero");

            if (HitCount == 0)
            {
                FirstHitAtUtc = nowUtc;
                FirstHitSequence = hitSequence;
            }

            HitCount++;
            HitAtUtc = nowUtc;
            LastHitSequence = hitSequence;
            IsPlayingAtHit = isPlaying;
            IsPausedAtHit = isPaused;
            IsEnabled = false;
            Status = UloopPausePointStatus.Hit;
            Message = "Pause point hit; Unity pause was requested.";
        }

        public UloopPausePointSnapshot ToSnapshot(DateTime nowUtc, IUloopPausePointPauseController pauseController)
        {
            Debug.Assert(pauseController != null, "pauseController must not be null");

            bool isHit = Status == UloopPausePointStatus.Hit;
            bool expired = Status == UloopPausePointStatus.Expired;
            UloopPausePointEditorStateSnapshot editorState = isHit
                ? new UloopPausePointEditorStateSnapshot(
                    IsPlayingAtHit,
                    IsPausedAtHit,
                    UloopPausePointEditorStateCapturedAt.PausePointHit)
                : UloopPausePointEditorStateSnapshot.FromController(
                    pauseController,
                    UloopPausePointEditorStateCapturedAt.Current);
            long elapsedMilliseconds = Math.Max(0, (long)(nowUtc - EnabledAtUtc).TotalMilliseconds);
            long remainingMilliseconds = CalculateRemainingMilliseconds(nowUtc);
            string recommendedNextAction = expired ? CreateExpiredRecommendedNextAction() : string.Empty;
            string firstHitAtUtc = HitCount > 0 ? FormatUtc(FirstHitAtUtc) : string.Empty;
            string lastHitAtUtc = HitCount > 0 ? FormatUtc(HitAtUtc) : string.Empty;

            return new UloopPausePointSnapshot(
                Id,
                Status,
                IsEnabled,
                isHit,
                HitCount,
                TimeoutSeconds,
                expired,
                FormatUtc(EnabledAtUtc),
                elapsedMilliseconds,
                remainingMilliseconds,
                Generation,
                editorState,
                firstHitAtUtc,
                lastHitAtUtc,
                FirstHitSequence,
                LastHitSequence,
                Message,
                recommendedNextAction);
        }

        private long CalculateRemainingMilliseconds(DateTime nowUtc)
        {
            if (!IsEnabled)
            {
                return 0;
            }

            long remainingMilliseconds = (long)(ExpiresAtUtc - nowUtc).TotalMilliseconds;
            return Math.Max(0, remainingMilliseconds);
        }

        private string CreateExpiredRecommendedNextAction()
        {
            return "Clear this marker, then re-enable it with the same Id and TimeoutSeconds values.";
        }

        private static string FormatUtc(DateTime value)
        {
            DateTime utcValue = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return utcValue.ToString("O");
        }
    }

    /// <summary>
    /// Centralizes status names shared by Editor tools and the native CLI.
    /// </summary>
    internal static class UloopPausePointStatus
    {
        public const string NotEnabled = "NotEnabled";
        public const string Enabled = "Enabled";
        public const string Hit = "Hit";
        public const string Expired = "Expired";
        public const string Cleared = "Cleared";
    }
}
#endif
