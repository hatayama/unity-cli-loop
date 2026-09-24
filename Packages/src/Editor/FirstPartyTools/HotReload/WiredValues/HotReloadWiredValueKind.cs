namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// How the ledger remembers one wired value.
    /// </summary>
    internal enum HotReloadWiredValueKind
    {
        Plain,
        SceneObject,
        Asset,
        Unrestorable,
    }
}
