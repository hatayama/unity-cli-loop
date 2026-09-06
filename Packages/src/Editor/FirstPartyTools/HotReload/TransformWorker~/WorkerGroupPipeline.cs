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
        List<SyntaxTree> syntaxTrees = new List<SyntaxTree>(units.Count);
        foreach (WorkerSourceUnit unit in units)
        {
            if (unit.SyntaxTree == null)
            {
                continue;
            }

            loadedUnits.Add(unit);
            syntaxTrees.Add(unit.SyntaxTree);
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

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "UloopHotReloadTransformWorkerCompilation",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        foreach (WorkerSourceUnit unit in loadedUnits)
        {
            unit.SemanticModel = compilation.GetSemanticModel(unit.SyntaxTree, ignoreAccessibility: true);
        }

        IAssemblySymbol targetTypesAssemblySymbol = ResolveTargetTypesAssemblySymbol(
            compilation,
            targetTypesReference);
        List<CompilationUnitSyntax> editedRoots = new List<CompilationUnitSyntax>(loadedUnits.Count);
        foreach (WorkerSourceUnit loadedUnit in loadedUnits)
        {
            editedRoots.Add(loadedUnit.Root);
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
        // Why counters run across units: shim type and method names must stay unique when two
        // files of the group declare types of the same name.
        int shimTypeCounter = 0;
        int globalShimMethodCounter = 0;
        foreach (WorkerSourceUnit unit in loadedUnits)
        {
            (shimTypeCounter, globalShimMethodCounter) = QueueUnit(
                unit,
                input,
                parseOptions,
                targetTypesAssemblySymbol,
                assemblyGlobalUsings,
                shimTypes,
                addedMethodCatalog,
                addedFieldCatalog,
                skipped,
                unchangedMethods,
                shimTypeCounter,
                globalShimMethodCounter);
        }

        foreach (WorkerSourceUnit unit in loadedUnits)
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
        foreach (WorkerSourceUnit unit in loadedUnits)
        {
            allTypeEmitStates.AddRange(unit.TypeEmitStates);
        }

        AddedCallSiteGuard.SkipBodiesThatCannotUseAddedMethods(
            allTypeEmitStates,
            addedMethodCatalog,
            addedFieldCatalog,
            skipped);

        ShimMethodEmitter.EmitQueuedMethodsAndPropertyGetters(
            allTypeEmitStates,
            addedMethodCatalog,
            addedFieldCatalog,
            input,
            entries,
            skipped,
            unchangedMethods,
            shimTypes,
            assemblyGlobalUsings,
            shimTypeCounter,
            globalShimMethodCounter);

        foreach (WorkerSourceUnit unit in loadedUnits)
        {
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
    private static (int ShimTypeCounter, int GlobalShimMethodCounter) QueueUnit(
        WorkerSourceUnit unit,
        WorkerInput input,
        CSharpParseOptions parseOptions,
        IAssemblySymbol targetTypesAssemblySymbol,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        int shimTypeCounter,
        int globalShimMethodCounter)
    {
        unit.DeclarationDriftWarnings.AddRange(
            ConstDriftCollector.CollectConstDriftWarnings(
                unit.Root,
                unit.SemanticModel,
                targetTypesAssemblySymbol));
        // Why here: a compiled property/event can disappear or change kind with no
        // touched body, so the generic outside-body warning would bury the name.
        unit.KindChangeSyntaxKeys =
            CompiledMemberKindChangeWarnings.AppendCompiledPropertyOrEventKindChangeWarnings(
                unit.Root,
                unit.SemanticModel,
                targetTypesAssemblySymbol,
                unit.DeclarationDriftWarnings);
        unit.Baseline = BaselineSnapshotBuilder.BuildBaselineSnapshotState(
            unit.Input.SnapshotSource,
            parseOptions,
            unit.PlainRoot);

        (List<TypeEmitState> typeEmitStates, int nextShimTypeCounter, int nextGlobalShimMethodCounter) =
            TypeEmitPlanner.QueueAllTypeEmitStates(
                unit,
                targetTypesAssemblySymbol,
                input,
                assemblyGlobalUsings,
                shimTypes,
                addedMethodCatalog,
                addedFieldCatalog,
                skipped,
                unchangedMethods,
                unit.DeclarationDriftWarnings,
                unit.RemovedMembers,
                unit.RemovedMethodSignatures,
                shimTypeCounter,
                globalShimMethodCounter);
        unit.TypeEmitStates = typeEmitStates;
        return (nextShimTypeCounter, nextGlobalShimMethodCounter);
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
            IntroducedTypeDiagnostics = unit.IntroducedTypeDiagnostics.ToArray()
        };
    }
}
