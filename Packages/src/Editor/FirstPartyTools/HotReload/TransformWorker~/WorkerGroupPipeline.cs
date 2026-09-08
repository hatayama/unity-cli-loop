using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

// Transforms every edited source of one compilation assembly in a single pass: all trees enter
// one compilation, added members are registered for the whole group before any body is guarded
// or emitted, and the emitted shim source covers the group. That order is what lets a body
// edited in one file call a member added in another.
internal static class WorkerGroupPipeline
{
    internal const string PrepareIntroducedTypesOperation = "prepareIntroducedTypes";

    internal static WorkerOutput Run(WorkerInput input)
    {
        if (string.Equals(input.Operation, PrepareIntroducedTypesOperation, StringComparison.Ordinal))
        {
            return IntroducedTypePreparation.Prepare(input);
        }

        if (!string.IsNullOrEmpty(input.Operation))
        {
            return CreateRunFailureOutput("Unknown worker operation: " + input.Operation);
        }

        return Transform(input);
    }

    internal static WorkerOutput Transform(WorkerInput input)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(
            languageVersion: LanguageVersion.Latest,
            preprocessorSymbols: input.Defines);
        List<WorkerSourceUnit> units = new List<WorkerSourceUnit>(input.Sources.Length);
        foreach (WorkerSourceInput source in input.Sources)
        {
            units.Add(WorkerSourceLoader.Load(source, parseOptions));
        }

        List<WorkerSourceUnit> loadedUnits = new List<WorkerSourceUnit>(units.Count);
        foreach (WorkerSourceUnit unit in units)
        {
            if (unit.SyntaxTree != null)
            {
                loadedUnits.Add(unit);
            }
        }

        // Why every loaded unit reports it: a missing reference is a problem of the whole assembly,
        // not of one source, so each file's own result has to state it — reporting it on one unit
        // would make the failure move as the input order changes. A run with no loadable source
        // drops it, because such a run reports no per-file findings at all.
        List<string> referenceParseErrors = new List<string>();
        (List<MetadataReference> references, MetadataReference targetTypesReference) =
            CollectMetadataReferences(input, referenceParseErrors);
        foreach (WorkerSourceUnit loadedUnit in loadedUnits)
        {
            loadedUnit.ParseErrors.AddRange(referenceParseErrors);
        }

        List<WorkerSourceUnit> transformUnits = SelectTransformableUnits(loadedUnits);

        // Why a run-level failure and not a per-file diagnostic: the orchestrator advances to
        // revert, gating and compile whenever the run succeeds, so a run that could not trust its
        // retained artifacts has to stop the whole group rather than transform against a binding
        // that is missing a type or attributing it to the wrong assembly.
        string artifactFailure = PrepareBindingTrees(input, transformUnits, references, targetTypesReference, parseOptions);
        if (artifactFailure != null)
        {
            return CreateRunFailureOutput(artifactFailure);
        }

