using System;
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
        /// The added method's own name and its declaring type in metadata form, or empty when the
        /// registration did not carry them.
        /// </summary>
        public string MethodName { get; }

        public string DeclaringTypeMetadataName { get; }

        /// <summary>
        /// The static long field the shim increments each time its body starts, or null for a
        /// member a test built without one.
        /// </summary>
        public FieldInfo InvocationCounter { get; }

        public HotReloadAddedMemberInfo(
            string methodKey,
            string filePath,
            MethodInfo shimMethod,
            int sourceStartLine = 0,
            int sourceEndLine = 0,
            string methodName = null,
            string declaringTypeMetadataName = null,
            FieldInfo invocationCounter = null)
        {
            MethodKey = methodKey ?? string.Empty;
            FilePath = filePath ?? string.Empty;
            ShimMethod = shimMethod;
            SourceStartLine = sourceStartLine;
            SourceEndLine = sourceEndLine;
            MethodName = methodName ?? string.Empty;
            DeclaringTypeMetadataName = declaringTypeMetadataName ?? string.Empty;
            InvocationCounter = invocationCounter;
        }

        /// <summary>
        /// Whether a field can serve as an added member's invocation counter: a static long, the
        /// shape the worker declares beside every added-member shim.
        /// </summary>
        internal static bool IsReadableInvocationCounter(FieldInfo field)
        {
            return field != null && field.IsStatic && field.FieldType == typeof(long);
        }

        /// <summary>
        /// Calls that started the shim's body since this member was registered, read when asked so
        /// calls made after the registration are included.
        /// </summary>
        internal long ReadInvocationCount()
        {
            if (InvocationCounter == null)
            {
                throw new InvalidOperationException(
                    "The added member " + MethodKey + " was built without an invocation counter.");
            }

            // Why a plain read of a field the shim increments with Interlocked: the Editor is
            // 64-bit, where reading an aligned long never observes half of an increment.
            return (long)InvocationCounter.GetValue(null);
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
