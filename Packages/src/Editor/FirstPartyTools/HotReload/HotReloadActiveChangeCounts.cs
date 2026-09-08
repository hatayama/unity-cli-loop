namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Every number one report needs about what this domain currently holds, read together.
    /// </summary>
    /// <remarks>
    /// Why one value carries all three: a report names a total and then says what it is made of,
    /// and reading the parts separately lets another reload activate a type between the reads, so
    /// the heading, the decision, and the total would answer about different moments.
    /// </remarks>
    internal readonly struct HotReloadActiveChangeSnapshot
    {
        internal HotReloadActiveChangeSnapshot(int patchAndAddedMemberCount, int introducedTypeCount)
        {
            PatchAndAddedMemberCount = patchAndAddedMemberCount;
            IntroducedTypeCount = introducedTypeCount;
            RuntimeChangeTotal = patchAndAddedMemberCount + introducedTypeCount;
        }

        /// <summary>Patched methods plus added members: what a run reports against PatchedTotal.</summary>
        internal int PatchAndAddedMemberCount { get; }

        internal int IntroducedTypeCount { get; }

        /// <summary>How many hot-reload changes the next Domain Reload discards.</summary>
        internal int RuntimeChangeTotal { get; }
    }

    /// <summary>
    /// The one place the hot-reload side reads how many runtime changes this domain holds.
    /// </summary>
    /// <remarks>
    /// Why a single accessor: the registry counts artifact assemblies as well as types, and a
    /// report that reached for the artifact count would total two types of one batch as one. Every
    /// consumer that wants a type count goes through here so only one call site can be wrong.
    /// </remarks>
    internal static class HotReloadActiveChangeCounts
    {
        internal static int IntroducedTypeCount => HotReloadIntroducedTypeHolder.Registry.ActiveTypeCount;

        /// <summary>
        /// How many hot-reload changes the next Domain Reload discards: patched methods, added
        /// members, and the types this domain introduced.
        /// </summary>
        /// <remarks>
        /// Why the types belong here: they live in artifact assemblies the reload retained, so a
        /// Domain Reload unloads them exactly as it unloads a patch. A run that patched no method
        /// still has something to lose, and every decision about what a reload would lose reads
        /// this total rather than counting for itself.
        /// </remarks>
        internal static int RuntimeChangeTotal => Capture().RuntimeChangeTotal;

        /// <summary>Reads every active-change number once, so one report cannot mix two moments.</summary>
        internal static HotReloadActiveChangeSnapshot Capture()
        {
            return new HotReloadActiveChangeSnapshot(
                HotReloadPatcher.ActiveChangeCount,
                IntroducedTypeCount);
        }
    }
}