        List<SyntaxTree> bindingTrees = new List<SyntaxTree>(transformUnits.Count);
        foreach (WorkerSourceUnit transformUnit in transformUnits)
        {
            bindingTrees.Add(transformUnit.BindingSyntaxTree);
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "UloopHotReloadTransformWorkerCompilation",
            syntaxTrees: bindingTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        foreach (WorkerSourceUnit unit in transformUnits)
        {
            unit.SemanticModel = compilation.GetSemanticModel(unit.BindingSyntaxTree, ignoreAccessibility: true);
        }

        IAssemblySymbol targetTypesAssemblySymbol = ResolveTargetTypesAssemblySymbol(
            compilation,
            targetTypesReference);
        List<CompilationUnitSyntax> editedRoots = new List<CompilationUnitSyntax>(transformUnits.Count);
        foreach (WorkerSourceUnit transformUnit in transformUnits)
        {
            editedRoots.Add(transformUnit.Root);
        }

        List<UsingDirectiveSyntax> assemblyGlobalUsings =
            WorkerUsingCollector.CollectAssemblyGlobalUsings(input, parseOptions, editedRoots);
        List<string> siblingConstDriftWarnings = SiblingConstDriftCollector.CollectConstDriftWarnings(
            input.ChangedSiblingSourcePaths,
            parseOptions,
            references,
            targetTypesAssemblySymbol);

        List<WorkerEntry> entries = new List<WorkerEntry>();
        List<WorkerSkipped> skipped = new List<WorkerSkipped>();
        List<WorkerUnchangedMethod> unchangedMethods = new List<WorkerUnchangedMethod>();
        List<ShimTypeBuilder> shimTypes = new List<ShimTypeBuilder>();
        AddedMethodCatalog addedMethodCatalog = new AddedMethodCatalog();
        AddedFieldCatalog addedFieldCatalog = new AddedFieldCatalog();
        AddedPropertyCatalog addedPropertyCatalog = new AddedPropertyCatalog();
        ShimNameAllocator shimNames = new ShimNameAllocator();
        foreach (WorkerSourceUnit unit in transformUnits)
        {
            QueueUnit(
                unit,
                input,
                parseOptions,
                targetTypesAssemblySymbol,
                assemblyGlobalUsings,
                shimTypes,
                addedMethodCatalog,
                addedFieldCatalog,
                addedPropertyCatalog,
                skipped,
                unchangedMethods,
                shimNames);
        }

        foreach (WorkerSourceUnit unit in transformUnits)
        {
            RemovedMemberCollector.CollectRemovedMembersIfBaseline(
                unit.Baseline,
                unit.PlainRoot,
                unit.TypeEmitStates,
                unit.SemanticModel,
                targetTypesAssemblySymbol,
                addedMethodCatalog,
                addedFieldCatalog,
                unit.RemovedMembers,
                unit.RemovedMethodSignatures);
        }

        // Why one concatenated list: the guard runs to a fixed point, so a body that calls an
        // added method of another file must be able to lose its shim in the same iteration.
        List<TypeEmitState> allTypeEmitStates = new List<TypeEmitState>();
        foreach (WorkerSourceUnit unit in transformUnits)
        {
            allTypeEmitStates.AddRange(unit.TypeEmitStates);
        }

        AddedCallSiteGuard.SkipBodiesThatCannotUseAddedMethods(
            allTypeEmitStates,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog,
            skipped);

        ShimMethodEmitter.EmitQueuedMethodsAndPropertyGetters(
            allTypeEmitStates,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog,
            input,
            entries,
            skipped,
            unchangedMethods,
            shimTypes,
            assemblyGlobalUsings,
            shimNames);

        foreach (WorkerSourceUnit unit in transformUnits)
        {
            // Why registered here and not where the type is planned: planning is a separate
            // worker operation, so by the time this run transforms the file the type is served
            // from a retained artifact. The drift check reads the unmodified edited root, where
            // the declaration is still visible, and would call an applied type an unapplied edit.
            foreach (string metadataName in unit.RetainedIntroducedTypeMetadataNames)
            {
                addedMethodCatalog.AddAddedTypeSyntaxKey(metadataName);
            }

            AppendOutsideMethodBodyDriftWarnings(unit, addedMethodCatalog, addedFieldCatalog);
        }

        return BuildWorkerOutput(
            units,
            shimTypes,
            entries,
            skipped,
            unchangedMethods,
            siblingConstDriftWarnings,
            addedFieldCatalog);
    }

    // Keeps only the units a transform may read. A unit with parse errors is dropped: Roslyn's
    // recovery tree still exposes method-shaped nodes, so transforming it would shim bodies read
    // out of broken source and call the file applied. A file is all-or-nothing, so such a unit
    // only carries its ParseErrors on its own file row and contributes no entry and no skipped
    // row. This is the condition IntroducedTypePreparation already applies to type introduction.
    private static List<WorkerSourceUnit> SelectTransformableUnits(List<WorkerSourceUnit> loadedUnits)
    {
        List<WorkerSourceUnit> transformUnits = new List<WorkerSourceUnit>(loadedUnits.Count);
        foreach (WorkerSourceUnit loadedUnit in loadedUnits)
        {
            if (loadedUnit.ParseErrors.Count == 0)
            {
                transformUnits.Add(loadedUnit);
            }
        }

        return transformUnits;
    }

