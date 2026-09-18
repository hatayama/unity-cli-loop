using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One added-method shim recorded for --status Kind "Added".
    /// </summary>
    internal sealed class HotReloadAddedMemberInfo
    {
        public string MethodKey { get; }

        public string FilePath { get; }

        public MethodInfo ShimMethod { get; }

        /// <summary>
        /// 1-based first and last source lines of the added method in the file as it was reloaded,
        /// both inclusive, or 0 when the range is unknown (a range of 0 never contains a line).
        /// </summary>
        public int SourceStartLine { get; }

        public int SourceEndLine { get; }

        /// <summary>
        /// The compiled project assembly of the added method's file, or empty when an
        /// introduced-type artifact serves its type and no compiled assembly does.
        /// </summary>
        public string CompiledAssemblyPath { get; }

        public HotReloadAddedMemberInfo(
            string methodKey,
            string filePath,
            MethodInfo shimMethod,
            int sourceStartLine = 0,
            int sourceEndLine = 0,
            string compiledAssemblyPath = null)
        {
            MethodKey = methodKey ?? string.Empty;
            FilePath = filePath ?? string.Empty;
            ShimMethod = shimMethod;
            SourceStartLine = sourceStartLine;
            SourceEndLine = sourceEndLine;
            CompiledAssemblyPath = compiledAssemblyPath ?? string.Empty;
        }

        internal bool ContainsSourceLine(int line)
        {
            return SourceStartLine > 0 && SourceStartLine <= line && line <= SourceEndLine;
        }
    }
}
