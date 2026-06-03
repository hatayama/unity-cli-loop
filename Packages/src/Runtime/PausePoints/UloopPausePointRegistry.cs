#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Stores armed pause point state for the current Editor domain.
    /// </summary>
    internal static class UloopPausePointRegistry
    {
        public const int DefaultTimeoutSeconds = 30;

        private static readonly Dictionary<string, UloopPausePointEntry> Entries = new();
        private static IUloopPausePointPauseController _pauseController = new UnityEditorPausePointPauseController();
        private static Func<DateTime> _nowProvider = () => DateTime.UtcNow;

        public static UloopPausePointSnapshot Arm(string id, int timeoutSeconds)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");
            Debug.Assert(timeoutSeconds > 0, "timeoutSeconds must be greater than zero");

            DateTime now = NowUtc();
            UloopPausePointEntry entry = new(id, timeoutSeconds, now);
            Entries[id] = entry;
            return entry.ToSnapshot(now, _pauseController);
        }

        public static UloopPausePointSnapshot Clear(string id)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");

            DateTime now = NowUtc();
            if (!Entries.ContainsKey(id))
            {
                return UloopPausePointSnapshot.NotArmed(id, _pauseController);
            }

            UloopPausePointEntry entry = Entries[id];
            entry.MarkCleared();
            return entry.ToSnapshot(now, _pauseController);
        }

        public static UloopPausePointClearAllResult ClearAll()
        {
            DateTime now = NowUtc();
            int clearedCount = 0;
            foreach (UloopPausePointEntry entry in Entries.Values)
            {
                if (!entry.IsTerminal)
                {
                    entry.MarkCleared();
                    clearedCount++;
                }
            }

            return new UloopPausePointClearAllResult(clearedCount, now);
        }

        public static UloopPausePointSnapshot GetStatus(string id)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");

            DateTime now = NowUtc();
            if (!Entries.ContainsKey(id))
            {
                return UloopPausePointSnapshot.NotArmed(id, _pauseController);
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
                return UloopPausePointSnapshot.NotArmed(id ?? string.Empty, _pauseController);
            }

            DateTime now = NowUtc();
            if (!Entries.ContainsKey(id))
            {
                return UloopPausePointSnapshot.NotArmed(id, _pauseController);
            }

            UloopPausePointEntry entry = Entries[id];
            entry.ExpireIfNeeded(now);
            if (!entry.IsArmed)
            {
                return entry.ToSnapshot(now, _pauseController);
            }

            _pauseController.Pause();
            entry.RecordHit(now, _pauseController.IsPlaying, _pauseController.IsPaused);
            return entry.ToSnapshot(now, _pauseController);
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
            bool isArmed,
            bool isHit,
            int hitCount,
            int timeoutSeconds,
            long elapsedMilliseconds,
            bool isPlaying,
            bool isPaused,
            string message)
        {
            Id = id ?? string.Empty;
            Status = status ?? UloopPausePointStatus.NotArmed;
            IsArmed = isArmed;
            IsHit = isHit;
            HitCount = hitCount;
            TimeoutSeconds = timeoutSeconds;
            ElapsedMilliseconds = elapsedMilliseconds;
            IsPlaying = isPlaying;
            IsPaused = isPaused;
            Message = message ?? string.Empty;
        }

        public string Id { get; }
        public string Status { get; }
        public bool IsArmed { get; }
        public bool IsHit { get; }
        public int HitCount { get; }
        public int TimeoutSeconds { get; }
        public long ElapsedMilliseconds { get; }
        public bool IsPlaying { get; }
        public bool IsPaused { get; }
        public string Message { get; }

        public static UloopPausePointSnapshot NotArmed(string id, IUloopPausePointPauseController pauseController)
        {
            Debug.Assert(pauseController != null, "pauseController must not be null");

            return new UloopPausePointSnapshot(
                id,
                UloopPausePointStatus.NotArmed,
                false,
                false,
                0,
                0,
                0,
                pauseController.IsPlaying,
                pauseController.IsPaused,
                "Pause point is not armed.");
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
    /// Owns mutable state for one armed pause point id.
    /// </summary>
    internal sealed class UloopPausePointEntry
    {
        public UloopPausePointEntry(string id, int timeoutSeconds, DateTime armedAtUtc)
        {
            Id = id;
            TimeoutSeconds = timeoutSeconds;
            ArmedAtUtc = armedAtUtc;
            ExpiresAtUtc = armedAtUtc.AddSeconds(timeoutSeconds);
            Status = UloopPausePointStatus.Armed;
            IsArmed = true;
            Message = "Pause point armed.";
        }

        public string Id { get; }
        public int TimeoutSeconds { get; }
        public DateTime ArmedAtUtc { get; }
        public DateTime ExpiresAtUtc { get; }
        public string Status { get; private set; }
        public bool IsArmed { get; private set; }
        public int HitCount { get; private set; }
        public DateTime HitAtUtc { get; private set; }
        public bool IsPlayingAtHit { get; private set; }
        public bool IsPausedAtHit { get; private set; }
        public string Message { get; private set; }

        public bool IsTerminal =>
            Status == UloopPausePointStatus.Hit ||
            Status == UloopPausePointStatus.Expired ||
            Status == UloopPausePointStatus.Cleared;

        public void ExpireIfNeeded(DateTime nowUtc)
        {
            if (!IsArmed)
            {
                return;
            }

            if (nowUtc < ExpiresAtUtc)
            {
                return;
            }

            IsArmed = false;
            Status = UloopPausePointStatus.Expired;
            Message = "Pause point expired before it was hit.";
        }

        public void MarkCleared()
        {
            IsArmed = false;
            Status = UloopPausePointStatus.Cleared;
            Message = "Pause point cleared.";
        }

        public void RecordHit(DateTime nowUtc, bool isPlaying, bool isPaused)
        {
            HitCount++;
            HitAtUtc = nowUtc;
            IsPlayingAtHit = isPlaying;
            IsPausedAtHit = isPaused;
            IsArmed = false;
            Status = UloopPausePointStatus.Hit;
            Message = "Pause point hit; Unity pause was requested.";
        }

        public UloopPausePointSnapshot ToSnapshot(DateTime nowUtc, IUloopPausePointPauseController pauseController)
        {
            Debug.Assert(pauseController != null, "pauseController must not be null");

            bool isHit = Status == UloopPausePointStatus.Hit;
            bool isPlaying = isHit ? IsPlayingAtHit : pauseController.IsPlaying;
            bool isPaused = isHit ? IsPausedAtHit : pauseController.IsPaused;
            long elapsedMilliseconds = Math.Max(0, (long)(nowUtc - ArmedAtUtc).TotalMilliseconds);

            return new UloopPausePointSnapshot(
                Id,
                Status,
                IsArmed,
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
        public const string NotArmed = "NotArmed";
        public const string Armed = "Armed";
        public const string Hit = "Hit";
        public const string Expired = "Expired";
        public const string Cleared = "Cleared";
    }
}
#endif