    // Removes from each unit's binding tree the declarations a retained artifact already serves,
    // and reports why the artifacts could not be used at all. Returns null when the run has no
    // artifact to bind against, which is every run until introduced types are in play.
    private static string PrepareBindingTrees(
        WorkerInput input,
        List<WorkerSourceUnit> loadedUnits,
        List<MetadataReference> references,
        MetadataReference targetTypesReference,
        CSharpParseOptions parseOptions)
    {
        if (input.IntroducedTypeArtifacts.Length == 0)
        {
            return null;
        }

        List<string> artifactErrors = new List<string>();
        List<(WorkerIntroducedTypeArtifact Artifact, MetadataReference Reference)> artifactReferences =
            IntroducedTypeArtifactReferences.Collect(input, references, artifactErrors);
        if (artifactErrors.Count > 0)
        {
            return artifactErrors[0];
        }

        List<SyntaxTree> editedTrees = new List<SyntaxTree>(loadedUnits.Count);
        foreach (WorkerSourceUnit loadedUnit in loadedUnits)
        {
            editedTrees.Add(loadedUnit.SyntaxTree);
        }

        // The declarations are verified against the edited text, so this compilation binds the
        // trees as written; the binding compilation is built from what survives the removal.
        CSharpCompilation verificationCompilation = CSharpCompilation.Create(
            assemblyName: "UloopHotReloadRetainedDeclarationVerification",
            syntaxTrees: editedTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        IAssemblySymbol targetAssembly = ResolveTargetTypesAssemblySymbol(
            verificationCompilation,
            targetTypesReference);

        // A record is only valid for the assembly generation it was planned against, and the
        // recorded identities are what the fingerprint is rebuilt from. Binding against an
        // assembly the request cannot name would rebuild them from an empty identity, no
        // declaration would match its record, and every retained type would quietly bind from
        // source again.
        if (!IntroducedTypeTargetIdentity.MatchesRequest(input, targetAssembly))
        {
            return "Retained introduced types require the assembly identity the records were planned against.";
        }

        if (!IntroducedTypeArtifactMap.TryBuild(
                verificationCompilation, artifactReferences, out IntroducedTypeArtifactMap artifactMap, out string artifactError))
        {
            return artifactError;
        }

        foreach (WorkerSourceUnit loadedUnit in loadedUnits)
        {
            loadedUnit.ArtifactMap = artifactMap;
        }
        Dictionary<WorkerSourceUnit, List<BaseTypeDeclarationSyntax>> retainedDeclarations =
            IntroducedTypeDeclarationVerifier.FindRetainedDeclarations(
                loadedUnits, verificationCompilation, input, targetAssembly, artifactMap);
        List<string> bindingParseErrors = new List<string>();
        foreach (KeyValuePair<WorkerSourceUnit, List<BaseTypeDeclarationSyntax>> entry in retainedDeclarations)
        {
            // Recorded before the removal, because the rewriter replaces the unit's tree and
            // these declarations belong to the tree it replaces. Built from the syntax rather
            // than the symbol: the drift check strips by the syntax key of the edited root.
            foreach (BaseTypeDeclarationSyntax declaration in entry.Value)
            {
                if (declaration is TypeDeclarationSyntax typeDeclaration)
                {
                    entry.Key.RetainedIntroducedTypeMetadataNames.Add(
                        WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration));
                }
                else if (declaration is EnumDeclarationSyntax enumDeclaration)
                {
                    entry.Key.RetainedIntroducedTypeMetadataNames.Add(
                        WorkerSyntaxIndex.BuildEnumMetadataNameFromSyntax(enumDeclaration));
                }
            }

            IntroducedTypeBindingRewriter.RemoveRetainedDeclarations(
                entry.Key, entry.Value, parseOptions, bindingParseErrors);
        }

        if (bindingParseErrors.Count > 0)
        {
            return bindingParseErrors[0];
        }

        return null;
    }

