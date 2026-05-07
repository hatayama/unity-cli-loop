using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Dynamic Reference Set Builder operations for its owning module.
    /// </summary>
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
