using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// When the latest compile started, as far as this Editor session knows. The snapshot capture
    /// uses it to decide which sources may have been saved after the compiler read them.
    /// </summary>
    internal readonly struct HotReloadCompileStart
    {
        internal static readonly HotReloadCompileStart Unknown = new HotReloadCompileStart(false, 0);

        private readonly bool _known;
        private readonly long _utcTicks;

        private HotReloadCompileStart(bool known, long utcTicks)
        {
            _known = known;
            _utcTicks = utcTicks;
        }

        internal static HotReloadCompileStart At(long utcTicks)
        {
            return new HotReloadCompileStart(true, utcTicks);
        }

        /// <summary>
        /// Returns the earliest write time (UTC ticks) from which a source of an assembly whose DLL
        /// was written at <paramref name="dllLastWriteTimeUtcTicks"/> may differ from what the
        /// compiler read.
        /// </summary>
        /// <remarks>
        /// Why the earlier of the two: a start later than the DLL write belongs to a compile after the
        /// one that wrote the DLL (a failed one, or one that left this DLL as it was), and starting the
        /// check there would miss a save made during the compile that did. Why the DLL write when no
        /// start is known (the first compile after the Editor started): a source written after the DLL
        /// cannot be in it, so at least those are still checked.
        /// </remarks>
        internal long SuspectWritesFrom(long dllLastWriteTimeUtcTicks)
        {
            if (!_known)
            {
                return dllLastWriteTimeUtcTicks;
            }

            return Math.Min(_utcTicks, dllLastWriteTimeUtcTicks);
        }
    }
}
