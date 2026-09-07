using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compiles the types one run introduces into a retained artifact before the transform run, so
    /// the transform and the shim compilation can bind them from a loaded assembly instead of the
    /// edited source that still declares them.
    /// </summary>
    internal static class HotReloadIntroducedTypePreparation
    {
        // The artifact directory has to stay unique per domain, or a second reload would emit over
        // the assembly files a loaded artifact of this domain still maps.
        private static readonly string SessionId = Guid.NewGuid().ToString("N");

        public static async Task<HotReloadIntroducedTypePreparationResult> PrepareAsync(
            IReadOnlyList<HotReloadGroupFile> files,
            TransformWorkerInputDto transformInput,
            CancellationToken ct)
        {
            Debug.Assert(files != null && files.Count > 0, "A group must hold a file.");
            Debug.Assert(transformInput != null, "The preparation reuses the transform input.");

            TransformWorkerClientResult prepareResult = await TransformWorkerClient.RunAsync(
                BuildPrepareInput(transformInput),
                ct).ConfigureAwait(false);
            if (!prepareResult.Success)
            {
                return HotReloadIntroducedTypePreparationResult.Failure(
                    "Introduced-type preparation failed: " + prepareResult.ErrorMessage);
            }

            string refusedDeclaration = FindChangedIntroducedTypeDiagnostic(prepareResult.Output);
            if (refusedDeclaration != null)
            {
                return HotReloadIntroducedTypePreparationResult.Failure(refusedDeclaration);
            }

            List<HotReloadIntroducedTypeDescriptor> descriptors = CollectDescriptors(prepareResult.Output);
            if (descriptors.Count == 0)
            {
                return HotReloadIntroducedTypePreparationResult.NoIntroducedTypes();
            }

            string doubleDeclaration = FindDoubleDeclaredType(descriptors);
            if (doubleDeclaration != null)
            {
                return HotReloadIntroducedTypePreparationResult.Failure(doubleDeclaration);
            }

            HotReloadIntroducedTypeArtifactPaths paths =
                new HotReloadIntroducedTypeArtifactPathFactory(files[0].ProjectRoot, SessionId).Create();
            HotReloadIntroducedTypeCompilerResult compileResult =
                await new HotReloadIntroducedTypeCompiler(new HotReloadIntroducedTypeCompilerEnvironment())
                    .CompileAsync(
                        HotReloadIntroducedTypeCompilationRequest.CreateBatch(
                            paths,
                            descriptors,
                            BuildArtifactReferencePaths(transformInput),
                            transformInput.defines),
                        ct)
                    .ConfigureAwait(false);
            if (!compileResult.Success)
            {
                return HotReloadIntroducedTypePreparationResult.Failure(
                    "Introduced-type compilation failed: " + compileResult.ErrorMessage);
            }

            return HotReloadIntroducedTypePreparationResult.WithPrepared(
                new HotReloadPreparedIntroducedTypes(
                    compileResult.Artifact,
                    CollectOwnerSourceHashes(prepareResult.Output, descriptors)));
        }

        // Why the active artifacts as well: a declaration this run introduces may name a type an
        // earlier reload introduced, which lives in neither the compiled assembly nor the sources
        // of this run. Only the assemblies those reloads retained can supply it.
        private static List<string> BuildArtifactReferencePaths(TransformWorkerInputDto transformInput)
        {
            List<string> referencePaths = new List<string>(transformInput.referencePaths);
            HotReloadShimReferenceBuilder.AppendIntroducedTypeArtifactReferences(
                referencePaths,
                transformInput.introducedTypeArtifacts);
            return referencePaths;
        }

        // Why the same sources and references: preparation asks the worker which declarations of
        // this very run are new, so anything that changes what the run compiles against would make
        // it plan a type the transform run then sees differently.
        private static TransformWorkerInputDto BuildPrepareInput(TransformWorkerInputDto transformInput)
        {
            return new TransformWorkerInputDto
            {
                operation = "prepareIntroducedTypes",
                sources = transformInput.sources,
                defines = transformInput.defines,
                referencePaths = transformInput.referencePaths,
                targetTypesAssemblyPath = transformInput.targetTypesAssemblyPath,
                targetAssemblyName = transformInput.targetAssemblyName,
                targetAssemblyMvid = transformInput.targetAssemblyMvid,
                assemblySourcePaths = transformInput.assemblySourcePaths,
                changedSiblingSourcePaths = transformInput.changedSiblingSourcePaths,
                introducedTypeArtifacts = transformInput.introducedTypeArtifacts
            };
        }

        // Why only the owners: the commit boundary compares what the artifact was compiled from
        // against what the transform run read, and only the files that declare an introduced type
        // took part in that compilation.
        private static Dictionary<string, string> CollectOwnerSourceHashes(
            TransformWorkerOutputDto output,
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors)
        {
            HashSet<string> ownerPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (HotReloadIntroducedTypeDescriptor descriptor in descriptors)
            {
                ownerPaths.Add(descriptor.OwnerProjectRelativePath);
            }

            Dictionary<string, string> hashesByOwnerPath =
                new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (TransformWorkerFileOutputDto file in output.files)
            {
                if (!ownerPaths.Contains(file.projectRelativePath))
                {
                    continue;
                }

                hashesByOwnerPath[file.projectRelativePath] = file.sourceContentSha256;
            }

            Debug.Assert(
                hashesByOwnerPath.Count == ownerPaths.Count,
                "Every owner of an introduced type must have a row in the preparation output.");
            return hashesByOwnerPath;
        }

        // Why this one diagnostic fails the run: every other unsupported declaration simply is not
        // introduced, and the source that declares it stays in the tree. A declaration this domain
        // already retains an assembly for is taken out of the tree either way, so continuing would
        // bind callers against the retained definition the edited source no longer declares.
        private static string FindChangedIntroducedTypeDiagnostic(TransformWorkerOutputDto output)
        {
            foreach (TransformWorkerFileOutputDto file in output.files)
            {
                foreach (string diagnostic in file.introducedTypeDiagnostics)
                {
                    if (diagnostic != null
                        && diagnostic.StartsWith(
                            HotReloadConstants.ChangedIntroducedTypeDiagnosticPrefix,
                            StringComparison.Ordinal))
                    {
                        return diagnostic;
                    }
                }
            }

            return null;
        }

        // Why refused here and not by the artifact batch: two files of one group declaring the
        // same type is an editing mistake the reload has to report against both files, while the
        // batch's uniqueness rule is an internal contract whose violation would throw out of the
        // run and leave the group with no result at all.
        private static string FindDoubleDeclaredType(
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors)
        {
            Dictionary<string, string> ownerPathByIdentity =
                new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (HotReloadIntroducedTypeDescriptor descriptor in descriptors)
            {
                string identity = descriptor.BuildIdentity();
                if (!ownerPathByIdentity.TryGetValue(identity, out string firstOwnerPath))
                {
                    ownerPathByIdentity[identity] = descriptor.OwnerProjectRelativePath;
                    continue;
                }

                return "Introduced type " + descriptor.MetadataName
                    + " is declared in more than one file of the group: "
                    + firstOwnerPath + " and " + descriptor.OwnerProjectRelativePath + ".";
            }

            return null;
        }

        private static List<HotReloadIntroducedTypeDescriptor> CollectDescriptors(
            TransformWorkerOutputDto output)
        {
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor>();
            foreach (TransformWorkerFileOutputDto file in output.files)
            {
                foreach (TransformWorkerIntroducedTypeDto introducedType in file.introducedTypes)
                {
                    descriptors.Add(
                        new HotReloadIntroducedTypeDescriptor(
                            introducedType.originalAssemblyName,
                            introducedType.originalAssemblyMvid,
                            introducedType.metadataName,
                            introducedType.ownerProjectRelativePath,
                            introducedType.declarationFingerprint,
                            introducedType.source));
                }
            }

            return descriptors;
        }
    }
}
