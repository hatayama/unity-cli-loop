namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The domain's sibling records, read by project-relative path, that decide whether a later
    /// reload brings a file back.
    /// </summary>
    internal interface IHotReloadCarriedInLookup
    {
        // The hash the companion ledger holds for the file, or null.
        string TryGetCompanionHash(string projectRelativePath);

        // The hash of the applied-source record for the file, or null.
        string TryGetAppliedHash(string projectRelativePath);

        // Whether the file holds changes an earlier reload applied.
        bool IsActive(string projectRelativePath);
    }
}
