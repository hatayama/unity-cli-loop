using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// PlayMode execution options for Domain Reload respect and pending-run recovery.
    /// </summary>
    public sealed class RunTestsPlayModeRunOptions
    {
        public RunTestsPlayModeRunOptions(
            bool respectEnterPlayModeSettings,
            string requestId,
            DateTime pendingRunExpiresAtUtc)
        {
            Debug.Assert(requestId != null, "requestId must not be null");

            RespectEnterPlayModeSettings = respectEnterPlayModeSettings;
            RequestId = requestId;
            PendingRunExpiresAtUtc = pendingRunExpiresAtUtc;
        }

        public bool RespectEnterPlayModeSettings { get; }
        public string RequestId { get; }
        public DateTime PendingRunExpiresAtUtc { get; }

        public static RunTestsPlayModeRunOptions WithoutRespect()
        {
            return new RunTestsPlayModeRunOptions(false, "", DateTime.MinValue);
        }

        public static bool ShouldDisableDomainReload(bool respectEnterPlayModeSettings)
        {
            return !respectEnterPlayModeSettings;
        }
    }
}
