using System;
using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The boundary checks a transform-worker request and its preparation output must pass before
    /// the client acts on them.
    /// </summary>
    internal sealed class TransformWorkerOutputValidator
    {
        /// <summary>
        /// Rejects retained-artifact records the worker could not act on faithfully: an incomplete
        /// record, a run that names no target assembly to normalize back to, or two records that
        /// claim the same assembly identity or the same type inside it.
        /// </summary>
        internal bool TryValidateIntroducedTypeArtifacts(
            TransformWorkerInputDto input,
            out string errorMessage)
        {
            if (input.introducedTypeArtifacts == null || input.introducedTypeArtifacts.Length == 0)
            {
                errorMessage = string.Empty;
                return true;
            }

            // The records normalize a retained type back to the assembly its source belongs to,
            // and that is the assembly this run targets.
            if (string.IsNullOrWhiteSpace(input.targetAssemblyName)
                || string.IsNullOrWhiteSpace(input.targetAssemblyMvid))
            {
                errorMessage = "A run that carries retained artifacts must name the target assembly and its module version id.";
                return false;
            }

            HashSet<string> assemblyFullNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> typeKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerIntroducedTypeArtifactDto artifact in input.introducedTypeArtifacts)
            {
                if (artifact == null
                    || string.IsNullOrWhiteSpace(artifact.assemblyFullName)
                    || string.IsNullOrWhiteSpace(artifact.referencePath)
                    || artifact.types == null
                    || artifact.types.Length == 0)
                {
                    errorMessage = "A retained artifact must name its assembly, its reference path and at least one type.";
                    return false;
                }

                // Two records claiming one identity would put the same assembly into the
                // compilation twice, and the worker would then resolve one of them to nothing.
                if (!assemblyFullNames.Add(artifact.assemblyFullName))
                {
                    errorMessage = "Two retained artifacts claim the assembly identity " + artifact.assemblyFullName + ".";
                    return false;
                }

                if (!TryValidateIntroducedTypeArtifactTypes(artifact, typeKeys, out errorMessage))
                {
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
        }

        private bool TryValidateIntroducedTypeArtifactTypes(
            TransformWorkerIntroducedTypeArtifactDto artifact,
            HashSet<string> typeKeys,
            out string errorMessage)
        {
            foreach (TransformWorkerIntroducedTypeArtifactTypeDto artifactType in artifact.types)
            {
                // Without the owner and the fingerprint the worker cannot tell whether the edited
                // source still produces the declaration the artifact holds, so it would leave the
                // declaration in place and bind the type from source after all.
                if (artifactType == null
                    || string.IsNullOrWhiteSpace(artifactType.metadataName)
                    || string.IsNullOrWhiteSpace(artifactType.originalAssemblyName)
                    || string.IsNullOrWhiteSpace(artifactType.originalAssemblyMvid)
                    || string.IsNullOrWhiteSpace(artifactType.ownerProjectRelativePath)
                    || string.IsNullOrWhiteSpace(artifactType.declarationFingerprint))
                {
                    errorMessage = "A retained type must carry its metadata name, its original identity, its owner and its fingerprint.";
                    return false;
                }

                if (!typeKeys.Add(artifact.assemblyFullName + "|" + artifactType.metadataName))
                {
                    errorMessage = "A retained artifact lists " + artifactType.metadataName + " more than once.";
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
        }

        internal bool TryValidateRequiredPreparationOutput(
            TransformWorkerInputDto input,
            TransformWorkerOutputDto output,
            out string errorMessage)
        {
            if (!string.Equals(input.operation, "prepareIntroducedTypes", StringComparison.Ordinal))
            {
                errorMessage = string.Empty;
                return true;
            }

            // The descriptors repeat the requested identity, so a request that carries none would
            // produce descriptors that pass the "matches its input" check with an identity no
            // retained artifact can be attributed to, and fail far from here when the descriptor
            // is constructed.
            if (string.IsNullOrWhiteSpace(input.targetAssemblyName)
                || string.IsNullOrWhiteSpace(input.targetAssemblyMvid))
            {
                errorMessage = "Preparation input must name the target assembly and its module version id.";
                return false;
            }

            if (output.files == null)
            {
                errorMessage = "Preparation output must contain files.";
                return false;
            }

            // Why the keys are built once for the whole output and not per file: a reuse may only
            // name a type a retained artifact of this very run holds, and the same reuse must not
            // be reported twice across the run, which no single file can see on its own.
            HashSet<string> retainedTypeKeys = CollectRetainedTypeKeys(input);
            HashSet<string> reportedReuseKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerFileOutputDto file in output.files)
            {
                if (!TryValidatePreparationFile(
                        file,
                        input,
                        retainedTypeKeys,
                        reportedReuseKeys,
                        out errorMessage))
                {
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
        }

        private HashSet<string> CollectRetainedTypeKeys(TransformWorkerInputDto input)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            if (input.introducedTypeArtifacts == null)
            {
                return keys;
            }

            foreach (TransformWorkerIntroducedTypeArtifactDto artifact in input.introducedTypeArtifacts)
            {
                if (artifact?.types == null)
                {
                    continue;
                }

                foreach (TransformWorkerIntroducedTypeArtifactTypeDto type in artifact.types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    keys.Add(
                        BuildRetainedTypeKey(
                            type.metadataName,
                            type.originalAssemblyName,
                            type.originalAssemblyMvid));
                }
            }

            return keys;
        }

        private string BuildRetainedTypeKey(
            string metadataName,
            string originalAssemblyName,
            string originalAssemblyMvid)
        {
            return metadataName + "|" + originalAssemblyName + "|" + originalAssemblyMvid;
        }

        private bool TryValidatePreparationFile(
            TransformWorkerFileOutputDto file,
            TransformWorkerInputDto input,
            HashSet<string> retainedTypeKeys,
            HashSet<string> reportedReuseKeys,
            out string errorMessage)
        {
            if (file == null || file.introducedTypes == null || file.introducedTypeDiagnostics == null)
            {
                errorMessage = "Preparation output must contain introducedTypes and introducedTypeDiagnostics.";
                return false;
            }

            foreach (TransformWorkerIntroducedTypeDto introducedType in file.introducedTypes)
            {
                if (!TryValidatePreparationDescriptor(introducedType, file, input, out errorMessage))
                {
                    return false;
                }
            }

            // Why omission is refused rather than coalesced: an omitted list reads the same as a
            // run that reused nothing, and a reload would then report a type it bound from an
            // active artifact as introduced by no one.
            if (file.introducedTypeReuses == null)
            {
                errorMessage = "Preparation output must contain introducedTypeReuses.";
                return false;
            }

            foreach (TransformWorkerIntroducedTypeReuseDto reuse in file.introducedTypeReuses)
            {
                if (!TryValidatePreparationReuse(
                        reuse,
                        input,
                        retainedTypeKeys,
                        reportedReuseKeys,
                        out errorMessage))
                {
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
        }

        // Why a reuse is refused unless a retained artifact of this run holds the type: a reuse row
        // becomes an AlreadyActive row of the response and takes the declaration out of the tree
        // the transform binds against, so a name the run cannot account for would report a binding
        // against an assembly this domain never retained.
        private bool TryValidatePreparationReuse(
            TransformWorkerIntroducedTypeReuseDto reuse,
            TransformWorkerInputDto input,
            HashSet<string> retainedTypeKeys,
            HashSet<string> reportedReuseKeys,
            out string errorMessage)
        {
            if (reuse == null)
            {
                errorMessage = "Preparation output must not contain a null introduced type reuse.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(reuse.metadataName))
            {
                errorMessage = "Preparation reuse must name the type it bound.";
                return false;
            }

            if (reuse.originalAssemblyName != input.targetAssemblyName
                || reuse.originalAssemblyMvid != input.targetAssemblyMvid)
            {
                errorMessage = "Preparation reuse assembly identity must match its input.";
                return false;
            }

            string key = BuildRetainedTypeKey(
                reuse.metadataName,
                reuse.originalAssemblyName,
                reuse.originalAssemblyMvid);
            if (!retainedTypeKeys.Contains(key))
            {
                errorMessage = "Preparation reuse must name a type a retained artifact of this run holds.";
                return false;
            }

            if (!reportedReuseKeys.Add(key))
            {
                errorMessage = "Preparation output must not report the same reuse more than once.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private bool TryValidatePreparationDescriptor(
            TransformWorkerIntroducedTypeDto introducedType,
            TransformWorkerFileOutputDto file,
            TransformWorkerInputDto input,
            out string errorMessage)
        {
            if (introducedType == null)
            {
                errorMessage = "Preparation output must not contain a null introduced type descriptor.";
                return false;
            }

            if (introducedType.ownerProjectRelativePath == null
                || introducedType.ownerProjectRelativePath != file.projectRelativePath)
            {
                errorMessage = "Preparation descriptor owner must match its file output.";
                return false;
            }

            if (introducedType.originalAssemblyName != input.targetAssemblyName
                || introducedType.originalAssemblyMvid != input.targetAssemblyMvid)
            {
                errorMessage = "Preparation descriptor assembly identity must match its input.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(introducedType.metadataName)
                || string.IsNullOrWhiteSpace(introducedType.declarationFingerprint)
                || string.IsNullOrWhiteSpace(introducedType.source))
            {
                errorMessage = "Preparation descriptor metadataName, declarationFingerprint, and source are required.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
    }
}
