using System.Collections.Generic;

namespace io.github.hatayama.uLoopMCP
{
    internal sealed class DynamicReferenceSetBuilderService
    {
        public List<string> BuildReferenceSet(
            List<string> additionalReferences,
            IReadOnlyCollection<string> resolvedAssemblyReferences,
            ExternalCompilerPaths externalCompilerPaths)
        {
            return DynamicReferenceSetBuilder.BuildReferenceSet(
                additionalReferences,
                resolvedAssemblyReferences,
                externalCompilerPaths);
        }

        public string[] MergeReferencesByAssemblyName(
            string[] baseReferences,
            List<string> additionalReferences)
        {
            return DynamicReferenceSetBuilder.MergeReferencesByAssemblyName(baseReferences, additionalReferences);
        }
    }
}
