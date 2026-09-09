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
}
