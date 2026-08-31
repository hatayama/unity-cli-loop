namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled referencing type used as the --files target while the sibling const holder
    /// is the file that actually drifted.
    /// </summary>
    public static class HotReloadSiblingConstUser
    {
        public static int ReadSiblingTuning()
        {
            return HotReloadSiblingConstDefinitions.SiblingTuning;
        }
    }
}
