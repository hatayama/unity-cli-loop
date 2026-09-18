namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A type a test adds a method to that registers a lambda calling a private method, which only
    /// works through an accessor rewrite because the added method runs outside this type.
    /// </summary>
    public sealed class HotReloadBindingSplitHost
    {
        private readonly HotReloadBindingSplitRegistry _registry = new HotReloadBindingSplitRegistry();
        private int _handled;

        public int Handled => _handled;

        public HotReloadBindingSplitRegistry Registry => _registry;

        private void Handle(HotReloadBindingSplitPayload payload)
        {
            _handled += payload.Value;
        }
    }
}
