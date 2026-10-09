namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the time a compile started across that compile's domain reload to the snapshot
    /// capture that follows it. Every member reads or writes SessionState, so each call has to come
    /// from the Unity main thread.
    /// </summary>
    internal static class HotReloadCompileStartRecord
    {
        internal static void Initialize()
        {
        }

        internal static void OnCompilationStarted(object context)
        {
        }

        internal static void Record(long utcTicks)
        {
        }

        internal static HotReloadCompileStart Read()
        {
            return HotReloadCompileStart.Unknown;
        }

        internal static void Clear()
        {
        }
    }
}
