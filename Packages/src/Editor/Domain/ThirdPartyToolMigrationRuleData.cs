using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Describes a removed legacy PlayerLoopTiming argument set for cross-file migration.
    /// </summary>
    public readonly struct RemovedLegacyPlayerLoopTimingSignature
    {
        public RemovedLegacyPlayerLoopTimingSignature(
            string methodName,
            string declaringTypeName,
            LegacyPlayerLoopTimingParameterDeclaration[] originalParameters,
            RemovedLegacyPlayerLoopTimingParameter[] removedParameters)
        {
            Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be null or empty");
            Debug.Assert(declaringTypeName != null, "declaringTypeName must not be null");
            Debug.Assert(originalParameters != null, "originalParameters must not be null");
            Debug.Assert(removedParameters != null, "removedParameters must not be null");

            MethodName = methodName;
            DeclaringTypeName = declaringTypeName;
            OriginalParameters =
                originalParameters ?? throw new ArgumentNullException(nameof(originalParameters));
            RemovedParameters =
                removedParameters ?? throw new ArgumentNullException(nameof(removedParameters));
        }

        public string MethodName { get; }
        public string DeclaringTypeName { get; }
        public LegacyPlayerLoopTimingParameterDeclaration[] OriginalParameters { get; }
        public RemovedLegacyPlayerLoopTimingParameter[] RemovedParameters { get; }
    }

    /// <summary>
    /// Describes one original PlayerLoopTiming method parameter.
    /// </summary>
    public readonly struct LegacyPlayerLoopTimingParameterDeclaration
    {
        public LegacyPlayerLoopTimingParameterDeclaration(
            int index,
            string typeName,
            string name,
            bool hasDefaultValue)
        {
            Debug.Assert(index >= 0, "index must not be negative");
            Debug.Assert(!string.IsNullOrEmpty(typeName), "typeName must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(name), "name must not be null or empty");

            Index = index;
            TypeName = typeName;
            Name = name;
            HasDefaultValue = hasDefaultValue;
        }

        public int Index { get; }
        public string TypeName { get; }
        public string Name { get; }
        public bool HasDefaultValue { get; }
    }

    /// <summary>
    /// Describes one removed legacy PlayerLoopTiming method parameter.
    /// </summary>
    public readonly struct RemovedLegacyPlayerLoopTimingParameter
    {
        public RemovedLegacyPlayerLoopTimingParameter(int index, string name)
        {
            Debug.Assert(index >= 0, "index must not be negative");
            Debug.Assert(!string.IsNullOrEmpty(name), "name must not be null or empty");

            Index = index;
            Name = name;
        }

        public int Index { get; }
        public string Name { get; }
    }

    /// <summary>
    /// Describes rewritten source content and collected timing migration metadata.
    /// </summary>
    public readonly struct ThirdPartyToolMigrationContentResult
    {
        public ThirdPartyToolMigrationContentResult(
            string content,
            int replacementCount,
            RemovedLegacyPlayerLoopTimingSignature[] removedPlayerLoopTimingSignatures)
        {
            Debug.Assert(content != null, "content must not be null");
            Debug.Assert(replacementCount >= 0, "replacementCount must not be negative");
            Debug.Assert(
                removedPlayerLoopTimingSignatures != null,
                "removedPlayerLoopTimingSignatures must not be null");

            Content = content ?? throw new ArgumentNullException(nameof(content));
            ReplacementCount = replacementCount;
            RemovedPlayerLoopTimingSignatures =
                removedPlayerLoopTimingSignatures ??
                throw new ArgumentNullException(nameof(removedPlayerLoopTimingSignatures));
        }

        public string Content { get; }
        public int ReplacementCount { get; }
        public RemovedLegacyPlayerLoopTimingSignature[] RemovedPlayerLoopTimingSignatures { get; }
        public bool Changed => ReplacementCount > 0;
    }

    /// <summary>
    /// Describes migrated asmdef references resolved by pure migration policy.
    /// </summary>
    public readonly struct ThirdPartyToolMigrationAsmdefReferenceMigrationResult
    {
        public ThirdPartyToolMigrationAsmdefReferenceMigrationResult(
            string[] references,
            int replacementCount)
        {
            Debug.Assert(references != null, "references must not be null");
            Debug.Assert(replacementCount >= 0, "replacementCount must not be negative");

            References = references ?? throw new ArgumentNullException(nameof(references));
            ReplacementCount = replacementCount;
        }

        public string[] References { get; }
        public int ReplacementCount { get; }
        public bool Changed => ReplacementCount > 0;
    }
}
