namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A file with no method body, so the compiled PDB has no document for it. Used to check how a
    /// missing source baseline is explained for such files.
    /// </summary>
    public enum HotReloadSnapshotBodylessFixture
    {
        First,
        Second
    }
}
