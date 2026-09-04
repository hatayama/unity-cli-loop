namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled caller type for cross-file worker tests. Tests edit a copy of this body so it
    /// calls a member added in the sibling host file within the same worker run.
    /// </summary>
    internal sealed class HotReloadCrossFileAddedMemberCaller
    {
        public int Call(HotReloadCrossFileAddedMemberHost host)
        {
            return host.Value();
        }
    }
}
