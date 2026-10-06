using System;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Starts and ends an operating-system activity that keeps the Editor process from being throttled
    /// while it works in the background.
    /// </summary>
    internal interface IProcessActivityApi
    {
        /// <summary>
        /// Starts an activity and returns its token, or IntPtr.Zero when no activity was started
        /// (the platform has no such activity, or the operating system declined).
        /// A platform that lacks the native entry points may throw DllNotFoundException or
        /// EntryPointNotFoundException; callers treat that as "unavailable for this domain".
        /// </summary>
        /// <param name="reason">Non-empty ASCII text the operating system records with the activity.</param>
        IntPtr Begin(string reason);

        /// <summary>
        /// Ends the activity of a token returned by Begin. Each non-zero token must be passed exactly once,
        /// because ending the same token twice touches released native memory.
        /// </summary>
        /// <param name="token">A non-zero token returned by Begin. IntPtr.Zero throws ArgumentException.</param>
        void End(IntPtr token);
    }
}
