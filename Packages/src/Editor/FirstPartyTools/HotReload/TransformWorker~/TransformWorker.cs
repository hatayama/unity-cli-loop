// Hot-reload transform worker: parse + semantic analysis of one edited C# file, emit static
// shim method sources (no Prefix wrappers) plus a per-method manifest / skip list.
// Runs out-of-process on the Unity-bundled .NET host against the Unity-bundled Roslyn.
// Generated shims mirror user method signatures verbatim; repo style rules apply to
// hand-written code only.

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

public static class TransformWorkerProgram
{
    private const string RoslynDirectorySidecarFileName = "roslyn-directory.txt";

    // SyntaxAnnotation kind for original-source 1-based lines; survive rewriter + NormalizeWhitespace
    // so Emit can inject #line directives after formatting.
    internal const string UloopLineAnnotationKind = "uloop-line";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: TransformWorker <input-json-path> <output-json-path>");
            return 2;
        }

        string roslynDirectoryPath = ReadRoslynDirectorySidecar();
        AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            string candidatePath = Path.Combine(roslynDirectoryPath, assemblyName.Name + ".dll");
            if (File.Exists(candidatePath))
            {
                return context.LoadFromAssemblyPath(candidatePath);
            }

            return null;
        };

        return RunTransform(args[0], args[1]);
    }

    private static string ReadRoslynDirectorySidecar()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string sidecarPath = Path.Combine(baseDirectory, RoslynDirectorySidecarFileName);
        if (!File.Exists(sidecarPath))
        {
            throw new InvalidOperationException(
                "Roslyn directory sidecar not found next to worker.dll: " + sidecarPath);
        }

        string roslynDirectoryPath = File.ReadAllText(sidecarPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).Trim();
        if (string.IsNullOrEmpty(roslynDirectoryPath) || !Directory.Exists(roslynDirectoryPath))
        {
            throw new InvalidOperationException("Roslyn directory from sidecar is missing: " + roslynDirectoryPath);
        }

        return roslynDirectoryPath;
    }

    private static int RunTransform(string inputJsonPath, string outputJsonPath)
    {
        WorkerInput input = ReadInput(inputJsonPath);
        WorkerOutput unsupportedSourceCount = TryCreateUnsupportedSourceCountOutput(input);
        WorkerOutput output = unsupportedSourceCount ?? TransformFile(input);
        WriteOutput(outputJsonPath, output);
        return 0;
    }

    // Why a run-level failure output (not a throw): the source count crosses a process boundary
    // via JSON, so it is untrusted input rather than a broken internal contract.
    private static WorkerOutput TryCreateUnsupportedSourceCountOutput(WorkerInput input)
    {
        if (input.Sources.Length == 1)
        {
            return null;
        }

        return new WorkerOutput
        {
            ShimSource = string.Empty,
            Entries = Array.Empty<WorkerEntry>(),
            Skipped = Array.Empty<WorkerSkipped>(),
            Files = Array.Empty<WorkerFileOutput>(),
            ParseErrors = new[] { "This worker build accepts exactly one source." }
        };
    }

    private static WorkerInput ReadInput(string inputJsonPath)
    {
        byte[] bytes = File.ReadAllBytes(inputJsonPath);
        WorkerInput input = JsonSerializer.Deserialize<WorkerInput>(bytes, JsonOptions);
        if (input == null)
        {
            throw new InvalidOperationException("Failed to deserialize worker input JSON.");
        }

        input.Sources ??= Array.Empty<WorkerSourceInput>();
        input.Defines ??= Array.Empty<string>();
        input.ReferencePaths ??= Array.Empty<string>();
        input.ExcludedMethodKeys ??= Array.Empty<string>();
        input.ExcludedAddedMethodKeys ??= Array.Empty<string>();
        input.AssemblySourcePaths ??= Array.Empty<string>();
        input.ChangedSiblingSourcePaths ??= Array.Empty<string>();
        return input;
    }

    private static void WriteOutput(string outputJsonPath, WorkerOutput output)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(output, JsonOptions);
        File.WriteAllBytes(outputJsonPath, bytes);
    }

    private static WorkerOutput TransformFile(WorkerInput input)
    {
        WorkerSourceInput source = input.Sources[0];
        CSharpParseOptions parseOptions = new CSharpParseOptions(
            languageVersion: LanguageVersion.Latest,
            preprocessorSymbols: input.Defines);
        WorkerSourceUnit unit = WorkerSourceLoader.Load(source, parseOptions);
        if (unit.SyntaxTree == null)
        {
            return CreateSourceFailureOutput(source, unit.ParseErrors.ToArray());
        }

        List<string> parseErrors = unit.ParseErrors;
        string sourceContentSha256 = unit.SourceContentSha256;
        SyntaxTree syntaxTree = unit.SyntaxTree;
        CompilationUnitSyntax plainRoot = unit.PlainRoot;

        (List<MetadataReference> references, MetadataReference targetTypesReference) =
            CollectMetadataReferences(input, parseErrors);

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "UloopHotReloadTransformWorkerCompilation",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
        unit.SemanticModel = semanticModel;
        CompilationUnitSyntax root = unit.Root;

        IAssemblySymbol targetTypesAssemblySymbol = ResolveTargetTypesAssemblySymbol(
            compilation,
            targetTypesReference);
        List<string> declarationDriftWarnings = ConstDriftCollector.CollectConstDriftWarnings(
            root,
            semanticModel,
            targetTypesAssemblySymbol);
        List<string> siblingConstDriftWarnings = SiblingConstDriftCollector.CollectConstDriftWarnings(
            input.ChangedSiblingSourcePaths,
            parseOptions,
            references,
            targetTypesAssemblySymbol);
        // Why here: a compiled property/event can disappear or change kind with no
        // touched body, so the generic outside-body warning would bury the name.
        CompiledMemberKindChangeWarnings.SyntaxKeys kindChangeSyntaxKeys =
            CompiledMemberKindChangeWarnings.AppendCompiledPropertyOrEventKindChangeWarnings(
                root,
                semanticModel,
                targetTypesAssemblySymbol,
                declarationDriftWarnings);

        BaselineSnapshotState baseline =
            BaselineSnapshotBuilder.BuildBaselineSnapshotState(source.SnapshotSource, parseOptions, plainRoot);
        unit.Baseline = baseline;

        List<WorkerEntry> entries = new List<WorkerEntry>();
        List<WorkerSkipped> skipped = new List<WorkerSkipped>();
        List<WorkerUnchangedMethod> unchangedMethods = new List<WorkerUnchangedMethod>();
        List<WorkerRemovedMember> removedMembers = new List<WorkerRemovedMember>();
        List<WorkerRemovedMethodSignature> removedMethodSignatures = new List<WorkerRemovedMethodSignature>();
        List<ShimTypeBuilder> shimTypes = new List<ShimTypeBuilder>();
        int globalShimMethodCounter = 0;
        int shimTypeCounter = 0;
        List<UsingDirectiveSyntax> assemblyGlobalUsings =
            WorkerUsingCollector.CollectAssemblyGlobalUsings(input, parseOptions);
        AddedMethodCatalog addedMethodCatalog = new AddedMethodCatalog();
        AddedFieldCatalog addedFieldCatalog = new AddedFieldCatalog();
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
                declarationDriftWarnings,
                removedMembers,
                removedMethodSignatures,
                shimTypeCounter,
                globalShimMethodCounter);
        shimTypeCounter = nextShimTypeCounter;
        globalShimMethodCounter = nextGlobalShimMethodCounter;

        RemovedMemberCollector.CollectRemovedMembersIfBaseline(
            baseline,
            plainRoot,
            typeEmitStates,
            semanticModel,
            targetTypesAssemblySymbol,
            addedMethodCatalog,
            addedFieldCatalog,
            removedMembers,
            removedMethodSignatures);

        AddedCallSiteGuard.SkipBodiesThatCannotUseAddedMethods(
            typeEmitStates,
            addedMethodCatalog,
            addedFieldCatalog,
            skipped);

        ShimMethodEmitter.EmitQueuedMethodsAndPropertyGetters(
            typeEmitStates,
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

        if (baseline.HasBaseline && baseline.SnapshotRoot != null)
        {
            // Why after property emit: added-property syntax keys are registered when a skip
            // row is written. Running the drift check first would miss those keys and keep
            // the false outside-body warning for added properties that already have a row.
            OutsideMethodBodyDriftChecker.AppendOutsideMethodBodyDriftWarningIfNeeded(
                baseline.SnapshotRoot,
                plainRoot,
                Path.GetFileName(source.SourcePath),
                declarationDriftWarnings,
                addedMethodCatalog,
                addedFieldCatalog,
                kindChangeSyntaxKeys.PropertySyntaxKeys,
                kindChangeSyntaxKeys.EventSyntaxKeys);
        }

        return BuildWorkerOutput(
            source.ProjectRelativePath,
            shimTypes,
            entries,
            skipped,
            declarationDriftWarnings,
            siblingConstDriftWarnings,
            parseErrors,
            unchangedMethods,
            baseline,
            removedMembers,
            removedMethodSignatures,
            addedFieldCatalog,
            sourceContentSha256);
    }

    // A failure the run can attribute to one source: the row set is empty and the messages
    // travel on that source's per-file parse errors.
    private static WorkerOutput CreateSourceFailureOutput(WorkerSourceInput source, string[] parseErrors)
    {
        return new WorkerOutput
        {
            ShimSource = string.Empty,
            Entries = Array.Empty<WorkerEntry>(),
            Skipped = Array.Empty<WorkerSkipped>(),
            Files = new[]
            {
                new WorkerFileOutput
                {
                    ProjectRelativePath = source.ProjectRelativePath,
                    SourceContentSha256 = string.Empty,
                    ParseErrors = parseErrors,
                    DeclarationDriftWarnings = Array.Empty<string>(),
                    RemovedMembers = Array.Empty<WorkerRemovedMember>(),
                    RemovedMethodSignatures = Array.Empty<WorkerRemovedMethodSignature>(),
                    AddedFieldNames = Array.Empty<string>(),
                    AddedConstNames = Array.Empty<string>()
                }
            }
        };
    }

    private static (List<MetadataReference> References, MetadataReference TargetTypesReference)
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

    private static IAssemblySymbol ResolveTargetTypesAssemblySymbol(
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
        string projectRelativePath,
        List<ShimTypeBuilder> shimTypes,
        List<WorkerEntry> entries,
        List<WorkerSkipped> skipped,
        List<string> declarationDriftWarnings,
        List<string> siblingConstDriftWarnings,
        List<string> parseErrors,
        List<WorkerUnchangedMethod> unchangedMethods,
        BaselineSnapshotState baseline,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures,
        AddedFieldCatalog addedFieldCatalog,
        string sourceContentSha256)
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

        // Why stamp here instead of at each row's producer: six separate producers emit these
        // rows, and stamping at the single output point cannot leave one of them behind.
        StampSourceProjectRelativePath(entries, skipped, unchangedMethods, projectRelativePath);

        string shimSource = ShimSourceEmitter.Emit(shimTypes);
        WorkerFileOutput fileOutput = new WorkerFileOutput
        {
            ProjectRelativePath = projectRelativePath,
            SourceContentSha256 = sourceContentSha256,
            ParseErrors = parseErrors.ToArray(),
            DeclarationDriftWarnings = declarationDriftWarnings.ToArray(),
            BaselineDisabledByDuplicateKeys = baseline.BaselineDisabledByDuplicateKeys,
            RemovedMembers = removedMembers.ToArray(),
            RemovedMethodSignatures = removedMethodSignatures.ToArray(),
            AddedFieldNames = addedFieldCatalog.ListRewrittenAddedFieldDisplayNames(projectRelativePath),
            AddedConstNames = addedFieldCatalog.ListFoldedConstDisplayNames(projectRelativePath)
        };
        return new WorkerOutput
        {
            ShimSource = shimSource,
            Entries = entries.ToArray(),
            Skipped = skipped.ToArray(),
            Files = new[] { fileOutput },
            SiblingConstDriftWarnings = siblingConstDriftWarnings.ToArray(),
            UnchangedMethods = unchangedMethods.ToArray(),
            HasAccessorDelegates = hasAccessorDelegates,
            HasAddedFieldRewrites = addedFieldCatalog.HasStoreRewrites
        };
    }

    // Records which edited file every worker row came from, right before the rows leave the worker.
    private static void StampSourceProjectRelativePath(
        List<WorkerEntry> entries,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        string projectRelativePath)
    {
        foreach (WorkerEntry entry in entries)
        {
            entry.SourceProjectRelativePath = projectRelativePath;
        }

        foreach (WorkerSkipped skippedRow in skipped)
        {
            skippedRow.SourceProjectRelativePath = projectRelativePath;
        }

        foreach (WorkerUnchangedMethod unchangedMethod in unchangedMethods)
        {
            unchangedMethod.SourceProjectRelativePath = projectRelativePath;
        }
    }

    internal static IEnumerable<TypeDeclarationSyntax> EnumerateTypeDeclarations(CompilationUnitSyntax root)
    {
        // Why interfaces: a new interface (including default methods) is absent from the compiled
        // assembly; enumerating it registers the type syntax key so the strip rewriter can drop
        // it instead of firing a false outside-body drift warning.
        return root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(static typeDeclaration =>
                typeDeclaration is ClassDeclarationSyntax
                || typeDeclaration is StructDeclarationSyntax
                || typeDeclaration is RecordDeclarationSyntax
                || typeDeclaration is InterfaceDeclarationSyntax);
    }
}
