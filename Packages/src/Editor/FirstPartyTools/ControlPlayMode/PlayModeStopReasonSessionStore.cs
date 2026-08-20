#nullable enable
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Holds the pending Play Mode stop reason in-domain and confirms it to SessionState
    /// on ExitingPlayMode so a domain reload cannot drop it.
    /// </summary>
    internal static class PlayModeStopReasonSessionStore
    {
        // Why: domain reload clears static fields; SessionState is the Editor-local store that survives it.
        private const string ReasonKey =
            "io.github.hatayama.uloopmcp.playModeStopReason.reason";
        private const string StoppedAtKey =
            "io.github.hatayama.uloopmcp.playModeStopReason.stoppedAtUtc";

        private static string? _pendingReason;

        internal static string? PendingReason => _pendingReason;

        internal static void SetPending(string reason)
        {
            Debug.Assert(!string.IsNullOrEmpty(reason), "stop reason must not be empty.");
            _pendingReason = reason;
        }

        // Why Try: compilationStarted is a fallback and must not replace an explicit CLI stop reason.
        internal static void TrySetPending(string reason)
        {
            Debug.Assert(!string.IsNullOrEmpty(reason), "stop reason must not be empty.");
            if (_pendingReason != null)
            {
                return;
            }

            _pendingReason = reason;
        }

        internal static void ClearPendingIfScriptCompilationFallback()
        {
            if (_pendingReason != ControlPlayModeConstants.StoppedByScriptCompilation)
            {
                return;
            }

            _pendingReason = null;
        }

        internal static void ConfirmPending(string stoppedAtUtc)
        {
            Debug.Assert(!string.IsNullOrEmpty(stoppedAtUtc), "stoppedAtUtc must not be empty.");
            string reason = _pendingReason ?? ControlPlayModeConstants.StoppedByUnknown;
            SessionState.SetString(ReasonKey, reason);
            SessionState.SetString(StoppedAtKey, stoppedAtUtc);
            _pendingReason = null;
        }

        internal static PlayModeStopReasonRecord TryReadConfirmed()
        {
            string reason = SessionState.GetString(ReasonKey, string.Empty);
            if (string.IsNullOrEmpty(reason))
            {
                return PlayModeStopReasonRecord.Empty;
            }

            return new PlayModeStopReasonRecord(
                reason,
                SessionState.GetString(StoppedAtKey, string.Empty));
        }

        internal static void ClearForTests()
        {
            _pendingReason = null;
            SessionState.SetString(ReasonKey, string.Empty);
            SessionState.SetString(StoppedAtKey, string.Empty);
        }
    }

    /// <summary>
    /// Confirmed Play Mode stop reason copied onto control-play-mode responses.
    /// </summary>
    internal readonly struct PlayModeStopReasonRecord
    {
        internal static PlayModeStopReasonRecord Empty => new PlayModeStopReasonRecord(null, null);

        internal PlayModeStopReasonRecord(string? stoppedBy, string? stoppedAtUtc)
        {
            StoppedBy = stoppedBy;
            StoppedAtUtc = stoppedAtUtc;
        }

        internal string? StoppedBy { get; }
        internal string? StoppedAtUtc { get; }
        internal bool HasValue => !string.IsNullOrEmpty(StoppedBy);
    }
}
