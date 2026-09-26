using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Assembles the compilation references for an introduced-type artifact.
    /// </summary>
    internal static class HotReloadIntroducedTypeArtifactReferenceBuilder
    {
        // Why the active artifacts as well: a declaration this run introduces may name a type an
        // earlier reload introduced, which lives in neither the compiled assembly nor the sources
        // of this run. Only the assemblies those reloads retained can supply it.
        internal static List<string> BuildRawReferencePaths(TransformWorkerInputDto transformInput)
        {
            List<string> referencePaths = new List<string>(transformInput.referencePaths);
            HotReloadShimReferenceBuilder.AppendIntroducedTypeArtifactReferences(
                referencePaths,
                transformInput.introducedTypeArtifacts);
            return referencePaths;
        }
    }
}
