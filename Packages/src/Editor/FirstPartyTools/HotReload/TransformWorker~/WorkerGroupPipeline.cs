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
        string artifactFailure = RetainedDeclarationStage.PrepareBindingTrees(
            input,
            transformUnits,
            references,
            targetTypesReference,
            parseOptions);
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

        WorkerTypeHome home = new WorkerTypeHome(
            input.TargetAssemblyName,
            WorkerCompiledAssemblySymbols.ResolveWithAllMembers(compilation, targetTypesReference));
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
            home);

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
                home,
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
                home,
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

        HashSet<string> activePatchedLabels =
            new HashSet<string>(input.ActivePatchedMethodLabels, StringComparer.Ordinal);
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
            AddedFieldSkippedWriterWarnings.AppendWarnings(
                unit,
                transformUnits,
                allTypeEmitStates,
                addedFieldCatalog,
                addedPropertyCatalog,
                skipped,
                activePatchedLabels);
        }

        return BuildWorkerOutput(
            units,
            shimTypes,
            entries,
            skipped,
            unchangedMethods,
            siblingConstDriftWarnings,
            addedFieldCatalog,
            CreateDeclaredTypeNames(compilation, home, transformUnits));
    }

    // Every unit of a run holds the same run-wide retained records and artifact map, so any one
    // of them answers for the group. A run with no transformable unit has neither.
    private static AddedFieldDeclaredTypeNames CreateDeclaredTypeNames(
        CSharpCompilation compilation,
        WorkerTypeHome home,
        List<WorkerSourceUnit> transformUnits)
    {
        if (transformUnits.Count == 0)
        {
            return new AddedFieldDeclaredTypeNames(
                compilation.Assembly, home, new WorkerRetainedBodyEditType[0], IntroducedTypeArtifactMap.Empty);
        }

        WorkerSourceUnit anyUnit = transformUnits[0];
        return new AddedFieldDeclaredTypeNames(
            compilation.Assembly, home, anyUnit.RunRetainedBodyEditTypes, anyUnit.ArtifactMap);
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
        WorkerTypeHome home,
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
                home));
        // Why here: a compiled property/event can disappear or change kind with no
        // touched body, so the generic outside-body warning would bury the name.
        unit.KindChangeSyntaxKeys =
            CompiledMemberKindChangeWarnings.AppendCompiledPropertyOrEventKindChangeWarnings(
                unit.BindingRoot,
                unit.SemanticModel,
                home,
                unit.DeclarationDriftWarnings);
        unit.Baseline = BaselineSnapshotBuilder.BuildBaselineSnapshotState(
            unit.Input.SnapshotSource,
            parseOptions,
            unit.PlainRoot);

        List<TypeEmitState> typeEmitStates = TypeEmitPlanner.QueueAllTypeEmitStates(
            unit,
            home,
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
    private static WorkerOutput BuildWorkerOutput(
        List<WorkerSourceUnit> units,
        List<ShimTypeBuilder> shimTypes,
        List<WorkerEntry> entries,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        List<string> siblingConstDriftWarnings,
        AddedFieldCatalog addedFieldCatalog,
        AddedFieldDeclaredTypeNames declaredTypeNames)
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
            files[index] = BuildFileOutput(units[index], addedFieldCatalog, declaredTypeNames);
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

    private static WorkerFileOutput BuildFileOutput(
        WorkerSourceUnit unit,
        AddedFieldCatalog addedFieldCatalog,
        AddedFieldDeclaredTypeNames declaredTypeNames)
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
            AddedFieldInitializers =
                addedFieldCatalog.ListRewrittenAddedFieldInitializers(projectRelativePath),
            AddedFieldDeclarations =
                addedFieldCatalog.ListRewrittenAddedFieldDeclarations(projectRelativePath, declaredTypeNames),
            AddedConstNames = addedFieldCatalog.ListFoldedConstDisplayNames(projectRelativePath),
            IntroducedTypes = unit.IntroducedTypes.ToArray(),
            IntroducedTypeDiagnostics = unit.IntroducedTypeDiagnostics.ToArray(),
            IntroducedTypeReuses = unit.IntroducedTypeReuses.ToArray(),
            PlannedAddedMemberNames = Array.Empty<string>()
        };
    }
}
