namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Where a file stands once a run has written its sibling records: whether the run took it in
    /// at all, and if so whether it holds patches or would be carried into the next reload.
    /// </summary>
    internal enum HotReloadCarriedInState
    {
        // The run neither was given the file nor brought it back.
        NotInRun = 0,

        // The run applied a change from the file.
        AppliedInThisRun = 1,

        // The file holds changes an earlier reload applied, so every later reload brings it back.
        ActiveFromEarlierRun = 2,

        // A record matches the file's current source, so a later reload brings the file back
        // while its source stays the same.
        RecordedAtCurrentSource = 3,

        // No record matches the file's current source. A record of other bytes counts here too:
        // it only brings the file back once the source returns to those bytes.
        NotRecorded = 4
    }
}
