namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// Compiled holder type that exposes the host through a property. It is never passed to a
    /// reload, so a body reaching the host through it sees the compiled (metadata) host type.
    internal sealed class HotReloadCrossFileAddedMemberHolder
    {
        public HotReloadCrossFileAddedMemberHost Host { get; } = new HotReloadCrossFileAddedMemberHost();
    }
}
