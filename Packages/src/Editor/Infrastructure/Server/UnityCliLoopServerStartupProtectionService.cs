using System;
using System.Threading;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Tracks the short recovery suppression window after a successful server startup.
    /// </summary>
    internal sealed class UnityCliLoopServerStartupProtectionService
    {
        private long _startupProtectionUntilTicks = 0;

        internal bool IsStartupProtectionActive()
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            return nowTicks < Volatile.Read(ref _startupProtectionUntilTicks);
        }

        internal void ActivateStartupProtection(int milliseconds)
        {
            long untilTicks = DateTime.UtcNow.AddMilliseconds(milliseconds).Ticks;
            Volatile.Write(ref _startupProtectionUntilTicks, untilTicks);
            VibeLogger.LogInfo("startup_protection_active", $"window={milliseconds}ms");
        }

        /// <summary>
        /// Clears startup protection so recovery paths can restart the server immediately.
        /// </summary>
        internal void ClearStartupProtection()
        {
            Volatile.Write(ref _startupProtectionUntilTicks, 0L);
        }
    }
}
