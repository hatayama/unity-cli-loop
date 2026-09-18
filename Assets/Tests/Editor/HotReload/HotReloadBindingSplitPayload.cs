namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// The value a registry hands to its handlers. A worker run that includes this file declares the
    /// type a second time from source, beside the compiled one the registry's API refers to.
    /// </summary>
    public sealed class HotReloadBindingSplitPayload
    {
        public int Value;
    }
}
