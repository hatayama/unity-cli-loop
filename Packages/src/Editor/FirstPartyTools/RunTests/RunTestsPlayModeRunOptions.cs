using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// PlayMode execution options for Domain Reload respect and pending-run recovery.
    /// </summary>
    internal sealed class RunTestsPlayModeRunOptions
    {
        internal RunTestsPlayModeRunOptions(
            bool respectEnterPlayModeSettings,
            string requestId,
            DateTime pendingRunExpiresAtUtc)
        {
            Debug.Assert(requestId != null, "requestId must not be null");

            RespectEnterPlayModeSettings = respectEnterPlayModeSettings;
            RequestId = requestId;
            PendingRunExpiresAtUtc = pendingRunExpiresAtUtc;
        }

        internal bool RespectEnterPlayModeSettings { get; }
        internal string RequestId { get; }
        internal DateTime PendingRunExpiresAtUtc { get; }

        internal static RunTestsPlayModeRunOptions WithoutRespect()
        {
            return new RunTestsPlayModeRunOptions(false, "", DateTime.MinValue);
        }

        internal static bool ShouldDisableDomainReload(bool respectEnterPlayModeSettings)
        {
            return !respectEnterPlayModeSettings;
        }
    }
}
