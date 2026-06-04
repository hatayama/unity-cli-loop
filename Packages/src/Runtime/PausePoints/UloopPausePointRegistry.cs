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
        private static UloopPausePointSnapshot _latestHitSnapshot;

        public static UloopPausePointSnapshot Enable(string id, int timeoutSeconds)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");
            Debug.Assert(timeoutSeconds > 0, "timeoutSeconds must be greater than zero");

            DateTime now = NowUtc();
            UloopPausePointEntry entry = new(id, timeoutSeconds, now);
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
            entry.MarkCleared();
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

            return new UloopPausePointClearAllResult(clearedCount, now);
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
            entry.RecordHit(now, _pauseController.IsPlaying, _pauseController.IsPaused);
            UloopPausePointSnapshot snapshot = entry.ToSnapshot(now, _pauseController);
            _latestHitSnapshot = snapshot;
            return snapshot;
        }

        public static UloopPausePointSnapshot GetLatestHitSnapshot()
        {
            return _latestHitSnapshot;
        }

        public static void ClearLatestHitSnapshot()
        {
            _latestHitSnapshot = null;
        }

        private static void ClearLatestHitSnapshotIfMatches(string id)
        {
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
            _latestHitSnapshot = null;
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
            long elapsedMilliseconds,
            bool isPlaying,
            bool isPaused,
            string message)
        {
            Id = id ?? string.Empty;
            Status = status ?? UloopPausePointStatus.NotEnabled;
            IsEnabled = isEnabled;
            IsHit = isHit;
            HitCount = hitCount;
            TimeoutSeconds = timeoutSeconds;
            ElapsedSinceEnabledMilliseconds = elapsedMilliseconds;
            IsPlaying = isPlaying;
            IsPaused = isPaused;
            Message = message ?? string.Empty;
        }

        public string Id { get; }
        public string Status { get; }
        public bool IsEnabled { get; }
        public bool IsHit { get; }
        public int HitCount { get; }
        public int TimeoutSeconds { get; }
        public long ElapsedSinceEnabledMilliseconds { get; }
        public bool IsPlaying { get; }
        public bool IsPaused { get; }
        public string Message { get; }

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
                0,
                pauseController.IsPlaying,
                pauseController.IsPaused,
                "Debug break is not enabled.");
        }
    }

    /// <summary>
    /// Reports the number of pause point entries cleared by a bulk clear request.
    /// </summary>
    internal sealed class UloopPausePointClearAllResult
    {
        public UloopPausePointClearAllResult(int clearedCount, DateTime clearedAtUtc)
        {
            ClearedCount = clearedCount;
            ClearedAtUtc = clearedAtUtc;
        }

        public int ClearedCount { get; }
        public DateTime ClearedAtUtc { get; }
    }

    /// <summary>
    /// Owns mutable state for one enabled pause point id.
    /// </summary>
    internal sealed class UloopPausePointEntry
    {
        public UloopPausePointEntry(string id, int timeoutSeconds, DateTime enabledAtUtc)
        {
            Id = id;
            TimeoutSeconds = timeoutSeconds;
            EnabledAtUtc = enabledAtUtc;
            ExpiresAtUtc = enabledAtUtc.AddSeconds(timeoutSeconds);
            Status = UloopPausePointStatus.Enabled;
            IsEnabled = true;
            Message = "Debug break enabled.";
        }

        public string Id { get; }
        public int TimeoutSeconds { get; }
        public DateTime EnabledAtUtc { get; }
        public DateTime ExpiresAtUtc { get; }
        public string Status { get; private set; }
        public bool IsEnabled { get; private set; }
        public int HitCount { get; private set; }
        public DateTime HitAtUtc { get; private set; }
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
            Message = "Debug break expired before it was hit.";
        }

        public void MarkCleared()
        {
            IsEnabled = false;
            Status = UloopPausePointStatus.Cleared;
            Message = "Debug break cleared.";
        }

        public void RecordHit(DateTime nowUtc, bool isPlaying, bool isPaused)
        {
            HitCount++;
            HitAtUtc = nowUtc;
            IsPlayingAtHit = isPlaying;
            IsPausedAtHit = isPaused;
            IsEnabled = false;
            Status = UloopPausePointStatus.Hit;
            Message = "Debug break hit; Unity pause was requested.";
        }

        public UloopPausePointSnapshot ToSnapshot(DateTime nowUtc, IUloopPausePointPauseController pauseController)
        {
            Debug.Assert(pauseController != null, "pauseController must not be null");

            bool isHit = Status == UloopPausePointStatus.Hit;
            bool isPlaying = isHit ? IsPlayingAtHit : pauseController.IsPlaying;
            bool isPaused = isHit ? IsPausedAtHit : pauseController.IsPaused;
            long elapsedMilliseconds = Math.Max(0, (long)(nowUtc - EnabledAtUtc).TotalMilliseconds);

            return new UloopPausePointSnapshot(
                Id,
                Status,
                IsEnabled,
                isHit,
                HitCount,
                TimeoutSeconds,
                elapsedMilliseconds,
                isPlaying,
                isPaused,
                Message);
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