    private static WorkerOutput CreateRunFailureOutput(string parseError)
    {
        return new WorkerOutput
        {
            ShimSource = string.Empty,
            Entries = Array.Empty<WorkerEntry>(),
            Skipped = Array.Empty<WorkerSkipped>(),
            Files = Array.Empty<WorkerFileOutput>(),
            ParseErrors = new[] { parseError },
            SiblingConstDriftWarnings = Array.Empty<string>(),
            UnchangedMethods = Array.Empty<WorkerUnchangedMethod>()
        };
    }

    // Everything one unit contributes before the group-wide guard and emit: its drift warnings,
    // its baseline, and the queued shim methods of its types.
    private static void QueueUnit(
        WorkerSourceUnit unit,
        WorkerInput input,
        CSharpParseOptions parseOptions,
        IAssemblySymbol targetTypesAssemblySymbol,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        AddedPropertyCatalog addedPropertyCatalog,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        ShimNameAllocator shimNames)
    {
        unit.DeclarationDriftWarnings.AddRange(
            ConstDriftCollector.CollectConstDriftWarnings(
                unit.BindingRoot,
                unit.SemanticModel,
                targetTypesAssemblySymbol));
        // Why here: a compiled property/event can disappear or change kind with no
        // touched body, so the generic outside-body warning would bury the name.
        unit.KindChangeSyntaxKeys =
            CompiledMemberKindChangeWarnings.AppendCompiledPropertyOrEventKindChangeWarnings(
                unit.BindingRoot,
                unit.SemanticModel,
                targetTypesAssemblySymbol,
                unit.DeclarationDriftWarnings);
        unit.Baseline = BaselineSnapshotBuilder.BuildBaselineSnapshotState(
            unit.Input.SnapshotSource,
            parseOptions,
            unit.PlainRoot);

        List<TypeEmitState> typeEmitStates = TypeEmitPlanner.QueueAllTypeEmitStates(
            unit,
            targetTypesAssemblySymbol,
            input,
            assemblyGlobalUsings,
            shimTypes,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog,
            skipped,
            unchangedMethods,
            unit.DeclarationDriftWarnings,
            unit.RemovedMembers,
            unit.RemovedMethodSignatures,
            shimNames);
        unit.TypeEmitStates = typeEmitStates;
    }

    private static void AppendOutsideMethodBodyDriftWarnings(
        WorkerSourceUnit unit,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        if (!unit.Baseline.HasBaseline || unit.Baseline.SnapshotRoot == null)
        {
            return;
        }

        // Why after property emit: added-property syntax keys are registered when a skip
        // row is written. Running the drift check first would miss those keys and keep
        // the false outside-body warning for added properties that already have a row.
        OutsideMethodBodyDriftChecker.AppendOutsideMethodBodyDriftWarningIfNeeded(
            unit.Baseline.SnapshotRoot,
            unit.PlainRoot,
            Path.GetFileName(unit.Input.SourcePath),
            unit.DeclarationDriftWarnings,
            addedMethodCatalog,
            addedFieldCatalog,
            unit.KindChangeSyntaxKeys.PropertySyntaxKeys,
            unit.KindChangeSyntaxKeys.EventSyntaxKeys);
    }

