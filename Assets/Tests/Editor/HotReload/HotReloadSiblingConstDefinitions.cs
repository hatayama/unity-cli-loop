namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled const holder used to assert sibling const-drift warnings when only a
    /// referencing file is passed to hot reload.
    /// </summary>
    public static class HotReloadSiblingConstDefinitions
    {
        public const int SiblingTuning = 6;

        // Why typed through the global using alone: a changed sibling naming this const has a
        // value only in a compilation that carries the assembly's global usings.
        public const HotReloadGlobalUsingMode SiblingMode = HotReloadGlobalUsingMode.First;
    }
}
