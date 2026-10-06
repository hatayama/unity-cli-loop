using System;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Process activity API for platforms where the Editor has no background throttle to lift.
    /// It never starts an activity.
    /// </summary>
    internal sealed class InertProcessActivityApi : IProcessActivityApi
    {
        public IntPtr Begin(string reason)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrEmpty(reason), "reason must not be empty");

            return IntPtr.Zero;
        }

        public void End(IntPtr token)
        {
            // Why every call throws: Begin never hands out a token here, so no argument can be one
            // this instance started, and a call means the caller broke the End contract.
            throw new ArgumentException("This platform never starts an activity, so there is no token to end.", nameof(token));
        }
    }
}