    internal static (List<MetadataReference> References, MetadataReference TargetTypesReference)
        CollectMetadataReferences(WorkerInput input, List<string> parseErrors)
    {
        string targetTypesFullPath =
            !string.IsNullOrEmpty(input.TargetTypesAssemblyPath) && File.Exists(input.TargetTypesAssemblyPath)
                ? Path.GetFullPath(input.TargetTypesAssemblyPath)
                : null;
        MetadataReference targetTypesReference = null;

        List<MetadataReference> references = new List<MetadataReference>();
        foreach (string referencePath in input.ReferencePaths)
        {
            if (File.Exists(referencePath))
            {
                MetadataReference reference = MetadataReference.CreateFromFile(referencePath);
                references.Add(reference);
                if (targetTypesFullPath != null
                    && string.Equals(
                        Path.GetFullPath(referencePath),
                        targetTypesFullPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    targetTypesReference = reference;
                }
            }
            else
            {
                parseErrors.Add("Reference not found: " + referencePath);
            }
        }

        if (targetTypesFullPath != null && targetTypesReference == null)
        {
            targetTypesReference = MetadataReference.CreateFromFile(input.TargetTypesAssemblyPath);
            references.Add(targetTypesReference);
        }

        return (references, targetTypesReference);
    }
    internal static IAssemblySymbol ResolveTargetTypesAssemblySymbol(
        CSharpCompilation compilation,
        MetadataReference targetTypesReference)
    {
        // The drift comparison must see private and internal consts in the compiled target
        // assembly, which the default MetadataImportOptions (Public) hides. Widening the main
        // compilation would also widen what every classification query can bind to, so the
        // wider import is confined to a throwaway compilation used only for this lookup.
        if (targetTypesReference == null)
        {
            return null;
        }

        CSharpCompilation driftCompilation = compilation.WithOptions(
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithMetadataImportOptions(MetadataImportOptions.All));
        return driftCompilation.GetAssemblyOrModuleSymbol(targetTypesReference) as IAssemblySymbol;
    }

    private static WorkerOutput BuildWorkerOutput(
        List<WorkerSourceUnit> units,
        List<ShimTypeBuilder> shimTypes,
        List<WorkerEntry> entries,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        List<string> siblingConstDriftWarnings,
        AddedFieldCatalog addedFieldCatalog)
    {
        bool hasAccessorDelegates = false;
        foreach (ShimTypeBuilder shimType in shimTypes)
        {
            if (shimType.AccessorPlan.Entries.Count > 0)
            {
                hasAccessorDelegates = true;
                break;
            }
        }

        // Why the input order: the orchestrator pairs files[i] with the source it sent.
        WorkerFileOutput[] files = new WorkerFileOutput[units.Count];
        for (int index = 0; index < units.Count; index++)
        {
            files[index] = BuildFileOutput(units[index], addedFieldCatalog);
        }

        return new WorkerOutput
        {
            ShimSource = ShimSourceEmitter.Emit(shimTypes),
            Entries = entries.ToArray(),
            Skipped = skipped.ToArray(),
            Files = files,
            SiblingConstDriftWarnings = siblingConstDriftWarnings.ToArray(),
            UnchangedMethods = unchangedMethods.ToArray(),
            HasAccessorDelegates = hasAccessorDelegates,
            HasAddedFieldRewrites = addedFieldCatalog.HasStoreRewrites
        };
    }

    private static WorkerFileOutput BuildFileOutput(WorkerSourceUnit unit, AddedFieldCatalog addedFieldCatalog)
    {
        string projectRelativePath = unit.Input.ProjectRelativePath;
        return new WorkerFileOutput
        {
            ProjectRelativePath = projectRelativePath,
            SourceContentSha256 = unit.SourceContentSha256,
            ParseErrors = unit.ParseErrors.ToArray(),
            DeclarationDriftWarnings = unit.DeclarationDriftWarnings.ToArray(),
            // A unit that failed to load has no baseline, so duplicate keys never disabled one.
            BaselineDisabledByDuplicateKeys =
                unit.Baseline != null && unit.Baseline.BaselineDisabledByDuplicateKeys,
            RemovedMembers = unit.RemovedMembers.ToArray(),
            RemovedMethodSignatures = unit.RemovedMethodSignatures.ToArray(),
            AddedFieldNames = addedFieldCatalog.ListRewrittenAddedFieldDisplayNames(projectRelativePath),
            AddedConstNames = addedFieldCatalog.ListFoldedConstDisplayNames(projectRelativePath),
            IntroducedTypes = unit.IntroducedTypes.ToArray(),
            IntroducedTypeDiagnostics = unit.IntroducedTypeDiagnostics.ToArray(),
            IntroducedTypeReuses = unit.IntroducedTypeReuses.ToArray()
        };
    }
}
