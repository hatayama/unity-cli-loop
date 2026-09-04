namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host type for cross-file worker tests. Tests copy this source, add members to the
    /// copy, and expect a body edited in the sibling caller file to bind against them.
    /// </summary>
    internal sealed class HotReloadCrossFileAddedMemberHost
    {
        public int Value()
        {
            return 1;
        }
    }
}
