namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// The value a registry hands to its handlers. A worker run that includes this file declares the
    /// type a second time from source, beside the compiled one the registry's API refers to.
    /// </summary>
    public sealed class HotReloadBindingSplitPayload
    {
        public int Value;

        // A body a reload can patch, so this file stays active and comes back into a later reload
        // of the assembly as a sibling.
        public int Scaled()
        {
            return Value * 2;
        }
    }
}
