namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One of Unity's compile references of the edited assembly, resolved against the project root,
    /// and whether the shim compile binds a rewritten copy of it instead of the file itself.
    /// </summary>
    internal readonly struct ShimCompileReference
    {
        internal ShimCompileReference(string fullPath, bool usesRewrittenCopy)
        {
            FullPath = fullPath;
            UsesRewrittenCopy = usesRewrittenCopy;
        }

        internal string FullPath { get; }

        internal bool UsesRewrittenCopy { get; }
    }
}
