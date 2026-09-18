namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Why a file with patch candidates has no verified snapshot to compare its methods against.
    /// Each kind has its own wording, because only one of them is something a compile fixes.
    /// </summary>
    internal enum HotReloadMissingBaselineKind
    {
        IntroducedType = 0,
        NoVerifiedSourceSnapshot = 1,
        NoCompiledMethodBody = 2,
    }
}
