using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the retained-artifact records one worker run may bind against: the artifacts still
    /// active for the generation this run targets, plus the artifact this run just prepared.
    /// </summary>
    internal static class HotReloadIntroducedTypeArtifactRecords
    {
        /// <summary>
        /// Collects the records of the artifacts already active for the run's target generation.
        /// An artifact of another generation is left out, because its types were normalized back
        /// to an assembly this run no longer edits.
        /// </summary>
        /// <param name="findFullyAppliedSourceHash">
        /// The hash the last reload recorded for a project-relative file when it applied all of
        /// it, or null.
        /// </param>
        public static List<TransformWorkerIntroducedTypeArtifactDto> CollectActive(
            HotReloadIntroducedTypeRegistry registry,
            string targetAssemblyName,
            string targetAssemblyMvid,
            Func<string, string> findFullyAppliedSourceHash)
        {
            List<TransformWorkerIntroducedTypeArtifactDto> records =
                new List<TransformWorkerIntroducedTypeArtifactDto>();
            IReadOnlyList<HotReloadIntroducedTypeArtifact> active = registry.CollectActiveArtifactsForTarget(
                targetAssemblyName,
                targetAssemblyMvid);
            foreach (HotReloadIntroducedTypeArtifact artifact in active)
            {
                TransformWorkerIntroducedTypeArtifactDto record = CreateRecord(artifact);
                AttachOwnerAppliedSourceHashes(findFullyAppliedSourceHash, record);
                records.Add(record);
            }

            return records;
        }

        // The worker compares the hash with the bytes it reads, so a source that still matches is
        // known to hold only what earlier reloads applied.
        private static void AttachOwnerAppliedSourceHashes(
            Func<string, string> findFullyAppliedSourceHash,
            TransformWorkerIntroducedTypeArtifactDto record)
        {
            foreach (TransformWorkerIntroducedTypeArtifactTypeDto type in record.types)
            {
                if (string.IsNullOrEmpty(type.ownerProjectRelativePath))
                {
                    continue;
                }

                type.ownerAppliedSourceHash = findFullyAppliedSourceHash(type.ownerProjectRelativePath);
            }
        }

        public static TransformWorkerIntroducedTypeArtifactDto CreateRecord(
            HotReloadIntroducedTypeArtifact artifact)
        {
            TransformWorkerIntroducedTypeArtifactTypeDto[] types =
                new TransformWorkerIntroducedTypeArtifactTypeDto[artifact.Descriptors.Count];
            for (int index = 0; index < artifact.Descriptors.Count; index++)
            {
                HotReloadIntroducedTypeDescriptor descriptor = artifact.Descriptors[index];
                types[index] = new TransformWorkerIntroducedTypeArtifactTypeDto
                {
                    metadataName = descriptor.MetadataName.Value,
                    originalAssemblyName = descriptor.OriginalAssemblyName,
                    originalAssemblyMvid = descriptor.OriginalAssemblyMvid,
                    ownerProjectRelativePath = descriptor.OwnerProjectRelativePath,
                    declarationFingerprint = descriptor.DeclarationFingerprint
                };
            }

            return new TransformWorkerIntroducedTypeArtifactDto
            {
                assemblyFullName = artifact.AssemblyFullName,
                referencePath = artifact.DllPath,
                types = types
            };
        }
    }
}
