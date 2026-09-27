using System;
using System.Collections.Generic;
using System.IO;

using Mono.Cecil;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Assembles the compilation references for an introduced-type artifact.
    /// </summary>
    internal static class HotReloadIntroducedTypeArtifactReferenceBuilder
    {
        /// <summary>
        /// Builds the references one artifact compiles against: the worker's raw references, or,
        /// when <paramref name="exposeInternals"/> is set, the same list with the target assembly
        /// and its retained artifacts replaced by copies whose internal members are public.
        /// </summary>
        /// <remarks>
        /// Why only the target and its retained artifacts: the declarations belong to the target
        /// assembly, while the internals of any other assembly stay out of reach exactly as in a
        /// regular compile. Why the input is copied and never edited: the worker runs and the shim
        /// compilation must keep binding against the raw references. Exposed copies read the
        /// project through Unity APIs, so an exposing build has to run on the main thread.
        /// </remarks>
        internal static HotReloadArtifactCompileReferences Build(
            TransformWorkerInputDto transformInput,
            HotReloadTypeHome targetHome,
            HotReloadDomain domain,
            string projectRoot,
            bool exposeInternals)
        {
            ValidateBuildArguments(transformInput, targetHome, domain, projectRoot);
            if (!exposeInternals)
            {
                return HotReloadArtifactCompileReferences.Raw(BuildRawReferencePaths(transformInput));
            }

            List<string> references = new List<string>(transformInput.referencePaths);
            int targetIndex = FindTargetReferenceIndex(references, targetHome);
            List<HotReloadTypeHome> retainedHomes = new List<HotReloadTypeHome>();
            List<string> unresolvedArtifactPaths = new List<string>();
            PartitionArtifactRecords(
                transformInput.introducedTypeArtifacts,
                domain,
                projectRoot,
                retainedHomes,
                unresolvedArtifactPaths);

            // Why the artifact directories join the search: a retained artifact references the target
            // and the other artifacts of the domain, and Cecil resolves them while writing a copy.
            IReadOnlyCollection<string> searchDirectories = HotReloadShimReferenceBuilder.CollectArtifactSearchDirectories(
                retainedHomes,
                ReferencePublicizer.CollectResolverSearchDirectories(references));

            // Why only a resolution failure becomes a result: Cecil could not find an assembly a copy
            // needs, which fails this preparation like any other unusable reference. A missing file or
            // unreadable metadata still escapes, because compiling the raw references instead would
            // hand the grant an artifact built against different references.
            try
            {
                references[targetIndex] = ReferencePublicizer.GetOrCreateInternalsExposedCopy(
                    targetHome,
                    searchDirectories);
                foreach (HotReloadTypeHome retainedHome in retainedHomes)
                {
                    HotReloadShimReferenceBuilder.AppendIfMissingByFullPath(
                        references,
                        ReferencePublicizer.GetOrCreateInternalsExposedCopy(retainedHome, searchDirectories));
                }

                foreach (string unresolvedArtifactPath in unresolvedArtifactPaths)
                {
                    HotReloadShimReferenceBuilder.AppendIfMissingByFullPath(references, unresolvedArtifactPath);
                }
            }
            catch (AssemblyResolutionException resolutionException)
            {
                return HotReloadArtifactCompileReferences.Failed(
                    "Exposing internal members of the referenced assemblies failed: " + resolutionException.Message);
            }

            return HotReloadArtifactCompileReferences.Exposed(references);
        }

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

        private static void ValidateBuildArguments(
            TransformWorkerInputDto transformInput,
            HotReloadTypeHome targetHome,
            HotReloadDomain domain,
            string projectRoot)
        {
            if (transformInput == null)
            {
                throw new ArgumentNullException(nameof(transformInput));
            }

            if (targetHome == null)
            {
                throw new ArgumentNullException(nameof(targetHome));
            }

            if (domain == null)
            {
                throw new ArgumentNullException(nameof(domain));
            }

            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new ArgumentException("projectRoot must not be null or empty.", nameof(projectRoot));
            }
        }

        // Why the rule that deduplicates references: the worker list holds the target as Unity
        // spelled it, and a spelling this rule accepts must not leave the raw target beside its copy.
        private static int FindTargetReferenceIndex(List<string> references, HotReloadTypeHome targetHome)
        {
            string targetPath = Path.GetFullPath(targetHome.DllPath);
            int targetIndex = HotReloadShimReferenceBuilder.IndexOfFullPath(references, targetPath);
            if (targetIndex < 0)
            {
                throw new InvalidOperationException(
                    "The worker references must hold the target assembly before its internal members can be exposed: "
                    + targetPath);
            }

            return targetIndex;
        }

        // Why the records the raw references would skip are skipped here too: exposure must change
        // how an artifact is referenced, never which artifacts are.
        private static void PartitionArtifactRecords(
            TransformWorkerIntroducedTypeArtifactDto[] records,
            HotReloadDomain domain,
            string projectRoot,
            List<HotReloadTypeHome> retainedHomes,
            List<string> unresolvedArtifactPaths)
        {
            if (records == null)
            {
                return;
            }

            foreach (TransformWorkerIntroducedTypeArtifactDto record in records)
            {
                if (record == null || string.IsNullOrEmpty(record.referencePath))
                {
                    continue;
                }

                HotReloadTypeHome retainedHome = HotReloadShimReferenceBuilder.ResolveRetainedArtifactHome(
                    domain,
                    projectRoot,
                    record);
                if (retainedHome == null)
                {
                    unresolvedArtifactPaths.Add(Path.GetFullPath(record.referencePath));
                    continue;
                }

                retainedHomes.Add(retainedHome);
            }
        }
    }
}
