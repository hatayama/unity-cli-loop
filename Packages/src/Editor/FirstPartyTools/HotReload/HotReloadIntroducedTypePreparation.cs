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
            HotReloadGroupStageCollaborators collaborators,
            IReadOnlyList<HotReloadGroupFile> files,
            TransformWorkerInputDto transformInput,
            CancellationToken ct)
        {
            Debug.Assert(collaborators != null, "collaborators must not be null.");
            Debug.Assert(files != null && files.Count > 0, "A group must hold a file.");
            Debug.Assert(transformInput != null, "The preparation reuses the transform input.");

            TransformWorkerClientResult prepareResult = await collaborators.TransformWorkerClient.RunAsync(
                BuildPrepareInput(transformInput),
                ct).ConfigureAwait(false);
            if (!prepareResult.Success)
            {
                return HotReloadIntroducedTypePreparationResult.WorkerFailure(
                    "Introduced-type preparation failed: " + prepareResult.ErrorMessage);
            }

            // Why collected before the refusals are answered: one preparation covers every
            // declaration of the group, so what it observed about the declarations it did not
            // refuse is still true and a refusal that returned first would drop it.
            List<HotReloadIntroducedTypeOutcome> alreadyActiveTypes =
                CollectAlreadyActiveTypes(prepareResult.Output);
            List<HotReloadIntroducedTypeNotice> notices = CollectNotices(prepareResult.Output);
            List<HotReloadIntroducedTypeOutcome> refusedDeclarations = CollectRedefinedTypeFailures(
                prepareResult.Output,
                transformInput.targetAssemblyName);
            if (refusedDeclarations.Count > 0)
            {
                return HotReloadIntroducedTypePreparationResult.TypeFailures(
                    refusedDeclarations,
                    alreadyActiveTypes,
                    notices);
            }

            List<HotReloadIntroducedTypeDescriptor> descriptors = CollectDescriptors(prepareResult.Output);
            if (descriptors.Count == 0)
            {
                return HotReloadIntroducedTypePreparationResult.NoIntroducedTypes(alreadyActiveTypes, notices);
            }

            List<HotReloadIntroducedTypeOutcome> doubleDeclarations =
                CollectDoubleDeclaredTypeFailures(descriptors, transformInput.targetAssemblyName);
            if (doubleDeclarations.Count > 0)
            {
                return HotReloadIntroducedTypePreparationResult.TypeFailures(
                    doubleDeclarations,
                    alreadyActiveTypes,
                    notices);
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
                return HotReloadIntroducedTypePreparationResult.TypeFailures(
                    HotReloadIntroducedTypeCompileFailureOutcomes.Build(
                        compileResult,
                        descriptors,
                        transformInput.targetAssemblyName),
                    alreadyActiveTypes,
                    notices);
            }

            return HotReloadIntroducedTypePreparationResult.WithPrepared(
                new HotReloadPreparedIntroducedTypes(
                    compileResult.Artifact,
                    CollectOwnerSourceHashes(prepareResult.Output, descriptors)),
                alreadyActiveTypes,
                notices);
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
        // Why every diagnostic but the redefinition one: those declarations are simply not
        // introduced and their source stays in the tree, so the run continues and only has to
        // say what will keep not working until a compile.
        private static List<HotReloadIntroducedTypeNotice> CollectNotices(TransformWorkerOutputDto output)
        {
            List<HotReloadIntroducedTypeNotice> notices = new List<HotReloadIntroducedTypeNotice>();
            foreach (TransformWorkerFileOutputDto file in output.files)
            {
                foreach (string diagnostic in file.introducedTypeDiagnostics)
                {
                    if (diagnostic == null
                        || diagnostic.StartsWith(
                            HotReloadConstants.ChangedIntroducedTypeDiagnosticPrefix,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    notices.Add(new HotReloadIntroducedTypeNotice(file.projectRelativePath, diagnostic));
                }
            }

            return notices;
        }

        private static List<HotReloadIntroducedTypeOutcome> CollectRedefinedTypeFailures(
            TransformWorkerOutputDto output,
            string targetAssemblyName)
        {
            List<HotReloadIntroducedTypeOutcome> failures = new List<HotReloadIntroducedTypeOutcome>();
            foreach (TransformWorkerFileOutputDto file in output.files)
            {
                foreach (string diagnostic in file.introducedTypeDiagnostics)
                {
                    if (diagnostic == null
                        || !diagnostic.StartsWith(
                            HotReloadConstants.ChangedIntroducedTypeDiagnosticPrefix,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    failures.Add(
                        HotReloadIntroducedTypeOutcome.Failed(
                            diagnostic
                                .Substring(HotReloadConstants.ChangedIntroducedTypeDiagnosticPrefix.Length)
                                .Trim(),
                            targetAssemblyName,
                            file.projectRelativePath,
                            diagnostic));
                }
            }

            return failures;
        }

        // Why refused here and not by the artifact batch: two files of one group declaring the
        // same type is an editing mistake the reload has to report against both files, while the
        // batch's uniqueness rule is an internal contract whose violation would throw out of the
        // run and leave the group with no result at all.
        // Why one row per owner: the mistake is in every file that declares the type, and a report
        // that named only one of them would send the reader to a file that is correct on its own.
        // Why every repeated declaration and not the first: a group can declare one type in three
        // files, or double-declare two types, and a report that stopped at the first pair would
        // hide the rest until the reader fixed that pair and reloaded again.
        private static List<HotReloadIntroducedTypeOutcome> CollectDoubleDeclaredTypeFailures(
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors,
            string targetAssemblyName)
        {
            List<string> identityOrder = new List<string>();
            Dictionary<string, List<HotReloadIntroducedTypeDescriptor>> declarationsByIdentity =
                new Dictionary<string, List<HotReloadIntroducedTypeDescriptor>>(StringComparer.Ordinal);
            foreach (HotReloadIntroducedTypeDescriptor descriptor in descriptors)
            {
                string identity = descriptor.BuildIdentity();
                if (!declarationsByIdentity.TryGetValue(
                        identity,
                        out List<HotReloadIntroducedTypeDescriptor> declarations))
                {
                    declarations = new List<HotReloadIntroducedTypeDescriptor>();
                    declarationsByIdentity[identity] = declarations;
                    identityOrder.Add(identity);
                }

                declarations.Add(descriptor);
            }

            List<HotReloadIntroducedTypeOutcome> failures = new List<HotReloadIntroducedTypeOutcome>();
            foreach (string identity in identityOrder)
            {
                AppendDoubleDeclaredRows(failures, declarationsByIdentity[identity], targetAssemblyName);
            }

            return failures;
        }

        private static void AppendDoubleDeclaredRows(
            List<HotReloadIntroducedTypeOutcome> failures,
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> declarations,
            string targetAssemblyName)
        {
            if (declarations.Count < 2)
            {
                return;
            }

            List<string> ownerPaths = new List<string>(declarations.Count);
            foreach (HotReloadIntroducedTypeDescriptor declaration in declarations)
            {
                ownerPaths.Add(declaration.OwnerProjectRelativePath);
            }

            string reason = "Introduced type " + declarations[0].MetadataName.Value
                + " is declared in more than one file of the group: "
                + string.Join(", ", ownerPaths) + ".";
            foreach (HotReloadIntroducedTypeDescriptor declaration in declarations)
            {
                failures.Add(
                    HotReloadIntroducedTypeOutcome.Failed(
                        declaration.MetadataName.Value,
                        targetAssemblyName,
                        declaration.OwnerProjectRelativePath,
                        reason));
            }
        }

        // Why the owner comes from the file row and not the reuse row: the worker records a reuse
        // on the unit whose source declares it, so the row that holds it is the attribution.
        private static List<HotReloadIntroducedTypeOutcome> CollectAlreadyActiveTypes(
            TransformWorkerOutputDto output)
        {
            List<HotReloadIntroducedTypeOutcome> outcomes = new List<HotReloadIntroducedTypeOutcome>();
            foreach (TransformWorkerFileOutputDto file in output.files)
            {
                foreach (TransformWorkerIntroducedTypeReuseDto reuse in file.introducedTypeReuses)
                {
                    outcomes.Add(
                        HotReloadIntroducedTypeOutcome.AlreadyActive(
                            reuse.metadataName,
                            reuse.originalAssemblyName,
                            file.projectRelativePath));
                }
            }

            return outcomes;
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
