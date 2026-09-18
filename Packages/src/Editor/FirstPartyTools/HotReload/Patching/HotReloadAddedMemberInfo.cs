using System.Reflection;

using io.github.hatayama.UnityCliLoop.ToolContracts;

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

        /// <summary>
        /// The added method's own name and its declaring type in metadata form, or empty when the
        /// registration did not carry them.
        /// </summary>
        public string MethodName { get; }

        public string DeclaringTypeMetadataName { get; }

        public HotReloadAddedMemberInfo(
            string methodKey,
            string filePath,
            MethodInfo shimMethod,
            int sourceStartLine = 0,
            int sourceEndLine = 0,
            string compiledAssemblyPath = null,
            string methodName = null,
            string declaringTypeMetadataName = null)
        {
            MethodKey = methodKey ?? string.Empty;
            FilePath = filePath ?? string.Empty;
            ShimMethod = shimMethod;
            SourceStartLine = sourceStartLine;
            SourceEndLine = sourceEndLine;
            CompiledAssemblyPath = compiledAssemblyPath ?? string.Empty;
            MethodName = methodName ?? string.Empty;
            DeclaringTypeMetadataName = declaringTypeMetadataName ?? string.Empty;
        }

        /// <summary>
        /// The name parts pause point matches its --method filter against, spelled as the compiled
        /// resolver spells them.
        /// </summary>
        internal HotReloadAddedMethodAtLine ToAddedMethodAtLine()
        {
            (string declaringTypeName, string nestedOuterTypeName) =
                new HotReloadMetadataTypeName(DeclaringTypeMetadataName).ToShortNestingNames();
            return new HotReloadAddedMethodAtLine(MethodKey, MethodName, declaringTypeName, nestedOuterTypeName);
        }

        internal bool ContainsSourceLine(int line)
        {
            return SourceStartLine > 0 && SourceStartLine <= line && line <= SourceEndLine;
        }
    }
}
