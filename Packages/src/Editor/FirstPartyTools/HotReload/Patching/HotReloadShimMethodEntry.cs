using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One compiled method's shim registration inside a file's shim generation: the shim that
    /// replaces it and the source span the shim was compiled from.
    /// </summary>
    internal sealed class HotReloadShimMethodEntry
    {
        internal HotReloadShimMethodEntry(
            MethodBase shimMethod,
            bool isDelegation,
            int sourceStartLine,
            int sourceEndLine)
        {
            ShimMethod = shimMethod;
            IsDelegation = isDelegation;
            SourceStartLine = sourceStartLine;
            SourceEndLine = sourceEndLine;
        }

        internal MethodBase ShimMethod { get; }

        internal bool IsDelegation { get; }

        internal int SourceStartLine { get; }

        internal int SourceEndLine { get; }
    }
}
