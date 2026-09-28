using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes the Mono method record fields validated before a grant can write them.
    /// </summary>
    internal sealed class HotReloadMonoMethodLayout
    {
        internal const int GenericBit = 1 << 11;
        internal const int InflatedBit = 1 << 12;
        internal const int SkipVisibilityBit = 1 << 13;

        internal int FlagsOffset { get; }
        internal int ImplementationFlagsOffset { get; }
        internal int TokenOffset { get; }
        internal int NameOffset => 8 + 2 * IntPtr.Size;
        internal int BitfieldOffset => 8 + 3 * IntPtr.Size;
        internal int MaxNameBytes { get; }

        internal HotReloadMonoMethodLayout(
            int flagsOffset = 0,
            int implementationFlagsOffset = 2,
            int tokenOffset = 4,
            int maxNameBytes = 1024)
        {
            FlagsOffset = flagsOffset;
            ImplementationFlagsOffset = implementationFlagsOffset;
            TokenOffset = tokenOffset;
            MaxNameBytes = maxNameBytes;
        }
    }
}
