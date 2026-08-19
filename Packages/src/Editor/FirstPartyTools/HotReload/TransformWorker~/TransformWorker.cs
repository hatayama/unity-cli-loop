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
        WorkerOutput output = TransformFile(input);
        WriteOutput(outputJsonPath, output);
        return 0;
    }

    private static WorkerInput ReadInput(string inputJsonPath)
    {
        byte[] bytes = File.ReadAllBytes(inputJsonPath);
        WorkerInput input = JsonSerializer.Deserialize<WorkerInput>(bytes, JsonOptions);
        if (input == null)
        {
            throw new InvalidOperationException("Failed to deserialize worker input JSON.");
        }

        if (string.IsNullOrEmpty(input.SourcePath))
        {
            throw new InvalidOperationException("sourcePath is required.");
        }

        input.Defines ??= Array.Empty<string>();
        input.ReferencePaths ??= Array.Empty<string>();
        input.ExcludedMethodKeys ??= Array.Empty<string>();
        input.ExcludedAddedMethodKeys ??= Array.Empty<string>();
        input.AssemblySourcePaths ??= Array.Empty<string>();
        return input;
    }

    private static void WriteOutput(string outputJsonPath, WorkerOutput output)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(output, JsonOptions);
        File.WriteAllBytes(outputJsonPath, bytes);
    }

    // Keep in sync with HotReloadOrchestrator.BuildMethodKey (Unity package side)
    // and HotReloadCallSiteScanner.CreateHit.
    // Why arity suffix: Caller(int) and Caller<T>(int) must not share a wire key.
    // Arity 0 keeps the bare name so existing non-generic keys stay stable.
    private static string BuildMethodKey(
        string typeMetadataName,
        string methodName,
        string[] parameterTypeFullNames,
        int genericArity)
    {
        string nameWithArity = methodName;
        if (genericArity > 0)
        {
            nameWithArity = methodName + "`" + genericArity.ToString(CultureInfo.InvariantCulture);
        }

        return typeMetadataName + "::" + nameWithArity + "("
            + string.Join(",", parameterTypeFullNames ?? Array.Empty<string>()) + ")";
    }

    private static WorkerOutput TransformFile(WorkerInput input)
    {
        WorkerOutput invalidPath = TryCreateInvalidPathOutput(input);
        if (invalidPath != null)
        {
            return invalidPath;
        }

        (WorkerOutput readFailure, string sourceText, string sourceContentSha256) = TryReadSourceText(input);
        if (readFailure != null)
        {
            return readFailure;
        }

        List<string> parseErrors = new List<string>();
        CSharpParseOptions parseOptions = new CSharpParseOptions(
            languageVersion: LanguageVersion.Latest,
            preprocessorSymbols: input.Defines);
        (SyntaxTree syntaxTree, CompilationUnitSyntax plainRoot) = WorkerSourceAnnotator.ParseAndAnnotateSource(
            sourceText,
            parseOptions,
            input.SourcePath,
            parseErrors);

        (List<MetadataReference> references, MetadataReference targetTypesReference) =
            CollectMetadataReferences(input, parseErrors);

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "UloopHotReloadTransformWorkerCompilation",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
        CompilationUnitSyntax root = syntaxTree.GetCompilationUnitRoot();

        IAssemblySymbol targetTypesAssemblySymbol = ResolveTargetTypesAssemblySymbol(
            compilation,
            targetTypesReference);
        List<string> declarationDriftWarnings = CollectConstDriftWarnings(
            root,
            semanticModel,
            targetTypesAssemblySymbol);
        // Why here: a compiled property/event can disappear or change kind with no
        // touched body, so the generic outside-body warning would bury the name.
        AppendCompiledPropertyOrEventKindChangeWarnings(
            root,
            semanticModel,
            targetTypesAssemblySymbol,
            declarationDriftWarnings);

        BaselineSnapshotState baseline = BuildBaselineSnapshotState(input, parseOptions, plainRoot);

        List<WorkerEntry> entries = new List<WorkerEntry>();
        List<WorkerSkipped> skipped = new List<WorkerSkipped>();
        List<WorkerUnchangedMethod> unchangedMethods = new List<WorkerUnchangedMethod>();
        List<WorkerRemovedMember> removedMembers = new List<WorkerRemovedMember>();
        List<WorkerRemovedMethodSignature> removedMethodSignatures = new List<WorkerRemovedMethodSignature>();
        List<ShimTypeBuilder> shimTypes = new List<ShimTypeBuilder>();
        int globalShimMethodCounter = 0;
        int shimTypeCounter = 0;
        List<UsingDirectiveSyntax> assemblyGlobalUsings =
            CollectAssemblyGlobalUsings(input, parseOptions);
        AddedMethodCatalog addedMethodCatalog = new AddedMethodCatalog();
        AddedFieldCatalog addedFieldCatalog = new AddedFieldCatalog();
        (List<TypeEmitState> typeEmitStates, int nextShimTypeCounter, int nextGlobalShimMethodCounter) =
            QueueAllTypeEmitStates(
                root,
                semanticModel,
                targetTypesAssemblySymbol,
                input,
                baseline,
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

        CollectRemovedMembersIfBaseline(
            baseline,
            plainRoot,
            typeEmitStates,
            semanticModel,
            targetTypesAssemblySymbol,
            addedMethodCatalog,
            addedFieldCatalog,
            removedMembers,
            removedMethodSignatures);

        SkipBodiesThatCannotUseAddedMethods(
            typeEmitStates,
            semanticModel,
            addedMethodCatalog,
            addedFieldCatalog,
            skipped);

        EmitQueuedMethodsAndPropertyGetters(
            typeEmitStates,
            semanticModel,
            addedMethodCatalog,
            addedFieldCatalog,
            root,
            input,
            baseline,
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
            AppendOutsideMethodBodyDriftWarningIfNeeded(
                baseline.SnapshotRoot,
                plainRoot,
                Path.GetFileName(input.SourcePath),
                declarationDriftWarnings,
                addedMethodCatalog,
                addedFieldCatalog);
        }

        return BuildWorkerOutput(
            root,
            input.ProjectRelativePath,
            shimTypes,
            entries,
            skipped,
            declarationDriftWarnings,
            parseErrors,
            unchangedMethods,
            baseline,
            removedMembers,
            removedMethodSignatures,
            addedFieldCatalog,
            sourceContentSha256);
    }

    private static WorkerOutput TryCreateInvalidPathOutput(WorkerInput input)
    {
        // Why ParseErrors (not Debug.Assert): ProjectRelativePath crosses a process boundary via
        // JSON, and the worker is built without a DEBUG define so Conditional Asserts are stripped.
        if (string.IsNullOrEmpty(input.ProjectRelativePath)
            || input.ProjectRelativePath.IndexOf('\\') >= 0
            || input.ProjectRelativePath.IndexOf('"') >= 0)
        {
            return new WorkerOutput
            {
                ShimSource = string.Empty,
                Entries = Array.Empty<WorkerEntry>(),
                Skipped = Array.Empty<WorkerSkipped>(),
                ParseErrors = new[]
                {
                    "Invalid projectRelativePath: must be a non-empty forward-slash path without quotes."
                }
            };
        }

        return null;
    }

    private static (WorkerOutput Failure, string SourceText, string SourceContentSha256) TryReadSourceText(
        WorkerInput input)
    {
        try
        {
            byte[] sourceBytes = File.ReadAllBytes(input.SourcePath);
            string sourceContentSha256 = ComputeSourceContentSha256(sourceBytes);
            using MemoryStream memoryStream = new MemoryStream(sourceBytes, writable: false);
            using StreamReader reader = new StreamReader(
                memoryStream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            return (null, reader.ReadToEnd(), sourceContentSha256);
        }
        catch (Exception exception)
        {
            return (
                new WorkerOutput
                {
                    ShimSource = string.Empty,
                    Entries = Array.Empty<WorkerEntry>(),
                    Skipped = Array.Empty<WorkerSkipped>(),
                    ParseErrors = new[] { "Failed to read sourcePath: " + exception.Message }
                },
                null,
                null);
        }
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

    private static BaselineSnapshotState BuildBaselineSnapshotState(
        WorkerInput input,
        CSharpParseOptions parseOptions,
        CompilationUnitSyntax plainRoot)
    {
        // Syntax-key maps for edited-method detection. Distinct from BuildMethodKey (Cecil names):
        // same-file old/new comparison only needs syntax keys to stay consistent with each other.
        BaselineSnapshotState baseline = new BaselineSnapshotState();
        // Null disables comparison; empty string is a real (empty) baseline text.
        if (input.SnapshotSource == null)
        {
            return baseline;
        }

        baseline.SnapshotRoot = CSharpSyntaxTree.ParseText(
                SourceText.From(input.SnapshotSource, Encoding.UTF8),
                parseOptions)
            .GetCompilationUnitRoot();
        Dictionary<string, MethodDeclarationSyntax> snapMethods =
            BuildSyntaxMethodMapOrNull(baseline.SnapshotRoot);
        // Why plainRoot: annotated current nodes break AreEquivalent for some shapes (see plainRoot above).
        Dictionary<string, MethodDeclarationSyntax> currentMethods = BuildSyntaxMethodMapOrNull(plainRoot);
        if (snapMethods == null || currentMethods == null)
        {
            // Why surface: previously a colliding key silently disabled baseline and patched all.
            baseline.BaselineDisabledByDuplicateKeys = true;
            return baseline;
        }

        // Why both maps: a duplicate key on either side makes AreEquivalent matching
        // ambiguous, so fail closed to no-baseline (patch all) instead of guessing.
        baseline.HasBaseline = true;
        baseline.SnapshotMethodMap = snapMethods;
        baseline.PlainCurrentMethodMap = currentMethods;
        // Why null is kept as-is: a colliding property/indexer key only disables accessor
        // gating for this file; method-level baseline matching still applies.
        baseline.SnapshotPropertyMap = BuildSyntaxPropertyMapOrNull(baseline.SnapshotRoot);
        baseline.SnapshotIndexerMap = BuildSyntaxIndexerMapOrNull(baseline.SnapshotRoot);
        baseline.SnapshotConstructorMap = BuildSyntaxConstructorMapOrNull(baseline.SnapshotRoot);
        baseline.SnapshotOperatorMap = BuildSyntaxOperatorMapOrNull(baseline.SnapshotRoot);
        baseline.SnapshotEventMap = BuildSyntaxEventMapOrNull(baseline.SnapshotRoot);
        baseline.PlainCurrentPropertyMap = BuildSyntaxPropertyMapOrNull(plainRoot);
        baseline.PlainCurrentIndexerMap = BuildSyntaxIndexerMapOrNull(plainRoot);
        baseline.PlainCurrentConstructorMap = BuildSyntaxConstructorMapOrNull(plainRoot);
        baseline.PlainCurrentOperatorMap = BuildSyntaxOperatorMapOrNull(plainRoot);
        baseline.PlainCurrentEventMap = BuildSyntaxEventMapOrNull(plainRoot);
        return baseline;
    }

    private static (List<TypeEmitState> TypeEmitStates, int ShimTypeCounter, int GlobalShimMethodCounter)
        QueueAllTypeEmitStates(
            CompilationUnitSyntax root,
            SemanticModel semanticModel,
            IAssemblySymbol targetTypesAssemblySymbol,
            WorkerInput input,
            BaselineSnapshotState baseline,
            List<UsingDirectiveSyntax> assemblyGlobalUsings,
            List<ShimTypeBuilder> shimTypes,
            AddedMethodCatalog addedMethodCatalog,
            AddedFieldCatalog addedFieldCatalog,
            List<WorkerSkipped> skipped,
            List<WorkerUnchangedMethod> unchangedMethods,
            List<string> declarationDriftWarnings,
            List<WorkerRemovedMember> removedMembers,
            List<WorkerRemovedMethodSignature> removedMethodSignatures,
            int shimTypeCounter,
            int globalShimMethodCounter)
    {
        List<TypeEmitState> typeEmitStates = new List<TypeEmitState>();
        foreach (TypeDeclarationSyntax typeDeclaration in EnumerateTypeDeclarations(root))
        {
            INamedTypeSymbol typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration);
            if (typeSymbol == null)
            {
                continue;
            }

            string typeMetadataNameFromSyntax = BuildTypeMetadataNameFromSyntax(typeDeclaration);

            // Property setters/init and all indexer accessors with bodies stay Skipped.
            // Property getters are patched below (not reported here).
            (Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
                Dictionary<string, IndexerDeclarationSyntax> snapshotIndexerMap,
                Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap,
                Dictionary<string, IndexerDeclarationSyntax> plainCurrentIndexerMap) =
                baseline.GetAccessorBaselineMaps();
            AppendExplicitAccessorSkips(
                typeDeclaration,
                typeMetadataNameFromSyntax,
                semanticModel,
                skipped,
                snapshotPropertyMap,
                snapshotIndexerMap,
                plainCurrentPropertyMap,
                plainCurrentIndexerMap,
                addedMethodCatalog);
            (Dictionary<string, ConstructorDeclarationSyntax> snapshotConstructorMap,
                Dictionary<string, MemberDeclarationSyntax> snapshotOperatorMap,
                Dictionary<string, EventDeclarationSyntax> snapshotEventMap,
                Dictionary<string, ConstructorDeclarationSyntax> plainCurrentConstructorMap,
                Dictionary<string, MemberDeclarationSyntax> plainCurrentOperatorMap,
                Dictionary<string, EventDeclarationSyntax> plainCurrentEventMap) =
                baseline.GetUnsupportedMemberBaselineMaps();
            AppendUnsupportedMemberKindSkips(
                typeDeclaration,
                typeMetadataNameFromSyntax,
                semanticModel,
                skipped,
                snapshotConstructorMap,
                snapshotOperatorMap,
                snapshotEventMap,
                plainCurrentConstructorMap,
                plainCurrentOperatorMap,
                plainCurrentEventMap);

            TypeEmitState typeState = new TypeEmitState
            {
                TypeDeclaration = typeDeclaration,
                TypeSymbol = typeSymbol,
                TypeMetadataNameFromSyntax = typeMetadataNameFromSyntax
            };
            (int nextShimTypeCounter, int nextGlobalShimMethodCounter) = QueueTypeMethods(
                typeState,
                semanticModel,
                targetTypesAssemblySymbol,
                input,
                baseline.HasBaseline,
                baseline.SnapshotMethodMap,
                baseline.PlainCurrentMethodMap,
                root,
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
            typeEmitStates.Add(typeState);
        }

        return (typeEmitStates, shimTypeCounter, globalShimMethodCounter);
    }

    private static void CollectRemovedMembersIfBaseline(
        BaselineSnapshotState baseline,
        CompilationUnitSyntax plainRoot,
        List<TypeEmitState> typeEmitStates,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures)
    {
        if (!baseline.HasBaseline)
        {
            return;
        }

        CollectRemovedMethods(
            baseline.SnapshotMethodMap,
            baseline.PlainCurrentMethodMap,
            addedMethodCatalog,
            removedMembers);
        CollectRemovedMethodSignaturesForDeletedNames(
            typeEmitStates,
            semanticModel,
            targetTypesAssemblySymbol,
            removedMembers,
            removedMethodSignatures);
        Dictionary<string, VariableDeclaratorSyntax> snapshotFieldMap =
            BuildSyntaxFieldMapOrNull(baseline.SnapshotRoot);
        Dictionary<string, VariableDeclaratorSyntax> currentFieldMap =
            BuildSyntaxFieldMapOrNull(plainRoot);
        if (snapshotFieldMap != null && currentFieldMap != null)
        {
            CollectRemovedFields(
                snapshotFieldMap,
                currentFieldMap,
                addedFieldCatalog,
                removedMembers);
        }
    }

    private static (int ShimTypeCounter, int GlobalShimMethodCounter) EmitQueuedMethodsAndPropertyGetters(
        List<TypeEmitState> typeEmitStates,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        CompilationUnitSyntax root,
        WorkerInput input,
        BaselineSnapshotState baseline,
        List<WorkerEntry> entries,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        List<ShimTypeBuilder> shimTypes,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        int shimTypeCounter,
        int globalShimMethodCounter)
    {
        foreach (TypeEmitState typeState in typeEmitStates)
        {
            EmitQueuedMethods(
                typeState,
                semanticModel,
                addedMethodCatalog,
                addedFieldCatalog,
                entries);
            (shimTypeCounter, globalShimMethodCounter) = EmitPropertyGettersForType(
                typeState,
                semanticModel,
                addedMethodCatalog,
                addedFieldCatalog,
                root,
                input,
                baseline,
                entries,
                skipped,
                unchangedMethods,
                shimTypes,
                assemblyGlobalUsings,
                shimTypeCounter,
                globalShimMethodCounter);
        }

        return (shimTypeCounter, globalShimMethodCounter);
    }

    private static (int ShimTypeCounter, int GlobalShimMethodCounter) EmitPropertyGettersForType(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        CompilationUnitSyntax root,
        WorkerInput input,
        BaselineSnapshotState baseline,
        List<WorkerEntry> entries,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        List<ShimTypeBuilder> shimTypes,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        int shimTypeCounter,
        int globalShimMethodCounter)
    {
        foreach (PropertyDeclarationSyntax propertyDeclaration in typeState.TypeDeclaration.Members
            .OfType<PropertyDeclarationSyntax>())
        {
            if (typeState.TypeIsAbsentFromCompiledAssembly)
            {
                SkipPropertyGetterOnUncompiledType(
                    propertyDeclaration,
                    semanticModel,
                    skipped);
                continue;
            }

            (ShimTypeBuilder nextShimType, int nextShimTypeCounter, int nextGlobalShimMethodCounter) =
                AppendPropertyGetterEntry(
                    propertyDeclaration,
                    typeState.TypeDeclaration,
                    typeState.TypeSymbol,
                    typeState.TypeMetadataNameFromSyntax,
                    semanticModel,
                    root,
                    input,
                    baseline.HasBaseline,
                    baseline.SnapshotPropertyMap,
                    baseline.PlainCurrentPropertyMap,
                    entries,
                    skipped,
                    unchangedMethods,
                    shimTypes,
                    shimTypeCounter,
                    globalShimMethodCounter,
                    typeState.CurrentShimType,
                    assemblyGlobalUsings,
                    addedMethodCatalog,
                    addedFieldCatalog);
            typeState.CurrentShimType = nextShimType;
            shimTypeCounter = nextShimTypeCounter;
            globalShimMethodCounter = nextGlobalShimMethodCounter;
        }

        return (shimTypeCounter, globalShimMethodCounter);
    }

    private static WorkerOutput BuildWorkerOutput(
        CompilationUnitSyntax root,
        string projectRelativePath,
        List<ShimTypeBuilder> shimTypes,
        List<WorkerEntry> entries,
        List<WorkerSkipped> skipped,
        List<string> declarationDriftWarnings,
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

        string shimSource = ShimSourceEmitter.Emit(root, shimTypes, projectRelativePath);
        return new WorkerOutput
        {
            ShimSource = shimSource,
            Entries = entries.ToArray(),
            Skipped = skipped.ToArray(),
            DeclarationDriftWarnings = declarationDriftWarnings.ToArray(),
            ParseErrors = parseErrors.ToArray(),
            UnchangedMethods = unchangedMethods.ToArray(),
            BaselineDisabledByDuplicateKeys = baseline.BaselineDisabledByDuplicateKeys,
            RemovedMembers = removedMembers.ToArray(),
            RemovedMethodSignatures = removedMethodSignatures.ToArray(),
            HasAccessorDelegates = hasAccessorDelegates,
            HasAddedFieldRewrites = addedFieldCatalog.HasStoreRewrites,
            AddedFieldNames = addedFieldCatalog.ListRewrittenAddedFieldDisplayNames(),
            SourceContentSha256 = sourceContentSha256
        };
    }

    // Keep in sync with HotReloadAppliedSourceLedger.ComputeContentHash (lowercase hex SHA-256).
    private static string ComputeSourceContentSha256(byte[] bytes)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(bytes);
        StringBuilder builder = new StringBuilder(hash.Length * 2);
        for (int index = 0; index < hash.Length; index++)
        {
            builder.Append(hash[index].ToString("x2"));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Attaches original-source 1-based line annotations to every method and statement in the
    /// parsed tree. Must run before compilation so the SemanticModel binds the annotated tree.
    /// </summary>
    // What: direct one-shot Unity lifecycle note only. Indirect "only called from Awake"
    // notes were dropped — syntax-only caller walks cannot prove that claim (ctors,
    // accessors, lambdas, other types in the same file).
    private static string ComputeLifecycleNote(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol typeSymbol)
    {
        string methodName = methodDeclaration.Identifier.Text;
        if (!IsOneShotLifecycleMethodName(methodName))
        {
            return null;
        }

        if (!IsUnityEngineMonoBehaviourDerived(typeSymbol))
        {
            return null;
        }

        // Why private void (): Unity message methods are instance void with no parameters;
        // public/static/parameterized Start() on a MonoBehaviour is not the lifecycle hook.
        if (methodSymbol.DeclaredAccessibility != Accessibility.Private
            || methodSymbol.IsStatic
            || !methodSymbol.ReturnsVoid
            || methodSymbol.Parameters.Length != 0)
        {
            return null;
        }

        return string.Format(LifecycleNotes.DirectFormat, methodName);
    }

    private static bool IsOneShotLifecycleMethodName(string methodName)
    {
        for (int index = 0; index < LifecycleNotes.OneShotLifecycleMethodNames.Length; index++)
        {
            if (LifecycleNotes.OneShotLifecycleMethodNames[index] == methodName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnityEngineMonoBehaviourDerived(INamedTypeSymbol typeSymbol)
    {
        INamedTypeSymbol current = typeSymbol;
        while (current != null)
        {
            if (current.Name == "MonoBehaviour"
                && current.ContainingNamespace != null
                && current.ContainingNamespace.ToDisplayString() == "UnityEngine")
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol)
    {
        List<string> warnings = new List<string>();
        if (targetTypesAssemblySymbol == null)
        {
            return warnings;
        }

        HashSet<string> seenTypeMetadataNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (BaseTypeDeclarationSyntax typeDeclaration
            in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            INamedTypeSymbol sourceType = semanticModel.GetDeclaredSymbol(typeDeclaration);
            if (sourceType == null)
            {
                continue;
            }

            // Partial declarations in one file resolve to the same merged type symbol, and
            // comparing its members once per declaration would duplicate every warning.
            string typeMetadataName = ToReflectionMetadataName(sourceType);
            if (!seenTypeMetadataNames.Add(typeMetadataName))
            {
                continue;
            }

            INamedTypeSymbol compiledType = targetTypesAssemblySymbol.GetTypeByMetadataName(
                typeMetadataName);
            if (compiledType == null)
            {
                continue;
            }

            foreach (IFieldSymbol sourceField in sourceType.GetMembers().OfType<IFieldSymbol>())
            {
                if (!sourceField.HasConstantValue)
                {
                    continue;
                }

                IFieldSymbol compiledField = null;
                foreach (ISymbol member in compiledType.GetMembers(sourceField.Name))
                {
                    if (member is IFieldSymbol candidate && candidate.HasConstantValue)
                    {
                        compiledField = candidate;
                        break;
                    }
                }

                if (compiledField == null)
                {
                    // A const missing from the compiled assembly is a new declaration, not a
                    // drift; bodies reading it already fail shim compilation with their own
                    // actionable error.
                    continue;
                }

                if (Equals(sourceField.ConstantValue, compiledField.ConstantValue))
                {
                    continue;
                }

                warnings.Add(
                    "const " + sourceType.ToDisplayString() + "." + sourceField.Name
                    + " is " + FormatConstValue(sourceField.ConstantValue)
                    + " in the edited source but " + FormatConstValue(compiledField.ConstantValue)
                    + " in the compiled assembly; edits outside method bodies never take effect "
                    + "through hot reload. Run 'uloop compile' to apply this change.");
            }
        }

        return warnings;
    }

    /// <summary>
    /// Builds the CLR reflection metadata name ('+' for nested types) that
    /// IAssemblySymbol.GetTypeByMetadataName expects. CecilTypeNames.ToMetadataName cannot be
    /// reused here because Cecil separates nested types with '/'.
    /// </summary>
    private static string ToReflectionMetadataName(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.ContainingType != null)
        {
            return ToReflectionMetadataName(typeSymbol.ContainingType) + "+" + typeSymbol.MetadataName;
        }

        if (typeSymbol.ContainingNamespace == null || typeSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            return typeSymbol.MetadataName;
        }

        return typeSymbol.ContainingNamespace.ToDisplayString() + "." + typeSymbol.MetadataName;
    }

    /// <summary>
    /// Renders a const value for the drift warning: quoted for strings and chars, "null" for
    /// null, invariant-culture text otherwise.
    /// </summary>
    private static string FormatConstValue(object value)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is string text)
        {
            return "\"" + text + "\"";
        }

        if (value is char character)
        {
            // A bare char (especially whitespace) is invisible inside the warning sentence;
            // quote it the way C# source spells it.
            return "'" + character + "'";
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private const string CompiledPropertyKindChangeWarningFormat =
        "Compiled property '{0}' was removed or redeclared as a different member kind in the edited source; the compiled member stays until 'uloop compile'.";

    private const string CompiledEventKindChangeWarningFormat =
        "Compiled event '{0}' was removed or redeclared as a different member kind in the edited source; the compiled member stays until 'uloop compile'.";

    /// <summary>
    /// What: names compiled properties and events that the edited source deleted or
    /// redeclared as another member kind, even when no method body changed.
    /// </summary>
    private static void AppendCompiledPropertyOrEventKindChangeWarnings(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol,
        List<string> warnings)
    {
        if (targetTypesAssemblySymbol == null)
        {
            return;
        }

        HashSet<string> seenTypeMetadataNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (BaseTypeDeclarationSyntax typeDeclaration
            in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            // Why syntax PartialKeyword: the worker compilation sees only this file. A
            // compiled property or event declared in another partial file is absent from
            // the source symbol and would look permanently removed. Locations cannot be
            // used — metadata symbols have no source locations.
            if (typeDeclaration is TypeDeclarationSyntax typedDeclaration
                && typedDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                continue;
            }

            INamedTypeSymbol sourceType = semanticModel.GetDeclaredSymbol(typeDeclaration);
            if (sourceType == null)
            {
                continue;
            }

            string typeMetadataName = ToReflectionMetadataName(sourceType);
            if (!seenTypeMetadataNames.Add(typeMetadataName))
            {
                continue;
            }

            INamedTypeSymbol compiledType = targetTypesAssemblySymbol.GetTypeByMetadataName(
                typeMetadataName);
            if (compiledType == null)
            {
                continue;
            }

            AppendMissingCompiledPropertyOrEventWarnings(compiledType, sourceType, warnings);
        }
    }

    private static void AppendMissingCompiledPropertyOrEventWarnings(
        INamedTypeSymbol compiledType,
        INamedTypeSymbol sourceType,
        List<string> warnings)
    {
        foreach (ISymbol compiledMember in compiledType.GetMembers())
        {
            string warning = TryFormatMissingCompiledPropertyOrEventWarning(compiledMember, sourceType);
            if (warning == null)
            {
                continue;
            }

            warnings.Add(warning);
        }
    }

    private static string TryFormatMissingCompiledPropertyOrEventWarning(
        ISymbol compiledMember,
        INamedTypeSymbol sourceType)
    {
        // Why still check IsImplicitlyDeclared: source-compiled symbols can be implicit.
        // Metadata symbols from the PE almost always report false, so this is best-effort
        // and does not filter compiler-generated members out of the compiled assembly.
        if (compiledMember is IPropertySymbol property
            && !property.IsIndexer
            && !property.IsImplicitlyDeclared
            && !SourceDeclaresProperty(sourceType, property.Name))
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                CompiledPropertyKindChangeWarningFormat,
                sourceType.ToDisplayString() + "." + property.Name);
        }

        if (compiledMember is IEventSymbol compiledEvent
            && !compiledEvent.IsImplicitlyDeclared
            && !SourceDeclaresEvent(sourceType, compiledEvent.Name))
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                CompiledEventKindChangeWarningFormat,
                sourceType.ToDisplayString() + "." + compiledEvent.Name);
        }

        return null;
    }

    private static bool SourceDeclaresProperty(INamedTypeSymbol sourceType, string name)
    {
        foreach (ISymbol member in sourceType.GetMembers(name))
        {
            if (member is IPropertySymbol property && !property.IsIndexer)
            {
                return true;
            }
        }

        return false;
    }

    private static bool SourceDeclaresEvent(INamedTypeSymbol sourceType, string name)
    {
        foreach (ISymbol member in sourceType.GetMembers(name))
        {
            if (member is IEventSymbol)
            {
                return true;
            }
        }

        return false;
    }

    private const string ExplicitAccessorSkipReason =
        "Property setter, init, or indexer accessors are out of scope for v1; "
        + "run 'uloop compile' to apply accessor edits.";

    private const string UnsupportedMemberKindSkipReason =
        "Constructors, operators, and event accessors are out of scope for v1; "
        + "run 'uloop compile' to apply these edits.";

    private const string OutsideMethodBodyDriftWarningFormat =
        "Edits outside method bodies in {0} (fields, initializers, or attributes) are not applied by hot reload; run uloop compile to pick them up.";

    // Syntax-based method key for same-file snapshot vs current comparison. Do not mix with
    // BuildMethodKey (Cecil/metadata names used by the orchestrator exclusion path).
    // Used only for in-memory baseline maps — safe to evolve without wire compatibility concerns.
    private static string BuildSyntaxMethodKey(string typeMetadataName, MethodDeclarationSyntax methodDeclaration)
    {
        List<string> parameterKeys = new List<string>();
        if (methodDeclaration.ParameterList != null)
        {
            foreach (ParameterSyntax parameter in methodDeclaration.ParameterList.Parameters)
            {
                parameterKeys.Add(BuildSyntaxParameterTypeKey(parameter));
            }
        }

        // Why arity suffix: void F(int) and void F<T>(int) must not share a key (was silent
        // baseline-disable). Arity 0 keeps the bare name so existing non-generic keys stay stable.
        string methodName = methodDeclaration.Identifier.Text;
        if (methodDeclaration.TypeParameterList != null
            && methodDeclaration.TypeParameterList.Parameters.Count > 0)
        {
            methodName += "`"
                + methodDeclaration.TypeParameterList.Parameters.Count.ToString(CultureInfo.InvariantCulture);
        }

        // Why explicit-interface qualifier: IA.Run() and IB.Run() must not share a key (same as
        // BuildSyntaxPropertyKey). Property keys already include ExplicitInterfaceSpecifier.
        if (methodDeclaration.ExplicitInterfaceSpecifier != null)
        {
            methodName = methodDeclaration.ExplicitInterfaceSpecifier.Name.NormalizeWhitespace().ToString()
                + "." + methodName;
        }

        return typeMetadataName + "::" + methodName + "("
            + string.Join(",", parameterKeys) + ")";
    }

    // Keep in sync with HotReloadAddedFieldStore.FormatFieldKey / FieldKeySeparator.
    private static string BuildSyntaxFieldKey(string typeMetadataName, string fieldName)
    {
        return typeMetadataName + TransformWorkerProgramMarker.AddedFieldKeySeparator + fieldName;
    }

    private static string BuildSyntaxParameterTypeKey(ParameterSyntax parameter)
    {
        // Why NormalizeWhitespace: trivia / spacing differences must not invent distinct keys.
        string typeText = parameter.Type != null
            ? parameter.Type.NormalizeWhitespace().ToString()
            : string.Empty;
        if (parameter.Modifiers.Any(SyntaxKind.RefKeyword)
            || parameter.Modifiers.Any(SyntaxKind.OutKeyword)
            || parameter.Modifiers.Any(SyntaxKind.InKeyword))
        {
            typeText += "&";
        }

        return typeText;
    }

    // What: syntax-only type metadata name for baseline signature keys (not shim naming).
    private static string BuildTypeMetadataNameFromSyntax(TypeDeclarationSyntax typeDeclaration)
    {
        List<string> nestedNames = new List<string>();
        TypeDeclarationSyntax current = typeDeclaration;
        while (current != null)
        {
            string simpleName = current.Identifier.Text;
            if (current.TypeParameterList != null && current.TypeParameterList.Parameters.Count > 0)
            {
                simpleName += "`" + current.TypeParameterList.Parameters.Count.ToString(CultureInfo.InvariantCulture);
            }

            nestedNames.Add(simpleName);
            current = current.Parent as TypeDeclarationSyntax;
        }

        nestedNames.Reverse();
        string typeMetadataName = string.Join("+", nestedNames);

        string namespaceName = GetContainingNamespaceName(typeDeclaration);
        if (string.IsNullOrEmpty(namespaceName))
        {
            return typeMetadataName;
        }

        return namespaceName + "." + typeMetadataName;
    }

    // What: dotted namespace path including all ancestor namespaces (not only the innermost).
    private static string GetContainingNamespaceName(SyntaxNode node)
    {
        List<string> parts = new List<string>();
        SyntaxNode current = node.Parent;
        while (current != null)
        {
            // Why NormalizeWhitespace: trivia in nested namespace names must not invent distinct keys.
            if (current is NamespaceDeclarationSyntax namespaceDeclaration)
            {
                parts.Add(namespaceDeclaration.Name.NormalizeWhitespace().ToString());
            }
            else if (current is FileScopedNamespaceDeclarationSyntax fileScopedNamespace)
            {
                parts.Add(fileScopedNamespace.Name.NormalizeWhitespace().ToString());
            }

            current = current.Parent;
        }

        if (parts.Count == 0)
        {
            return string.Empty;
        }

        parts.Reverse();
        return string.Join(".", parts);
    }

    private static string BuildSyntaxPropertyKey(
        string typeMetadataName,
        PropertyDeclarationSyntax propertyDeclaration)
    {
        string name = propertyDeclaration.Identifier.Text;
        if (propertyDeclaration.ExplicitInterfaceSpecifier != null)
        {
            // Why NormalizeWhitespace: keep property keys symmetric with BuildSyntaxMethodKey so
            // trivia in the interface name cannot invent a distinct baseline key.
            name = propertyDeclaration.ExplicitInterfaceSpecifier.Name.NormalizeWhitespace().ToString()
                + "." + name;
        }

        return typeMetadataName + "::" + name;
    }

    private static string BuildSyntaxIndexerKey(
        string typeMetadataName,
        IndexerDeclarationSyntax indexerDeclaration)
    {
        List<string> parameterKeys = new List<string>();
        if (indexerDeclaration.ParameterList != null)
        {
            foreach (ParameterSyntax parameter in indexerDeclaration.ParameterList.Parameters)
            {
                parameterKeys.Add(BuildSyntaxParameterTypeKey(parameter));
            }
        }

        return typeMetadataName + "::this(" + string.Join(",", parameterKeys) + ")";
    }

    private static string BuildSyntaxConstructorKey(
        string typeMetadataName,
        ConstructorDeclarationSyntax constructorDeclaration)
    {
        List<string> parameterKeys = new List<string>();
        if (constructorDeclaration.ParameterList != null)
        {
            foreach (ParameterSyntax parameter in constructorDeclaration.ParameterList.Parameters)
            {
                parameterKeys.Add(BuildSyntaxParameterTypeKey(parameter));
            }
        }

        string name = constructorDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword)
            ? ".cctor"
            : ".ctor";
        return typeMetadataName + "::" + name + "(" + string.Join(",", parameterKeys) + ")";
    }

    private static string BuildSyntaxOperatorKey(
        string typeMetadataName,
        OperatorDeclarationSyntax operatorDeclaration)
    {
        List<string> parameterKeys = new List<string>();
        if (operatorDeclaration.ParameterList != null)
        {
            foreach (ParameterSyntax parameter in operatorDeclaration.ParameterList.Parameters)
            {
                parameterKeys.Add(BuildSyntaxParameterTypeKey(parameter));
            }
        }

        return typeMetadataName + "::" + operatorDeclaration.OperatorToken.ValueText
            + "(" + string.Join(",", parameterKeys) + ")";
    }

    private static string BuildSyntaxConversionOperatorKey(
        string typeMetadataName,
        ConversionOperatorDeclarationSyntax conversionDeclaration)
    {
        List<string> parameterKeys = new List<string>();
        if (conversionDeclaration.ParameterList != null)
        {
            foreach (ParameterSyntax parameter in conversionDeclaration.ParameterList.Parameters)
            {
                parameterKeys.Add(BuildSyntaxParameterTypeKey(parameter));
            }
        }

        string targetType = conversionDeclaration.Type != null
            ? conversionDeclaration.Type.NormalizeWhitespace().ToString()
            : string.Empty;
        return typeMetadataName + "::" + conversionDeclaration.ImplicitOrExplicitKeyword.ValueText
            + "->" + targetType + "(" + string.Join(",", parameterKeys) + ")";
    }

    private static string BuildSyntaxEventKey(
        string typeMetadataName,
        EventDeclarationSyntax eventDeclaration)
    {
        string name = eventDeclaration.Identifier.Text;
        if (eventDeclaration.ExplicitInterfaceSpecifier != null)
        {
            name = eventDeclaration.ExplicitInterfaceSpecifier.Name.NormalizeWhitespace().ToString()
                + "." + name;
        }

        return typeMetadataName + "::" + name;
    }

    private static Dictionary<string, MethodDeclarationSyntax> BuildSyntaxMethodMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, MethodDeclarationSyntax> map = new Dictionary<string, MethodDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (MethodDeclarationSyntax methodDeclaration in typeDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                string key = BuildSyntaxMethodKey(typeMetadataName, methodDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = methodDeclaration;
            }
        }

        return map;
    }

    private static Dictionary<string, VariableDeclaratorSyntax> BuildSyntaxFieldMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, VariableDeclaratorSyntax> map =
            new Dictionary<string, VariableDeclaratorSyntax>(StringComparer.Ordinal);
        foreach (TypeDeclarationSyntax typeDeclaration in EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (FieldDeclarationSyntax fieldDeclaration in typeDeclaration.Members
                .OfType<FieldDeclarationSyntax>())
            {
                foreach (VariableDeclaratorSyntax variable in fieldDeclaration.Declaration.Variables)
                {
                    string key = BuildSyntaxFieldKey(typeMetadataName, variable.Identifier.Text);
                    if (map.ContainsKey(key))
                    {
                        return null;
                    }

                    map[key] = variable;
                }
            }
        }

        return map;
    }

    private static Dictionary<string, PropertyDeclarationSyntax> BuildSyntaxPropertyMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, PropertyDeclarationSyntax> map = new Dictionary<string, PropertyDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (PropertyDeclarationSyntax propertyDeclaration in typeDeclaration.Members.OfType<PropertyDeclarationSyntax>())
            {
                string key = BuildSyntaxPropertyKey(typeMetadataName, propertyDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = propertyDeclaration;
            }
        }

        return map;
    }

    private static Dictionary<string, IndexerDeclarationSyntax> BuildSyntaxIndexerMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, IndexerDeclarationSyntax> map = new Dictionary<string, IndexerDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (IndexerDeclarationSyntax indexerDeclaration in typeDeclaration.Members.OfType<IndexerDeclarationSyntax>())
            {
                string key = BuildSyntaxIndexerKey(typeMetadataName, indexerDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = indexerDeclaration;
            }
        }

        return map;
    }

    private static Dictionary<string, ConstructorDeclarationSyntax> BuildSyntaxConstructorMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, ConstructorDeclarationSyntax> map =
            new Dictionary<string, ConstructorDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (ConstructorDeclarationSyntax constructorDeclaration in typeDeclaration.Members
                .OfType<ConstructorDeclarationSyntax>())
            {
                string key = BuildSyntaxConstructorKey(typeMetadataName, constructorDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = constructorDeclaration;
            }
        }

        return map;
    }

    private static Dictionary<string, MemberDeclarationSyntax> BuildSyntaxOperatorMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, MemberDeclarationSyntax> map =
            new Dictionary<string, MemberDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (MemberDeclarationSyntax member in typeDeclaration.Members)
            {
                string key = TryBuildSyntaxOperatorMemberKey(typeMetadataName, member);
                if (key == null)
                {
                    continue;
                }

                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = member;
            }
        }

        return map;
    }

    private static string TryBuildSyntaxOperatorMemberKey(
        string typeMetadataName,
        MemberDeclarationSyntax member)
    {
        if (member is OperatorDeclarationSyntax operatorDeclaration)
        {
            return BuildSyntaxOperatorKey(typeMetadataName, operatorDeclaration);
        }

        if (member is ConversionOperatorDeclarationSyntax conversionDeclaration)
        {
            return BuildSyntaxConversionOperatorKey(typeMetadataName, conversionDeclaration);
        }

        return null;
    }

    private static Dictionary<string, EventDeclarationSyntax> BuildSyntaxEventMapOrNull(
        CompilationUnitSyntax root)
    {
        Dictionary<string, EventDeclarationSyntax> map =
            new Dictionary<string, EventDeclarationSyntax>();
        foreach (TypeDeclarationSyntax typeDeclaration in EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (EventDeclarationSyntax eventDeclaration in typeDeclaration.Members
                .OfType<EventDeclarationSyntax>())
            {
                string key = BuildSyntaxEventKey(typeMetadataName, eventDeclaration);
                if (map.ContainsKey(key))
                {
                    return null;
                }

                map[key] = eventDeclaration;
            }
        }

        return map;
    }

    private static void AppendOutsideMethodBodyDriftWarningIfNeeded(
        CompilationUnitSyntax snapshotRoot,
        CompilationUnitSyntax currentRoot,
        string fileName,
        List<string> declarationDriftWarnings,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        HashSet<string> snapshotKeys = new HashSet<string>(
            addedMethodCatalog.RemovedSyntaxKeys,
            StringComparer.Ordinal);
        foreach (string key in addedFieldCatalog.RemovedSyntaxKeys)
        {
            snapshotKeys.Add(key);
        }

        HashSet<string> currentKeys = new HashSet<string>(
            addedMethodCatalog.AddedSyntaxKeys,
            StringComparer.Ordinal);
        foreach (string key in addedFieldCatalog.AddedSyntaxKeys)
        {
            currentKeys.Add(key);
        }

        StripHandledMemberDeclarationsRewriter stripSnapshot =
            new StripHandledMemberDeclarationsRewriter(
                snapshotKeys,
                Array.Empty<string>(),
                Array.Empty<string>());
        StripHandledMemberDeclarationsRewriter stripCurrent =
            new StripHandledMemberDeclarationsRewriter(
                currentKeys,
                addedMethodCatalog.AddedTypeSyntaxKeys,
                addedMethodCatalog.AddedPropertySyntaxKeys);
        StripMethodBodiesRewriter bodyStripper = new StripMethodBodiesRewriter();
        SyntaxNode strippedSnapshot = bodyStripper.Visit(stripSnapshot.Visit(snapshotRoot));
        SyntaxNode strippedCurrent = bodyStripper.Visit(stripCurrent.Visit(currentRoot));
        if (!SyntaxFactory.AreEquivalent(strippedSnapshot, strippedCurrent, topLevel: false))
        {
            declarationDriftWarnings.Add(
                string.Format(CultureInfo.InvariantCulture, OutsideMethodBodyDriftWarningFormat, fileName));
        }
    }

    private sealed class StripHandledMemberDeclarationsRewriter : CSharpSyntaxRewriter
    {
        private readonly HashSet<string> _syntaxKeysToStrip;
        private readonly HashSet<string> _typeSyntaxKeysToStrip;
        private readonly HashSet<string> _propertySyntaxKeysToStrip;

        public StripHandledMemberDeclarationsRewriter(
            IReadOnlyCollection<string> syntaxKeysToStrip,
            IReadOnlyCollection<string> typeSyntaxKeysToStrip,
            IReadOnlyCollection<string> propertySyntaxKeysToStrip)
        {
            _syntaxKeysToStrip = new HashSet<string>(syntaxKeysToStrip, StringComparer.Ordinal);
            _typeSyntaxKeysToStrip = new HashSet<string>(
                typeSyntaxKeysToStrip ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            _propertySyntaxKeysToStrip = new HashSet<string>(
                propertySyntaxKeysToStrip ?? Array.Empty<string>(),
                StringComparer.Ordinal);
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (ShouldStripType(node))
            {
                return null;
            }

            return base.VisitClassDeclaration(node);
        }

        public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node)
        {
            if (ShouldStripType(node))
            {
                return null;
            }

            return base.VisitStructDeclaration(node);
        }

        public override SyntaxNode VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            if (ShouldStripType(node))
            {
                return null;
            }

            return base.VisitRecordDeclaration(node);
        }

        public override SyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            if (ShouldStripType(node))
            {
                return null;
            }

            return base.VisitInterfaceDeclaration(node);
        }

        private bool ShouldStripType(TypeDeclarationSyntax node)
        {
            return _typeSyntaxKeysToStrip.Contains(BuildTypeMetadataNameFromSyntax(node));
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            TypeDeclarationSyntax typeDeclaration = node.Parent as TypeDeclarationSyntax;
            if (typeDeclaration == null)
            {
                return base.VisitMethodDeclaration(node);
            }

            string syntaxKey = BuildSyntaxMethodKey(
                BuildTypeMetadataNameFromSyntax(typeDeclaration),
                node);
            if (_syntaxKeysToStrip.Contains(syntaxKey))
            {
                return null;
            }

            return base.VisitMethodDeclaration(node);
        }

        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            TypeDeclarationSyntax typeDeclaration = node.Parent as TypeDeclarationSyntax;
            if (typeDeclaration == null)
            {
                return base.VisitPropertyDeclaration(node);
            }

            string syntaxKey = BuildSyntaxPropertyKey(
                BuildTypeMetadataNameFromSyntax(typeDeclaration),
                node);
            if (_propertySyntaxKeysToStrip.Contains(syntaxKey))
            {
                return null;
            }

            return base.VisitPropertyDeclaration(node);
        }

        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            TypeDeclarationSyntax typeDeclaration = node.Parent as TypeDeclarationSyntax;
            if (typeDeclaration == null)
            {
                return base.VisitFieldDeclaration(node);
            }

            string typeMetadataName = BuildTypeMetadataNameFromSyntax(typeDeclaration);
            List<VariableDeclaratorSyntax> remaining = new List<VariableDeclaratorSyntax>();
            foreach (VariableDeclaratorSyntax variable in node.Declaration.Variables)
            {
                string syntaxKey = BuildSyntaxFieldKey(typeMetadataName, variable.Identifier.Text);
                if (!_syntaxKeysToStrip.Contains(syntaxKey))
                {
                    remaining.Add(variable);
                }
            }

            if (remaining.Count == 0)
            {
                return null;
            }

            if (remaining.Count == node.Declaration.Variables.Count)
            {
                return base.VisitFieldDeclaration(node);
            }

            return node.WithDeclaration(
                node.Declaration.WithVariables(SyntaxFactory.SeparatedList(remaining)));
        }
    }

    private sealed class StripMethodBodiesRewriter : CSharpSyntaxRewriter
    {
        // Using directives never change patched behavior: CollectUsingsForType copies the edited
        // file's usings into every shim, so comparing them here only produces false drift warnings
        // for using-only edits. extern alias declarations stay compared (not copied into shims).
        public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node)
        {
            return null;
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            MethodDeclarationSyntax visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
            if (visited.Body == null && visited.ExpressionBody == null)
            {
                return visited;
            }

            return visited
                .WithExpressionBody(null)
                .WithSemicolonToken(default(SyntaxToken))
                .WithBody(SyntaxFactory.Block());
        }

        // Why strip getters only: patched getter edits must not look like outside-body drift.
        // Setter/init/indexer bodies stay so those still-unapplied edits keep the warning.
        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            PropertyDeclarationSyntax visited = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node);
            if (visited.ExpressionBody != null)
            {
                return visited
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default(SyntaxToken))
                    .WithAccessorList(
                        SyntaxFactory.AccessorList(
                            SyntaxFactory.SingletonList(
                                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                    .WithBody(SyntaxFactory.Block()))));
            }

            if (visited.AccessorList == null)
            {
                return visited;
            }

            List<AccessorDeclarationSyntax> accessors = new List<AccessorDeclarationSyntax>();
            foreach (AccessorDeclarationSyntax accessor in visited.AccessorList.Accessors)
            {
                if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration)
                    && (accessor.Body != null || accessor.ExpressionBody != null))
                {
                    accessors.Add(
                        accessor
                            .WithExpressionBody(null)
                            .WithSemicolonToken(default(SyntaxToken))
                            .WithBody(SyntaxFactory.Block()));
                }
                else
                {
                    accessors.Add(accessor);
                }
            }

            return visited.WithAccessorList(
                SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));
        }

        // Why strip ctor/operator/event-accessor bodies: those members are reported as
        // per-member Skipped, so a body-only edit must not also look like outside-body drift.
        // Signature, attributes, and constructor initializers stay so those edits still warn.
        public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            ConstructorDeclarationSyntax visited =
                (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node);
            if (visited.Body == null && visited.ExpressionBody == null)
            {
                return visited;
            }

            return visited
                .WithExpressionBody(null)
                .WithSemicolonToken(default(SyntaxToken))
                .WithBody(SyntaxFactory.Block());
        }

        public override SyntaxNode VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            OperatorDeclarationSyntax visited = (OperatorDeclarationSyntax)base.VisitOperatorDeclaration(node);
            if (visited.Body == null && visited.ExpressionBody == null)
            {
                return visited;
            }

            return visited
                .WithExpressionBody(null)
                .WithSemicolonToken(default(SyntaxToken))
                .WithBody(SyntaxFactory.Block());
        }

        public override SyntaxNode VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
        {
            ConversionOperatorDeclarationSyntax visited =
                (ConversionOperatorDeclarationSyntax)base.VisitConversionOperatorDeclaration(node);
            if (visited.Body == null && visited.ExpressionBody == null)
            {
                return visited;
            }

            return visited
                .WithExpressionBody(null)
                .WithSemicolonToken(default(SyntaxToken))
                .WithBody(SyntaxFactory.Block());
        }

        public override SyntaxNode VisitEventDeclaration(EventDeclarationSyntax node)
        {
            EventDeclarationSyntax visited = (EventDeclarationSyntax)base.VisitEventDeclaration(node);
            if (visited.AccessorList == null)
            {
                return visited;
            }

            List<AccessorDeclarationSyntax> accessors = new List<AccessorDeclarationSyntax>();
            foreach (AccessorDeclarationSyntax accessor in visited.AccessorList.Accessors)
            {
                if (accessor.Body == null && accessor.ExpressionBody == null)
                {
                    accessors.Add(accessor);
                    continue;
                }

                accessors.Add(
                    accessor
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default(SyntaxToken))
                        .WithBody(SyntaxFactory.Block()));
            }

            return visited.WithAccessorList(
                SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));
        }

        // Why strip const initializers: const drift has its own warning with both values;
        // leaving EqualsValueClause here would also trip the generic outside-body warning.
        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            FieldDeclarationSyntax visited = (FieldDeclarationSyntax)base.VisitFieldDeclaration(node);
            bool isConst = false;
            foreach (SyntaxToken modifier in visited.Modifiers)
            {
                if (modifier.IsKind(SyntaxKind.ConstKeyword))
                {
                    isConst = true;
                    break;
                }
            }

            if (!isConst)
            {
                return visited;
            }

            List<VariableDeclaratorSyntax> declarators = new List<VariableDeclaratorSyntax>();
            foreach (VariableDeclaratorSyntax declarator in visited.Declaration.Variables)
            {
                declarators.Add(declarator.WithInitializer(null));
            }

            return visited.WithDeclaration(
                visited.Declaration.WithVariables(SyntaxFactory.SeparatedList(declarators)));
        }

        // Why strip enum member values: enum constants use the same dedicated const-drift path.
        public override SyntaxNode VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node)
        {
            EnumMemberDeclarationSyntax visited =
                (EnumMemberDeclarationSyntax)base.VisitEnumMemberDeclaration(node);
            if (visited.EqualsValue == null)
            {
                return visited;
            }

            return visited.WithEqualsValue(null);
        }
    }

    // What: reports each property/indexer accessor that has an explicit body as Skipped.
    // Auto-properties ({ get; set; }) have no body and are not listed.
    // When a verified snapshot declares an equivalent property/indexer, skip rows are omitted
    // (unchanged accessors must not appear as Skipped noise).
    private static void AppendExplicitAccessorSkips(
        TypeDeclarationSyntax typeDeclaration,
        string typeMetadataNameFromSyntax,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped,
        Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
        Dictionary<string, IndexerDeclarationSyntax> snapshotIndexerMap,
        Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap,
        Dictionary<string, IndexerDeclarationSyntax> plainCurrentIndexerMap,
        AddedMethodCatalog addedMethodCatalog)
    {
        foreach (MemberDeclarationSyntax member in typeDeclaration.Members)
        {
            if (member is PropertyDeclarationSyntax propertyDeclaration)
            {
                string propertyKey = BuildSyntaxPropertyKey(typeMetadataNameFromSyntax, propertyDeclaration);
                // Why plainCurrentPropertyMap: annotated property nodes break AreEquivalent the
                // same way annotated method bodies do; compare unannotated peers only.
                if (snapshotPropertyMap != null
                    && plainCurrentPropertyMap != null
                    && snapshotPropertyMap.TryGetValue(
                        propertyKey,
                        out PropertyDeclarationSyntax snapshotProperty)
                    && plainCurrentPropertyMap.TryGetValue(
                        propertyKey,
                        out PropertyDeclarationSyntax plainProperty)
                    && SyntaxFactory.AreEquivalent(snapshotProperty, plainProperty, topLevel: false))
                {
                    continue;
                }

                AppendExplicitAccessorSkipsForProperty(
                    propertyDeclaration,
                    semanticModel.GetDeclaredSymbol(propertyDeclaration),
                    skipped,
                    typeMetadataNameFromSyntax,
                    snapshotPropertyMap,
                    addedMethodCatalog);
                continue;
            }

            if (member is IndexerDeclarationSyntax indexerDeclaration)
            {
                string indexerKey = BuildSyntaxIndexerKey(typeMetadataNameFromSyntax, indexerDeclaration);
                if (snapshotIndexerMap != null
                    && plainCurrentIndexerMap != null
                    && snapshotIndexerMap.TryGetValue(
                        indexerKey,
                        out IndexerDeclarationSyntax snapshotIndexer)
                    && plainCurrentIndexerMap.TryGetValue(
                        indexerKey,
                        out IndexerDeclarationSyntax plainIndexer)
                    && SyntaxFactory.AreEquivalent(snapshotIndexer, plainIndexer, topLevel: false))
                {
                    continue;
                }

                AppendExplicitAccessorSkipsForProperty(
                    indexerDeclaration,
                    semanticModel.GetDeclaredSymbol(indexerDeclaration),
                    skipped,
                    typeMetadataNameFromSyntax,
                    snapshotPropertyMap,
                    addedMethodCatalog);
            }
        }
    }

    private static void AppendExplicitAccessorSkipsForProperty(
        BasePropertyDeclarationSyntax propertyDeclaration,
        IPropertySymbol propertySymbol,
        List<WorkerSkipped> skipped,
        string typeMetadataNameFromSyntax,
        Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
        AddedMethodCatalog addedMethodCatalog)
    {
        if (propertySymbol == null)
        {
            return;
        }

        // Indexers: keep reporting every explicit-body accessor (including expression-bodied).
        if (propertyDeclaration is IndexerDeclarationSyntax indexerDeclaration)
        {
            AppendIndexerExplicitAccessorSkips(indexerDeclaration, propertySymbol, skipped);
            return;
        }

        // Properties: getters are patched elsewhere; only setter/init with bodies are Skipped here.
        if (propertyDeclaration.AccessorList == null)
        {
            return;
        }

        bool emittedSkip = false;
        foreach (AccessorDeclarationSyntax accessor in propertyDeclaration.AccessorList.Accessors)
        {
            if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
            {
                continue;
            }

            // Auto-properties emit accessors with neither Body nor ExpressionBody.
            if (accessor.Body == null && accessor.ExpressionBody == null)
            {
                continue;
            }

            IMethodSymbol accessorMethod = ResolveAccessorMethodSymbol(propertySymbol, accessor.Kind());
            if (accessorMethod == null)
            {
                continue;
            }

            skipped.Add(new WorkerSkipped
            {
                Method = FormatMethodLabel(accessorMethod),
                Reason = ExplicitAccessorSkipReason
            });
            emittedSkip = true;
        }

        PropertyDeclarationSyntax namedProperty = propertyDeclaration as PropertyDeclarationSyntax;
        if (!emittedSkip
            || namedProperty == null
            || snapshotPropertyMap == null
            || addedMethodCatalog == null)
        {
            return;
        }

        string propertyKey = BuildSyntaxPropertyKey(typeMetadataNameFromSyntax, namedProperty);
        if (!snapshotPropertyMap.ContainsKey(propertyKey))
        {
            addedMethodCatalog.AddAddedPropertySyntaxKey(propertyKey);
        }
    }

    private static void AppendIndexerExplicitAccessorSkips(
        IndexerDeclarationSyntax indexerDeclaration,
        IPropertySymbol propertySymbol,
        List<WorkerSkipped> skipped)
    {
        if (indexerDeclaration.ExpressionBody != null)
        {
            if (propertySymbol.GetMethod != null)
            {
                skipped.Add(new WorkerSkipped
                {
                    Method = FormatMethodLabel(propertySymbol.GetMethod),
                    Reason = ExplicitAccessorSkipReason
                });
            }

            return;
        }

        if (indexerDeclaration.AccessorList == null)
        {
            return;
        }

        foreach (AccessorDeclarationSyntax accessor in indexerDeclaration.AccessorList.Accessors)
        {
            if (accessor.Body == null && accessor.ExpressionBody == null)
            {
                continue;
            }

            IMethodSymbol accessorMethod = ResolveAccessorMethodSymbol(propertySymbol, accessor.Kind());
            if (accessorMethod == null)
            {
                continue;
            }

            skipped.Add(new WorkerSkipped
            {
                Method = FormatMethodLabel(accessorMethod),
                Reason = ExplicitAccessorSkipReason
            });
        }
    }

    private static IMethodSymbol ResolveAccessorMethodSymbol(
        IPropertySymbol propertySymbol,
        SyntaxKind accessorKind)
    {
        if (accessorKind == SyntaxKind.GetAccessorDeclaration)
        {
            return propertySymbol.GetMethod;
        }

        if (accessorKind == SyntaxKind.SetAccessorDeclaration
            || accessorKind == SyntaxKind.InitAccessorDeclaration)
        {
            return propertySymbol.SetMethod;
        }

        return null;
    }

    // What: reports instance/static constructors, operators, conversion operators, and
    // explicit event accessors as Skipped. Unchanged members matching a verified snapshot
    // are omitted. Field-like events and finalizers are not listed.
    private static void AppendUnsupportedMemberKindSkips(
        TypeDeclarationSyntax typeDeclaration,
        string typeMetadataNameFromSyntax,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped,
        Dictionary<string, ConstructorDeclarationSyntax> snapshotConstructorMap,
        Dictionary<string, MemberDeclarationSyntax> snapshotOperatorMap,
        Dictionary<string, EventDeclarationSyntax> snapshotEventMap,
        Dictionary<string, ConstructorDeclarationSyntax> plainCurrentConstructorMap,
        Dictionary<string, MemberDeclarationSyntax> plainCurrentOperatorMap,
        Dictionary<string, EventDeclarationSyntax> plainCurrentEventMap)
    {
        AppendConstructorSkips(
            typeDeclaration,
            typeMetadataNameFromSyntax,
            semanticModel,
            skipped,
            snapshotConstructorMap,
            plainCurrentConstructorMap);
        AppendOperatorSkips(
            typeDeclaration,
            typeMetadataNameFromSyntax,
            semanticModel,
            skipped,
            snapshotOperatorMap,
            plainCurrentOperatorMap);
        AppendEventAccessorSkips(
            typeDeclaration,
            typeMetadataNameFromSyntax,
            semanticModel,
            skipped,
            snapshotEventMap,
            plainCurrentEventMap);
    }

    private static void AppendConstructorSkips(
        TypeDeclarationSyntax typeDeclaration,
        string typeMetadataNameFromSyntax,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped,
        Dictionary<string, ConstructorDeclarationSyntax> snapshotConstructorMap,
        Dictionary<string, ConstructorDeclarationSyntax> plainCurrentConstructorMap)
    {
        foreach (ConstructorDeclarationSyntax constructorDeclaration in typeDeclaration.Members
            .OfType<ConstructorDeclarationSyntax>())
        {
            string constructorKey = BuildSyntaxConstructorKey(
                typeMetadataNameFromSyntax,
                constructorDeclaration);
            if (snapshotConstructorMap != null
                && plainCurrentConstructorMap != null
                && snapshotConstructorMap.TryGetValue(
                    constructorKey,
                    out ConstructorDeclarationSyntax snapshotConstructor)
                && plainCurrentConstructorMap.TryGetValue(
                    constructorKey,
                    out ConstructorDeclarationSyntax plainConstructor)
                && SyntaxFactory.AreEquivalent(snapshotConstructor, plainConstructor, topLevel: false))
            {
                continue;
            }

            IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(constructorDeclaration);
            AppendUnsupportedKindSkip(skipped, methodSymbol);
        }
    }

    private static void AppendOperatorSkips(
        TypeDeclarationSyntax typeDeclaration,
        string typeMetadataNameFromSyntax,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped,
        Dictionary<string, MemberDeclarationSyntax> snapshotOperatorMap,
        Dictionary<string, MemberDeclarationSyntax> plainCurrentOperatorMap)
    {
        foreach (MemberDeclarationSyntax member in typeDeclaration.Members)
        {
            string operatorKey = TryBuildSyntaxOperatorMemberKey(typeMetadataNameFromSyntax, member);
            if (operatorKey == null)
            {
                continue;
            }

            if (snapshotOperatorMap != null
                && plainCurrentOperatorMap != null
                && snapshotOperatorMap.TryGetValue(operatorKey, out MemberDeclarationSyntax snapshotOperator)
                && plainCurrentOperatorMap.TryGetValue(operatorKey, out MemberDeclarationSyntax plainOperator)
                && SyntaxFactory.AreEquivalent(snapshotOperator, plainOperator, topLevel: false))
            {
                continue;
            }

            IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(member) as IMethodSymbol;
            AppendUnsupportedKindSkip(skipped, methodSymbol);
        }
    }

    private static void AppendEventAccessorSkips(
        TypeDeclarationSyntax typeDeclaration,
        string typeMetadataNameFromSyntax,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped,
        Dictionary<string, EventDeclarationSyntax> snapshotEventMap,
        Dictionary<string, EventDeclarationSyntax> plainCurrentEventMap)
    {
        foreach (EventDeclarationSyntax eventDeclaration in typeDeclaration.Members
            .OfType<EventDeclarationSyntax>())
        {
            string eventKey = BuildSyntaxEventKey(typeMetadataNameFromSyntax, eventDeclaration);
            if (snapshotEventMap != null
                && plainCurrentEventMap != null
                && snapshotEventMap.TryGetValue(eventKey, out EventDeclarationSyntax snapshotEvent)
                && plainCurrentEventMap.TryGetValue(eventKey, out EventDeclarationSyntax plainEvent)
                && SyntaxFactory.AreEquivalent(snapshotEvent, plainEvent, topLevel: false))
            {
                continue;
            }

            IEventSymbol eventSymbol = semanticModel.GetDeclaredSymbol(eventDeclaration);
            if (eventSymbol == null)
            {
                continue;
            }

            AppendEventAccessorSkipIfExplicit(skipped, eventDeclaration, SyntaxKind.AddAccessorDeclaration, eventSymbol.AddMethod);
            AppendEventAccessorSkipIfExplicit(
                skipped,
                eventDeclaration,
                SyntaxKind.RemoveAccessorDeclaration,
                eventSymbol.RemoveMethod);
        }
    }

    private static void AppendEventAccessorSkipIfExplicit(
        List<WorkerSkipped> skipped,
        EventDeclarationSyntax eventDeclaration,
        SyntaxKind accessorKind,
        IMethodSymbol accessorMethod)
    {
        if (accessorMethod == null || eventDeclaration.AccessorList == null)
        {
            return;
        }

        foreach (AccessorDeclarationSyntax accessor in eventDeclaration.AccessorList.Accessors)
        {
            if (accessor.Kind() != accessorKind)
            {
                continue;
            }

            if (accessor.Body == null && accessor.ExpressionBody == null)
            {
                return;
            }

            AppendUnsupportedKindSkip(skipped, accessorMethod);
            return;
        }
    }

    private static void AppendUnsupportedKindSkip(List<WorkerSkipped> skipped, IMethodSymbol methodSymbol)
    {
        if (methodSymbol == null)
        {
            return;
        }

        skipped.Add(new WorkerSkipped
        {
            Method = FormatMethodLabel(methodSymbol),
            Reason = UnsupportedMemberKindSkipReason
        });
    }

    // What: emit a get_<Name> entry / unchanged row / skip for one property with a getter body.
    private static (ShimTypeBuilder CurrentShimType, int ShimTypeCounter, int GlobalShimMethodCounter)
        AppendPropertyGetterEntry(
            PropertyDeclarationSyntax propertyDeclaration,
            TypeDeclarationSyntax typeDeclaration,
            INamedTypeSymbol typeSymbol,
            string typeMetadataNameFromSyntax,
            SemanticModel semanticModel,
            CompilationUnitSyntax root,
            WorkerInput input,
            bool hasBaseline,
            Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
            Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap,
            List<WorkerEntry> entries,
            List<WorkerSkipped> skipped,
            List<WorkerUnchangedMethod> unchangedMethods,
            List<ShimTypeBuilder> shimTypes,
            int shimTypeCounter,
            int globalShimMethodCounter,
            ShimTypeBuilder currentShimType,
            List<UsingDirectiveSyntax> assemblyGlobalUsings,
            AddedMethodCatalog addedMethodCatalog,
            AddedFieldCatalog addedFieldCatalog)
    {
        IPropertySymbol propertySymbol = semanticModel.GetDeclaredSymbol(propertyDeclaration);
        if (propertySymbol == null || propertySymbol.GetMethod == null)
        {
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        (bool hasGetterBody, AccessorDeclarationSyntax getAccessor) =
            TryGetPropertyGetterBody(propertyDeclaration);
        if (!hasGetterBody)
        {
            // Auto-property / setter-only: not a patch candidate (no Skipped row either).
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        IMethodSymbol getterSymbol = propertySymbol.GetMethod;
        string[] parameterTypeFullNames = Array.Empty<string>();
        string methodKey = BuildMethodKey(
            CecilTypeNames.ToMetadataName(typeSymbol),
            getterSymbol.Name,
            parameterTypeFullNames,
            getterSymbol.Arity);
        if (input.ExcludedMethodKeys.Contains(methodKey))
        {
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        if (TryRecordUnchangedPropertyGetter(
            hasBaseline,
            snapshotPropertyMap,
            plainCurrentPropertyMap,
            typeMetadataNameFromSyntax,
            propertyDeclaration,
            typeSymbol,
            getterSymbol,
            parameterTypeFullNames,
            unchangedMethods))
        {
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        // Why skip newly added properties: Harmony looks up get_<Name> on the compiled type
        // and fails with "No method 'get_X' ... was found" when the member does not exist.
        if (TrySkipAddedProperty(
            hasBaseline,
            snapshotPropertyMap,
            plainCurrentPropertyMap,
            typeMetadataNameFromSyntax,
            propertyDeclaration,
            getterSymbol,
            skipped,
            addedMethodCatalog))
        {
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        if (propertyDeclaration.ExplicitInterfaceSpecifier != null)
        {
            skipped.Add(new WorkerSkipped
            {
                Method = FormatMethodLabel(getterSymbol),
                Reason = "Explicit interface implementations are skipped in v1."
            });
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        // Why body stays on the property tree: SemanticModel rejects nodes re-parented onto a
        // synthetic MethodDeclaration ("Syntax node is not within syntax tree").
        SyntaxNode getterBodyNode = (SyntaxNode)propertyDeclaration.ExpressionBody
            ?? (SyntaxNode)getAccessor.Body
            ?? getAccessor.ExpressionBody;
        (bool skipGetter, MethodTransformDecision decision) = TrySkipPropertyGetterByDecision(
            typeDeclaration,
            typeSymbol,
            getterSymbol,
            getterBodyNode,
            semanticModel,
            addedMethodCatalog,
            addedFieldCatalog,
            skipped);
        if (skipGetter)
        {
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        return EmitPropertyGetterShim(
            propertyDeclaration,
            typeDeclaration,
            typeSymbol,
            getterSymbol,
            getterBodyNode,
            decision,
            methodKey,
            parameterTypeFullNames,
            semanticModel,
            root,
            entries,
            shimTypes,
            shimTypeCounter,
            globalShimMethodCounter,
            currentShimType,
            assemblyGlobalUsings,
            addedMethodCatalog,
            addedFieldCatalog);
    }

    private static bool TryRecordUnchangedPropertyGetter(
        bool hasBaseline,
        Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
        Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap,
        string typeMetadataNameFromSyntax,
        PropertyDeclarationSyntax propertyDeclaration,
        INamedTypeSymbol typeSymbol,
        IMethodSymbol getterSymbol,
        string[] parameterTypeFullNames,
        List<WorkerUnchangedMethod> unchangedMethods)
    {
        if (!hasBaseline
            || snapshotPropertyMap == null
            || plainCurrentPropertyMap == null)
        {
            return false;
        }

        string propertyKey = BuildSyntaxPropertyKey(typeMetadataNameFromSyntax, propertyDeclaration);
        if (snapshotPropertyMap.TryGetValue(propertyKey, out PropertyDeclarationSyntax snapshotProperty)
            && plainCurrentPropertyMap.TryGetValue(propertyKey, out PropertyDeclarationSyntax plainProperty)
            && ArePropertyGettersEquivalent(snapshotProperty, plainProperty))
        {
            unchangedMethods.Add(new WorkerUnchangedMethod
            {
                TypeMetadataName = CecilTypeNames.ToMetadataName(typeSymbol),
                MethodName = getterSymbol.Name,
                ParameterTypeFullNames = parameterTypeFullNames,
                GenericArity = getterSymbol.Arity
            });
            return true;
        }

        return false;
    }

    private static bool TrySkipAddedProperty(
        bool hasBaseline,
        Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
        Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap,
        string typeMetadataNameFromSyntax,
        PropertyDeclarationSyntax propertyDeclaration,
        IMethodSymbol getterSymbol,
        List<WorkerSkipped> skipped,
        AddedMethodCatalog addedMethodCatalog)
    {
        if (!hasBaseline
            || snapshotPropertyMap == null
            || plainCurrentPropertyMap == null)
        {
            return false;
        }

        string addedPropertyKey = BuildSyntaxPropertyKey(typeMetadataNameFromSyntax, propertyDeclaration);
        if (snapshotPropertyMap.ContainsKey(addedPropertyKey))
        {
            return false;
        }

        skipped.Add(new WorkerSkipped
        {
            Method = FormatMethodLabel(getterSymbol),
            Reason = AddedMethodSkipReasons.AddedProperty
        });
        addedMethodCatalog.AddAddedPropertySyntaxKey(addedPropertyKey);
        return true;
    }

    private static (bool SkipGetter, MethodTransformDecision Decision) TrySkipPropertyGetterByDecision(
        TypeDeclarationSyntax typeDeclaration,
        INamedTypeSymbol typeSymbol,
        IMethodSymbol getterSymbol,
        SyntaxNode getterBodyNode,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerSkipped> skipped)
    {
        MethodTransformDecision decision = DecideMethodTransform(
            typeDeclaration,
            typeSymbol,
            methodDeclaration: null,
            getterSymbol,
            getterBodyNode,
            semanticModel);
        if (decision.SkipReason != null)
        {
            skipped.Add(new WorkerSkipped
            {
                Method = FormatMethodLabel(getterSymbol),
                Reason = decision.SkipReason
            });
            return (true, decision);
        }

        (string addedCallSiteSkip, string calledAddedMethodKey) = EvaluateAddedCallSiteSkipReason(
            getterBodyNode,
            semanticModel,
            addedMethodCatalog,
            addedFieldCatalog);
        if (addedCallSiteSkip == null)
        {
            return (false, decision);
        }

        skipped.Add(new WorkerSkipped
        {
            Method = FormatMethodLabel(getterSymbol),
            Reason = addedCallSiteSkip,
            CalledAddedMethodKey = calledAddedMethodKey,
            MethodKey = calledAddedMethodKey == null
                ? null
                : BuildMethodKeyFromSymbol(getterSymbol)
        });
        return (true, decision);
    }

    private static (ShimTypeBuilder CurrentShimType, int ShimTypeCounter, int GlobalShimMethodCounter)
        EmitPropertyGetterShim(
            PropertyDeclarationSyntax propertyDeclaration,
            TypeDeclarationSyntax typeDeclaration,
            INamedTypeSymbol typeSymbol,
            IMethodSymbol getterSymbol,
            SyntaxNode getterBodyNode,
            MethodTransformDecision decision,
            string methodKey,
            string[] parameterTypeFullNames,
            SemanticModel semanticModel,
            CompilationUnitSyntax root,
            List<WorkerEntry> entries,
            List<ShimTypeBuilder> shimTypes,
            int shimTypeCounter,
            int globalShimMethodCounter,
            ShimTypeBuilder currentShimType,
            List<UsingDirectiveSyntax> assemblyGlobalUsings,
            AddedMethodCatalog addedMethodCatalog,
            AddedFieldCatalog addedFieldCatalog)
    {
        if (currentShimType == null)
        {
            string shimTypeName = typeSymbol.Name + "_UloopHotReloadShims_" + shimTypeCounter;
            shimTypeCounter++;
            string namespaceName = typeSymbol.ContainingNamespace == null
                || typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : typeSymbol.ContainingNamespace.ToDisplayString();
            currentShimType = new ShimTypeBuilder(
                shimTypeName,
                namespaceName,
                CollectUsingsForType(root, typeDeclaration, assemblyGlobalUsings));
            shimTypes.Add(currentShimType);
        }

        string shimMethodName = getterSymbol.Name + "__shim" + globalShimMethodCounter;
        globalShimMethodCounter++;

        FileLinePositionSpan originalSpan = propertyDeclaration.GetLocation().GetLineSpan();
        int sourceStartLine = originalSpan.StartLinePosition.Line + 1;
        int sourceEndLine = originalSpan.EndLinePosition.Line + 1;

        AccessorPlan rewritePlan = decision.UsesDelegation
            ? currentShimType.AccessorPlan
            : null;
        MethodDeclarationSyntax rewrittenMethod = RewritePropertyGetterBody(
            propertyDeclaration,
            getterBodyNode,
            getterSymbol,
            typeSymbol,
            semanticModel,
            rewritePlan,
            addedMethodCatalog,
            addedFieldCatalog);
        currentShimType.AddMethod(rewrittenMethod, shimMethodName);

        entries.Add(new WorkerEntry
        {
            TypeMetadataName = CecilTypeNames.ToMetadataName(typeSymbol),
            MethodName = getterSymbol.Name,
            ParameterTypeFullNames = parameterTypeFullNames,
            GenericArity = getterSymbol.Arity,
            ShimTypeName = currentShimType.ShimTypeName,
            ShimMethodName = shimMethodName,
            PatchKind = decision.PatchKind,
            CalledAddedMethodKeys = CollectCalledAddedMethodKeys(
                getterBodyNode,
                semanticModel,
                addedMethodCatalog,
                methodKey),
            SourceStartLine = sourceStartLine,
            SourceEndLine = sourceEndLine,
            LifecycleNote = null
        });

        return (currentShimType, shimTypeCounter, globalShimMethodCounter);
    }

    private static (bool HasGetterBody, AccessorDeclarationSyntax GetAccessor) TryGetPropertyGetterBody(
        PropertyDeclarationSyntax propertyDeclaration)
    {
        if (propertyDeclaration.ExpressionBody != null)
        {
            return (true, null);
        }

        if (propertyDeclaration.AccessorList == null)
        {
            return (false, null);
        }

        foreach (AccessorDeclarationSyntax accessor in propertyDeclaration.AccessorList.Accessors)
        {
            if (!accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
            {
                continue;
            }

            if (accessor.Body == null && accessor.ExpressionBody == null)
            {
                return (false, null);
            }

            return (true, accessor);
        }

        return (false, null);
    }

    // Why getter-only: whole-property AreEquivalent treats setter edits as getter changes and
    // would emit a useless Patched get_ row beside Skipped set_.
    private static bool ArePropertyGettersEquivalent(
        PropertyDeclarationSyntax snapshotProperty,
        PropertyDeclarationSyntax currentProperty)
    {
        return SyntaxFactory.AreEquivalent(
            NormalizePropertyToGetterShape(snapshotProperty),
            NormalizePropertyToGetterShape(currentProperty),
            topLevel: false);
    }

    private static PropertyDeclarationSyntax NormalizePropertyToGetterShape(
        PropertyDeclarationSyntax propertyDeclaration)
    {
        if (propertyDeclaration.ExpressionBody != null)
        {
            return propertyDeclaration.WithAccessorList(null);
        }

        if (propertyDeclaration.AccessorList == null)
        {
            return propertyDeclaration;
        }

        List<AccessorDeclarationSyntax> getAccessors = new List<AccessorDeclarationSyntax>();
        foreach (AccessorDeclarationSyntax accessor in propertyDeclaration.AccessorList.Accessors)
        {
            if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
            {
                getAccessors.Add(accessor);
            }
        }

        return propertyDeclaration.WithAccessorList(
            SyntaxFactory.AccessorList(SyntaxFactory.List(getAccessors)));
    }

    // What: rewrite a getter body while it is still in the bound tree, then wrap as a shim method.
    private static MethodDeclarationSyntax RewritePropertyGetterBody(
        PropertyDeclarationSyntax propertyDeclaration,
        SyntaxNode getterBodyNode,
        IMethodSymbol getterSymbol,
        INamedTypeSymbol targetType,
        SemanticModel semanticModel,
        AccessorPlan accessorPlan,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        ShimBodyRewriter rewriter = new ShimBodyRewriter(
            semanticModel,
            targetType,
            accessorPlan,
            addedMethodCatalog,
            addedFieldCatalog);
        SyntaxNode rewrittenBody = rewriter.Visit(getterBodyNode);
        // Why transfer: Visit may rebuild ArrowExpressionClause nodes and drop #line annotations.
        rewrittenBody = TransferUloopLineAnnotations(getterBodyNode, rewrittenBody);

        TypeSyntax returnType = propertyDeclaration.Type.WithoutTrivia();
        // ToShimMethod forces public static and injects __instance for instance getters.
        MethodDeclarationSyntax method = SyntaxFactory.MethodDeclaration(
                returnType,
                SyntaxFactory.Identifier(getterSymbol.Name))
            .WithModifiers(
                SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList());

        if (rewrittenBody is ArrowExpressionClauseSyntax arrowBody)
        {
            method = method
                .WithExpressionBody(arrowBody)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }
        else if (rewrittenBody is BlockSyntax blockBody)
        {
            method = method.WithBody(blockBody);
        }
        else
        {
            // get => expr rewritten to a bare expression: wrap as arrow.
            ArrowExpressionClauseSyntax wrappedArrow = SyntaxFactory.ArrowExpressionClause(
                (ExpressionSyntax)rewrittenBody);
            wrappedArrow = (ArrowExpressionClauseSyntax)TransferUloopLineAnnotations(
                getterBodyNode,
                wrappedArrow);
            method = method
                .WithExpressionBody(wrappedArrow)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        return ShimMethodFactory.ToShimMethod(method, getterSymbol);
    }

    private static SyntaxNode TransferUloopLineAnnotations(SyntaxNode source, SyntaxNode target)
    {
        if (source == null || target == null)
        {
            return target;
        }

        SyntaxNode result = target;
        foreach (SyntaxAnnotation annotation in source.GetAnnotations(UloopLineAnnotationKind))
        {
            result = result.WithAdditionalAnnotations(annotation);
        }

        return result;
    }

    private static IEnumerable<TypeDeclarationSyntax> EnumerateTypeDeclarations(CompilationUnitSyntax root)
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

    // methodDeclaration may be null for property getters (bodyNode must still be in the bound tree).
    private static MethodTransformDecision DecideMethodTransform(
        TypeDeclarationSyntax typeDeclaration,
        INamedTypeSymbol typeSymbol,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        SyntaxNode bodyNode,
        SemanticModel semanticModel)
    {
        string hardSkip = EvaluateHardSkipReason(
            typeDeclaration,
            typeSymbol,
            methodDeclaration,
            methodSymbol);
        if (hardSkip != null)
        {
            return MethodTransformDecision.Skip(hardSkip);
        }

        if (bodyNode == null)
        {
            return MethodTransformDecision.Skip("Methods without a body (abstract/extern) are skipped.");
        }

        if (ContainsBaseExpression(bodyNode))
        {
            return MethodTransformDecision.Skip(
                "Methods that call base. members are skipped; C# cannot express base calls outside the type.");
        }

        string eventUseReason = EvaluateEventUseSkipReason(bodyNode, semanticModel);
        if (eventUseReason != null)
        {
            return MethodTransformDecision.Skip(eventUseReason);
        }

        bool closureInaccessible = SubtreeHasInaccessibleMemberAccess(
            semanticModel,
            FindClosureBodies(bodyNode));
        bool asyncIteratorInaccessible = IsAsyncOrIterator(methodDeclaration, bodyNode)
            && SubtreeHasInaccessibleMemberAccess(semanticModel, new[] { bodyNode });

        if (!closureInaccessible && !asyncIteratorInaccessible)
        {
            return MethodTransformDecision.Transplant();
        }

        // Condition (a): only the v1 private-access skip reasons are eligible for accessor rewrite.
        string v1Reason = closureInaccessible
            ? "Lambda, local-function, or query-expression bodies that access private/internal members "
                + "are skipped in v1 (closure methods JIT-compile normally and fail accessibility checks)."
            : "Async or iterator methods whose bodies access private/internal members are skipped in v1 "
                + "(state-machine MoveNext JIT-compiles normally and fails accessibility checks).";

        if (!AccessorEligibility.TryBuildPlan(
                semanticModel,
                methodSymbol,
                typeSymbol,
                bodyNode,
                out AccessorPlan feasibilityPlan,
                out string accessorRejectReason))
        {
            return MethodTransformDecision.Skip(
                v1Reason + " Accessor rewrite unavailable: " + accessorRejectReason);
        }

        // Safety net: detection said "needs accessors" but eligibility found nothing to rewrite
        // (e.g. local-function-only async body). Transplant is correct — the body is unchanged.
        if (feasibilityPlan.Entries.Count == 0)
        {
            return MethodTransformDecision.Transplant();
        }

        return MethodTransformDecision.Delegation();
    }

    private static string EvaluateHardSkipReason(
        TypeDeclarationSyntax typeDeclaration,
        INamedTypeSymbol typeSymbol,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol)
    {
        // A nested type inside a partial outer type still has an incomplete single-file model.
        for (TypeDeclarationSyntax declaration = typeDeclaration;
             declaration != null;
             declaration = declaration.Parent as TypeDeclarationSyntax)
        {
            if (declaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
            {
                return "Partial types are skipped because a single file cannot provide a complete semantic model.";
            }
        }

        if (typeSymbol.TypeKind == TypeKind.Struct || typeSymbol.IsValueType)
        {
            return "Struct (value type) methods are out of scope for v1; byref instance transplant is unverified.";
        }

        bool hasTypeParameters = methodDeclaration != null && methodDeclaration.TypeParameterList != null;
        if (typeSymbol.IsGenericType || methodSymbol.IsGenericMethod || hasTypeParameters)
        {
            return "Generic methods and methods inside generic types cannot be safely patched with Harmony. Run 'uloop compile'.";
        }

        // Explicit interface implementations have dotted metadata names (e.g. IFoo.Bar) that are
        // not valid C# identifiers for shim method names; sanitizing would also desync the
        // matcher (Cecil MethodDefinition.Name). v1 skips them with an explicit reason.
        if (methodDeclaration != null && methodDeclaration.ExplicitInterfaceSpecifier != null)
        {
            return "Explicit interface implementations are skipped in v1.";
        }

        return null;
    }

    // Why skip event uses beyond +=/-=: outside the declaring type C# only allows those
    // assignments, so a shim cannot compile Raise/Invoke/read. nameof(ScoreChanged) and
    // similar non-runtime references are also skipped — Skip is an honest report and safer
    // than a compile failure.
    private static string EvaluateEventUseSkipReason(SyntaxNode bodyNode, SemanticModel semanticModel)
    {
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (node is not IdentifierNameSyntax && node is not MemberAccessExpressionSyntax)
            {
                continue;
            }

            IEventSymbol eventSymbol = semanticModel.GetSymbolInfo(node).Symbol as IEventSymbol;
            if (eventSymbol == null)
            {
                continue;
            }

            // this.E / instance.E resolve the same event on the IdentifierName and the outer
            // MemberAccess; judge usage on the outer expression only.
            SyntaxNode effective = node;
            if (node.Parent is MemberAccessExpressionSyntax parentAccess && parentAccess.Name == node)
            {
                effective = parentAccess;
            }

            // += / -= on the left-hand side are the only event operations C# allows outside the
            // declaring type.
            if (effective.Parent is AssignmentExpressionSyntax assignment
                && (assignment.IsKind(SyntaxKind.AddAssignmentExpression)
                    || assignment.IsKind(SyntaxKind.SubtractAssignmentExpression))
                && assignment.Left == effective)
            {
                continue;
            }

            return "Methods that raise, invoke, or read a field-like event are skipped; "
                + "C# only allows += / -= on an event outside its declaring type, so the "
                + "shim cannot compile this body. Use uloop compile.";
        }

        return null;
    }

    private static bool ContainsBaseExpression(SyntaxNode bodyNode)
    {
        return bodyNode.DescendantNodes().OfType<BaseExpressionSyntax>().Any();
    }

    private static bool IsAsyncOrIterator(MethodDeclarationSyntax methodDeclaration, SyntaxNode bodyNode)
    {
        if (methodDeclaration != null
            && methodDeclaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.AsyncKeyword)))
        {
            return true;
        }

        // Yields inside local functions do not make the outer method an iterator.
        foreach (YieldStatementSyntax yieldStatement in bodyNode.DescendantNodes().OfType<YieldStatementSyntax>())
        {
            if (!IsInsideLocalFunction(yieldStatement, bodyNode))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInsideLocalFunction(SyntaxNode node, SyntaxNode stopAt)
    {
        for (SyntaxNode current = node.Parent; current != null && current != stopAt; current = current.Parent)
        {
            if (current is LocalFunctionStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static List<SyntaxNode> FindClosureBodies(SyntaxNode bodyNode)
    {
        List<SyntaxNode> bodies = new List<SyntaxNode>();
        foreach (SyntaxNode node in bodyNode.DescendantNodes())
        {
            if (node is SimpleLambdaExpressionSyntax simpleLambda)
            {
                bodies.Add(simpleLambda.Body);
            }
            else if (node is ParenthesizedLambdaExpressionSyntax parenthesizedLambda)
            {
                bodies.Add(parenthesizedLambda.Body);
            }
            else if (node is AnonymousMethodExpressionSyntax anonymousMethod && anonymousMethod.Body != null)
            {
                bodies.Add(anonymousMethod.Body);
            }
            else if (node is LocalFunctionStatementSyntax localFunction)
            {
                SyntaxNode localBody = (SyntaxNode)localFunction.Body ?? localFunction.ExpressionBody;
                if (localBody != null)
                {
                    bodies.Add(localBody);
                }
            }
            else if (node is QueryExpressionSyntax queryExpression)
            {
                // Query clauses compile to display-class methods that JIT normally; treat the
                // whole query (including the source expression) as a closure body for v1.
                bodies.Add(queryExpression);
            }
        }

        return bodies;
    }

    private static bool SubtreeHasInaccessibleMemberAccess(
        SemanticModel semanticModel,
        IEnumerable<SyntaxNode> roots)
    {
        foreach (SyntaxNode root in roots)
        {
            if (root == null)
            {
                continue;
            }

            foreach (SyntaxNode node in root.DescendantNodesAndSelf())
            {
                if (NameofRules.IsInsideNameofArgument(node))
                {
                    continue;
                }

                if (HasInaccessibleAccessAtNode(semanticModel, node))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// What: site-aware inaccessible access detection (property get vs set, ctor, etc.).
    /// </summary>
    private static bool HasInaccessibleAccessAtNode(SemanticModel semanticModel, SyntaxNode node)
    {
        if (node is AssignmentExpressionSyntax assignment)
        {
            return IsInaccessibleAssignment(semanticModel, assignment);
        }

        if (node is PostfixUnaryExpressionSyntax postfix)
        {
            return IsInaccessiblePostfixIncrement(semanticModel, postfix);
        }

        if (node is PrefixUnaryExpressionSyntax prefix)
        {
            return IsInaccessiblePrefixIncrement(semanticModel, prefix);
        }

        if (node is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax)
        {
            return IsInaccessibleObjectCreation(semanticModel, node);
        }

        if (node is InvocationExpressionSyntax invocation)
        {
            return IsInaccessibleInvocation(semanticModel, invocation);
        }

        if (node is ElementAccessExpressionSyntax elementAccess)
        {
            return IsInaccessibleElementAccess(semanticModel, elementAccess);
        }

        if (node is MemberBindingExpressionSyntax memberBinding)
        {
            return IsInaccessibleMemberBinding(semanticModel, memberBinding);
        }

        if (node is IdentifierNameSyntax or GenericNameSyntax)
        {
            return IsInaccessibleSimpleName(semanticModel, (SimpleNameSyntax)node);
        }

        if (node is MemberAccessExpressionSyntax memberAccess)
        {
            return IsInaccessibleMemberAccess(semanticModel, memberAccess);
        }

        return false;
    }

    private static bool IsInaccessibleAssignment(
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment)
    {
        if (assignment.Parent is InitializerExpressionSyntax)
        {
            // Initializer assignments are always writes (including ImplicitElementAccess indexers).
            ISymbol initializerSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
            if (initializerSymbol is IPropertySymbol initializerProperty)
            {
                return AccessibilityRules.IsInaccessibleAccessor(initializerProperty.SetMethod);
            }

            return IsInaccessibleNonConstSymbol(initializerSymbol);
        }

        ISymbol leftSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
        if (leftSymbol is IPropertySymbol propertySymbol)
        {
            bool needsGetter = !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);
            if (needsGetter && AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod))
            {
                return true;
            }

            return AccessibilityRules.IsInaccessibleAccessor(propertySymbol.SetMethod);
        }

        return IsInaccessibleNonConstSymbol(leftSymbol);
    }

    private static bool IsInaccessiblePostfixIncrement(
        SemanticModel semanticModel,
        PostfixUnaryExpressionSyntax postfix)
    {
        if (!(postfix.IsKind(SyntaxKind.PostIncrementExpression)
            || postfix.IsKind(SyntaxKind.PostDecrementExpression)))
        {
            return false;
        }

        return IsInaccessibleIncrementOperand(semanticModel, postfix.Operand);
    }

    private static bool IsInaccessiblePrefixIncrement(
        SemanticModel semanticModel,
        PrefixUnaryExpressionSyntax prefix)
    {
        if (!(prefix.IsKind(SyntaxKind.PreIncrementExpression)
            || prefix.IsKind(SyntaxKind.PreDecrementExpression)))
        {
            return false;
        }

        return IsInaccessibleIncrementOperand(semanticModel, prefix.Operand);
    }

    private static bool IsInaccessibleObjectCreation(SemanticModel semanticModel, SyntaxNode node)
    {
        ISymbol ctorSymbol = semanticModel.GetSymbolInfo(node).Symbol;
        return ctorSymbol != null
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(ctorSymbol);
    }

    private static bool IsInaccessibleInvocation(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation)
    {
        if (NameofRules.IsNameofInvocation(invocation))
        {
            return false;
        }

        ISymbol symbol = semanticModel.GetSymbolInfo(invocation).Symbol;
        return symbol is IMethodSymbol methodSymbol
            && methodSymbol.MethodKind == MethodKind.Ordinary
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(methodSymbol);
    }

    private static bool IsInaccessibleElementAccess(
        SemanticModel semanticModel,
        ElementAccessExpressionSyntax elementAccess)
    {
        // Assignment-left ElementAccess is owned by the assignment branch (write context).
        if (elementAccess.Parent is AssignmentExpressionSyntax parentAssignment
            && parentAssignment.Left == elementAccess)
        {
            return false;
        }

        ISymbol symbol = semanticModel.GetSymbolInfo(elementAccess).Symbol;
        if (symbol is IPropertySymbol indexer)
        {
            // Standalone ElementAccess is a read.
            return AccessibilityRules.IsInaccessibleAccessor(indexer.GetMethod);
        }

        return IsInaccessibleNonConstSymbol(symbol);
    }

    private static bool IsInaccessibleMemberBinding(
        SemanticModel semanticModel,
        MemberBindingExpressionSyntax memberBinding)
    {
        // ?.Member — visibility of the bound member (not the receiver).
        ISymbol bound = semanticModel.GetSymbolInfo(memberBinding.Name).Symbol;
        if (bound is IPropertySymbol propertySymbol)
        {
            return AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod);
        }

        return bound != null
            && bound is not INamespaceSymbol
            && bound is not ITypeSymbol
            && IsInaccessibleNonConstSymbol(bound);
    }

    private static bool IsInaccessibleSimpleName(SemanticModel semanticModel, SimpleNameSyntax name)
    {
        if (AccessorEligibility.IsNameHandledByParent(name))
        {
            return false;
        }

        if (name.Parent is AssignmentExpressionSyntax parentAssignment
            && parentAssignment.Left == name)
        {
            return false;
        }

        // Invocation-target exclusion applies only to method groups; delegate-typed fields
        // invoked as `_cb()` must be detected as field reads.
        if (name.Parent is InvocationExpressionSyntax parentInvocation
            && parentInvocation.Expression == name)
        {
            ISymbol invocationTarget = semanticModel.GetSymbolInfo(name).Symbol;
            if (invocationTarget is IMethodSymbol)
            {
                return false;
            }
        }

        ISymbol symbol = semanticModel.GetSymbolInfo(name).Symbol;
        if (symbol is IPropertySymbol propertySymbol)
        {
            return AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod);
        }

        return IsInaccessibleNonConstSymbol(symbol);
    }

    private static bool IsInaccessibleMemberAccess(
        SemanticModel semanticModel,
        MemberAccessExpressionSyntax memberAccess)
    {
        if (memberAccess.Parent is InvocationExpressionSyntax parentInvocation
            && parentInvocation.Expression == memberAccess)
        {
            ISymbol invocationTarget = semanticModel.GetSymbolInfo(memberAccess).Symbol
                ?? semanticModel.GetSymbolInfo(memberAccess.Name).Symbol;
            if (invocationTarget is IMethodSymbol)
            {
                return false;
            }
        }

        if (memberAccess.Parent is AssignmentExpressionSyntax parentAssignment
            && parentAssignment.Left == memberAccess)
        {
            return false;
        }

        ISymbol symbol = semanticModel.GetSymbolInfo(memberAccess).Symbol
            ?? semanticModel.GetSymbolInfo(memberAccess.Name).Symbol;
        if (symbol is IPropertySymbol propertySymbol)
        {
            return AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod);
        }

        return IsInaccessibleNonConstSymbol(symbol);
    }

    // Why exclude const: a const field is IsStatic, but it has no runtime storage.
    // Publicized references fold the literal at compile time, so treating const as
    // inaccessible would force a StaticFieldRefAccess bind that cannot succeed.
    private static bool IsInaccessibleNonConstSymbol(ISymbol symbol)
    {
        if (symbol is IFieldSymbol fieldSymbol && fieldSymbol.IsConst)
        {
            return false;
        }

        return symbol != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol);
    }

    private static bool IsInaccessibleIncrementOperand(
        SemanticModel semanticModel,
        ExpressionSyntax operand)
    {
        ISymbol symbol = semanticModel.GetSymbolInfo(operand).Symbol;
        if (symbol is IPropertySymbol propertySymbol)
        {
            return AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod)
                || AccessibilityRules.IsInaccessibleAccessor(propertySymbol.SetMethod);
        }

        return IsInaccessibleNonConstSymbol(symbol);
    }

    private static (int ShimTypeCounter, int GlobalShimMethodCounter) QueueTypeMethods(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol,
        WorkerInput input,
        bool hasBaseline,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        List<string> declarationDriftWarnings,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures,
        int shimTypeCounter,
        int globalShimMethodCounter)
    {
        INamedTypeSymbol compiledType = FindCompiledType(typeState.TypeSymbol, targetTypesAssemblySymbol);
        if (compiledType == null)
        {
            SkipAllMethodsOnUncompiledType(typeState, semanticModel, skipped, addedMethodCatalog);
            return (shimTypeCounter, globalShimMethodCounter);
        }

        ClassifyAddedFields(
            typeState,
            semanticModel,
            compiledType,
            targetTypesAssemblySymbol,
            addedFieldCatalog,
            declarationDriftWarnings);

        foreach (MethodDeclarationSyntax methodDeclaration in typeState.TypeDeclaration.Members
            .OfType<MethodDeclarationSyntax>())
        {
            (shimTypeCounter, globalShimMethodCounter) = QueueOrdinaryMethod(
                methodDeclaration,
                typeState,
                semanticModel,
                compiledType,
                input,
                hasBaseline,
                snapshotMethodMap,
                plainCurrentMethodMap,
                root,
                assemblyGlobalUsings,
                shimTypes,
                addedMethodCatalog,
                skipped,
                unchangedMethods,
                declarationDriftWarnings,
                removedMembers,
                removedMethodSignatures,
                shimTypeCounter,
                globalShimMethodCounter);
        }

        return (shimTypeCounter, globalShimMethodCounter);
    }

    private static (int ShimTypeCounter, int GlobalShimMethodCounter) QueueOrdinaryMethod(
        MethodDeclarationSyntax methodDeclaration,
        TypeEmitState typeState,
        SemanticModel semanticModel,
        INamedTypeSymbol compiledType,
        WorkerInput input,
        bool hasBaseline,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedMethodCatalog addedMethodCatalog,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        List<string> declarationDriftWarnings,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures,
        int shimTypeCounter,
        int globalShimMethodCounter)
    {
        IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null)
        {
            return (shimTypeCounter, globalShimMethodCounter);
        }

        string[] parameterTypeFullNames = methodSymbol.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        string methodKey = BuildMethodKey(
            CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
            methodSymbol.Name,
            parameterTypeFullNames,
            methodSymbol.Arity);
        (bool isAddedMethod, bool replacesCompiledMethod) = ClassifyOrdinaryMethodAddedState(
            methodDeclaration,
            compiledType,
            methodSymbol);
        if (TrySkipExcludedOrdinaryMethod(
            isAddedMethod,
            replacesCompiledMethod,
            methodKey,
            methodDeclaration,
            typeState,
            input,
            snapshotMethodMap,
            plainCurrentMethodMap,
            addedMethodCatalog))
        {
            return (shimTypeCounter, globalShimMethodCounter);
        }

        string syntaxMethodKey = BuildSyntaxMethodKey(
            typeState.TypeMetadataNameFromSyntax,
            methodDeclaration);
        if (TrySkipInterfaceOrdinaryMethod(
            isAddedMethod,
            replacesCompiledMethod,
            hasBaseline,
            syntaxMethodKey,
            methodSymbol,
            typeState,
            snapshotMethodMap,
            plainCurrentMethodMap,
            addedMethodCatalog,
            skipped))
        {
            return (shimTypeCounter, globalShimMethodCounter);
        }

        if (TryRecordUnchangedOrdinaryMethod(
            isAddedMethod,
            hasBaseline,
            syntaxMethodKey,
            methodSymbol,
            typeState,
            parameterTypeFullNames,
            snapshotMethodMap,
            plainCurrentMethodMap,
            unchangedMethods))
        {
            return (shimTypeCounter, globalShimMethodCounter);
        }

        MethodTransformDecision decision = DecideOrdinaryMethodTransform(
            isAddedMethod,
            methodDeclaration,
            methodSymbol,
            typeState,
            semanticModel);
        if (decision.SkipReason != null)
        {
            skipped.Add(new WorkerSkipped
            {
                Method = FormatMethodLabel(methodSymbol),
                Reason = decision.SkipReason
            });
            if (isAddedMethod)
            {
                // Why strip skipped added declarations: otherwise drift warns about
                // fields/initializers for a method the skip reason already explained.
                RecordHandledAddedMethodSyntaxKey(
                    addedMethodCatalog,
                    syntaxMethodKey,
                    replacesCompiledMethod,
                    snapshotMethodMap,
                    plainCurrentMethodMap);
            }

            return (shimTypeCounter, globalShimMethodCounter);
        }

        return QueueDecidedOrdinaryMethod(
            methodDeclaration,
            methodSymbol,
            decision,
            isAddedMethod,
            replacesCompiledMethod,
            methodKey,
            syntaxMethodKey,
            parameterTypeFullNames,
            typeState,
            root,
            assemblyGlobalUsings,
            shimTypes,
            addedMethodCatalog,
            snapshotMethodMap,
            plainCurrentMethodMap,
            declarationDriftWarnings,
            removedMembers,
            removedMethodSignatures,
            shimTypeCounter,
            globalShimMethodCounter);
    }

    private static (bool IsAddedMethod, bool ReplacesCompiledMethod) ClassifyOrdinaryMethodAddedState(
        MethodDeclarationSyntax methodDeclaration,
        INamedTypeSymbol compiledType,
        IMethodSymbol methodSymbol)
    {
        // Why skip explicit-interface methods: compiled GetMembers(simpleName) does not
        // see them (metadata name is Interface.Method), so they would be misclassified as
        // Added and skip the unchanged/baseline path.
        if (methodDeclaration.ExplicitInterfaceSpecifier != null)
        {
            return (false, false);
        }

        CompiledMethodMatch compiledMatch = MatchCompiledOrdinaryMethod(compiledType, methodSymbol);
        return (
            compiledMatch != CompiledMethodMatch.Matched,
            compiledMatch == CompiledMethodMatch.ReturnTypeChanged);
    }

    private static bool TrySkipExcludedOrdinaryMethod(
        bool isAddedMethod,
        bool replacesCompiledMethod,
        string methodKey,
        MethodDeclarationSyntax methodDeclaration,
        TypeEmitState typeState,
        WorkerInput input,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        AddedMethodCatalog addedMethodCatalog)
    {
        if (isAddedMethod)
        {
            addedMethodCatalog.MarkClassifiedAdded(methodKey);
            if (input.ExcludedAddedMethodKeys.Contains(methodKey))
            {
                RecordHandledAddedMethodSyntaxKey(
                    addedMethodCatalog,
                    BuildSyntaxMethodKey(typeState.TypeMetadataNameFromSyntax, methodDeclaration),
                    replacesCompiledMethod,
                    snapshotMethodMap,
                    plainCurrentMethodMap);
                return true;
            }

            return false;
        }

        return input.ExcludedMethodKeys.Contains(methodKey);
    }

    private static bool TrySkipInterfaceOrdinaryMethod(
        bool isAddedMethod,
        bool replacesCompiledMethod,
        bool hasBaseline,
        string syntaxMethodKey,
        IMethodSymbol methodSymbol,
        TypeEmitState typeState,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        AddedMethodCatalog addedMethodCatalog,
        List<WorkerSkipped> skipped)
    {
        if (typeState.TypeSymbol.TypeKind != TypeKind.Interface)
        {
            return false;
        }

        if (!isAddedMethod && hasBaseline
            && snapshotMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax snapshotDecl)
            && plainCurrentMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax plainDecl)
            && SyntaxFactory.AreEquivalent(snapshotDecl, plainDecl, topLevel: false))
        {
            // Why not unchangedMethods: RevertUnchangedPatches Resolve/ReadAssembly is
            // wasted for members Harmony will never patch. Stay inert.
            return true;
        }

        skipped.Add(new WorkerSkipped
        {
            Method = FormatMethodLabel(methodSymbol),
            Reason = AddedMethodSkipReasons.InterfaceMember
        });
        if (isAddedMethod)
        {
            RecordHandledAddedMethodSyntaxKey(
                addedMethodCatalog,
                syntaxMethodKey,
                replacesCompiledMethod,
                snapshotMethodMap,
                plainCurrentMethodMap);
        }

        return true;
    }

    private static bool TryRecordUnchangedOrdinaryMethod(
        bool isAddedMethod,
        bool hasBaseline,
        string syntaxMethodKey,
        IMethodSymbol methodSymbol,
        TypeEmitState typeState,
        string[] parameterTypeFullNames,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        List<WorkerUnchangedMethod> unchangedMethods)
    {
        if (isAddedMethod || !hasBaseline)
        {
            return false;
        }

        // Why plainDecl: compare unannotated nodes; annotated methodDeclaration breaks
        // AreEquivalent for long-return / unchecked / switch shapes (see plainRoot).
        if (snapshotMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax snapshotDecl)
            && plainCurrentMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax plainDecl)
            && SyntaxFactory.AreEquivalent(snapshotDecl, plainDecl, topLevel: false))
        {
            unchangedMethods.Add(new WorkerUnchangedMethod
            {
                TypeMetadataName = CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
                MethodName = methodSymbol.Name,
                ParameterTypeFullNames = parameterTypeFullNames,
                GenericArity = methodSymbol.Arity
            });
            return true;
        }

        return false;
    }

    private static MethodTransformDecision DecideOrdinaryMethodTransform(
        bool isAddedMethod,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        TypeEmitState typeState,
        SemanticModel semanticModel)
    {
        SyntaxNode methodBodyNode =
            (SyntaxNode)methodDeclaration.Body ?? methodDeclaration.ExpressionBody;
        string addedSkip = isAddedMethod
            ? EvaluateAddedMethodSkipReason(methodSymbol, methodDeclaration)
            : null;
        MethodTransformDecision decision = addedSkip != null
            ? MethodTransformDecision.Skip(addedSkip)
            : DecideMethodTransform(
                typeState.TypeDeclaration,
                typeState.TypeSymbol,
                methodDeclaration,
                methodSymbol,
                methodBodyNode,
                semanticModel);
        if (isAddedMethod && decision.SkipReason == null)
        {
            decision = DecideAddedMethodAccessors(
                methodSymbol,
                typeState.TypeSymbol,
                methodBodyNode,
                semanticModel,
                decision);
        }

        return decision;
    }

    private static (int ShimTypeCounter, int GlobalShimMethodCounter) QueueDecidedOrdinaryMethod(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        MethodTransformDecision decision,
        bool isAddedMethod,
        bool replacesCompiledMethod,
        string methodKey,
        string syntaxMethodKey,
        string[] parameterTypeFullNames,
        TypeEmitState typeState,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedMethodCatalog addedMethodCatalog,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        List<string> declarationDriftWarnings,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures,
        int shimTypeCounter,
        int globalShimMethodCounter)
    {
        ShimTypeBuilder shimType;
        (shimType, shimTypeCounter) = EnsureShimType(
            typeState,
            root,
            assemblyGlobalUsings,
            shimTypes,
            shimTypeCounter);
        string shimMethodName = methodSymbol.Name + "__shim" + globalShimMethodCounter;
        globalShimMethodCounter++;

        FileLinePositionSpan originalSpan = methodDeclaration.GetLocation().GetLineSpan();
        QueuedShimMethod queued = new QueuedShimMethod
        {
            MethodDeclaration = methodDeclaration,
            MethodSymbol = methodSymbol,
            Decision = decision,
            ShimMethodName = shimMethodName,
            ShimType = shimType,
            SourceStartLine = originalSpan.StartLinePosition.Line + 1,
            SourceEndLine = originalSpan.EndLinePosition.Line + 1,
            ParameterTypeFullNames = parameterTypeFullNames,
            MethodKey = methodKey,
            IsAddedMethod = isAddedMethod,
            ReplacesCompiledMethod = replacesCompiledMethod
        };
        typeState.QueuedMethods.Add(queued);

        if (replacesCompiledMethod)
        {
            AddRemovedMethodName(removedMembers, methodSymbol.Name);
            AddRemovedMethodSignature(
                removedMethodSignatures,
                typeState.TypeSymbol,
                methodSymbol.Name,
                parameterTypeFullNames,
                methodSymbol.Arity);
        }

        if (isAddedMethod)
        {
            addedMethodCatalog.Register(
                new AddedMethodBinding
                {
                    MethodKey = methodKey,
                    ShimTypeName = shimType.ShimTypeName,
                    ShimMethodName = shimMethodName,
                    NamespaceName = shimType.NamespaceName,
                    IsStatic = methodSymbol.IsStatic
                });
            RecordHandledAddedMethodSyntaxKey(
                addedMethodCatalog,
                syntaxMethodKey,
                replacesCompiledMethod,
                snapshotMethodMap,
                plainCurrentMethodMap);
            AppendUnityMessageWarningIfNeeded(
                typeState.TypeSymbol,
                methodSymbol,
                declarationDriftWarnings);
        }

        return (shimTypeCounter, globalShimMethodCounter);
    }

    private static (ShimTypeBuilder ShimType, int ShimTypeCounter) EnsureShimType(
        TypeEmitState typeState,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        int shimTypeCounter)
    {
        if (typeState.CurrentShimType != null)
        {
            return (typeState.CurrentShimType, shimTypeCounter);
        }

        string shimTypeName = typeState.TypeSymbol.Name + "_UloopHotReloadShims_" + shimTypeCounter;
        shimTypeCounter++;
        string namespaceName = typeState.TypeSymbol.ContainingNamespace == null
            || typeState.TypeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeState.TypeSymbol.ContainingNamespace.ToDisplayString();
        typeState.CurrentShimType = new ShimTypeBuilder(
            shimTypeName,
            namespaceName,
            CollectUsingsForType(root, typeState.TypeDeclaration, assemblyGlobalUsings));
        shimTypes.Add(typeState.CurrentShimType);
        return (typeState.CurrentShimType, shimTypeCounter);
    }

    private static void EmitQueuedMethods(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerEntry> entries)
    {
        foreach (QueuedShimMethod queued in typeState.QueuedMethods)
        {
            AccessorPlan rewritePlan = queued.Decision.UsesDelegation
                ? queued.ShimType.AccessorPlan
                : null;
            MethodDeclarationSyntax rewrittenMethod = RewriteMethodBody(
                queued.MethodDeclaration,
                queued.MethodSymbol,
                typeState.TypeSymbol,
                semanticModel,
                rewritePlan,
                addedMethodCatalog,
                addedFieldCatalog);
            queued.ShimType.AddMethod(rewrittenMethod, queued.ShimMethodName);

            SyntaxNode bodyNode =
                (SyntaxNode)queued.MethodDeclaration.Body ?? queued.MethodDeclaration.ExpressionBody;
            string[] calledAddedMethodKeys = CollectCalledAddedMethodKeys(
                bodyNode,
                semanticModel,
                addedMethodCatalog,
                queued.MethodKey);

            entries.Add(new WorkerEntry
            {
                TypeMetadataName = CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
                MethodName = queued.MethodSymbol.Name,
                ParameterTypeFullNames = queued.ParameterTypeFullNames,
                GenericArity = queued.MethodSymbol.Arity,
                ShimTypeName = queued.ShimType.ShimTypeName,
                ShimMethodName = queued.ShimMethodName,
                PatchKind = queued.Decision.PatchKind,
                CalledAddedMethodKeys = calledAddedMethodKeys,
                SourceStartLine = queued.SourceStartLine,
                SourceEndLine = queued.SourceEndLine,
                LifecycleNote = ComputeLifecycleNote(
                    queued.MethodDeclaration,
                    queued.MethodSymbol,
                    typeState.TypeSymbol),
                ReplacesCompiledMethod = queued.ReplacesCompiledMethod
            });
        }
    }

    private static void CollectRemovedMethods(
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        AddedMethodCatalog addedMethodCatalog,
        List<WorkerRemovedMember> removedMembers)
    {
        HashSet<string> seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorkerRemovedMember existing in removedMembers)
        {
            if (existing.Kind == RemovedMemberKinds.Method)
            {
                seenNames.Add(existing.Name);
            }
        }

        foreach (KeyValuePair<string, MethodDeclarationSyntax> pair in snapshotMethodMap)
        {
            if (plainCurrentMethodMap.ContainsKey(pair.Key))
            {
                continue;
            }

            addedMethodCatalog.AddRemovedSyntaxKey(pair.Key);
            string name = pair.Value.Identifier.Text;
            if (!seenNames.Add(name))
            {
                continue;
            }

            removedMembers.Add(new WorkerRemovedMember
            {
                Kind = RemovedMemberKinds.Method,
                Name = name
            });
        }
    }

    private static void CollectRemovedMethodSignaturesForDeletedNames(
        List<TypeEmitState> typeEmitStates,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures)
    {
        HashSet<string> removedMethodNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorkerRemovedMember removed in removedMembers)
        {
            if (removed.Kind == RemovedMemberKinds.Method)
            {
                removedMethodNames.Add(removed.Name);
            }
        }

        if (removedMethodNames.Count == 0)
        {
            return;
        }

        foreach (TypeEmitState typeState in typeEmitStates)
        {
            if (typeState.TypeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                // Why skip: a compiled type includes methods declared in other files of a
                // partial. A name missing from this file is not proof the method was deleted.
                continue;
            }

            INamedTypeSymbol compiledType = FindCompiledType(typeState.TypeSymbol, targetTypesAssemblySymbol);
            if (compiledType == null)
            {
                continue;
            }

            foreach (ISymbol member in compiledType.GetMembers())
            {
                if (member is not IMethodSymbol compiledMethod
                    || compiledMethod.MethodKind != MethodKind.Ordinary
                    || !removedMethodNames.Contains(compiledMethod.Name))
                {
                    continue;
                }

                if (SourceDeclarationCoversCompiledMethod(typeState, semanticModel, compiledMethod))
                {
                    continue;
                }

                string[] parameterTypeFullNames = compiledMethod.Parameters
                    .Select(CecilTypeNames.ToParameterTypeFullName)
                    .ToArray();
                AddRemovedMethodSignature(
                    removedMethodSignatures,
                    typeState.TypeSymbol,
                    compiledMethod.Name,
                    parameterTypeFullNames,
                    compiledMethod.Arity);
            }
        }
    }

    private static bool SourceDeclarationCoversCompiledMethod(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        IMethodSymbol compiledMethod)
    {
        string[] compiledParameterTypeFullNames = compiledMethod.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        foreach (MethodDeclarationSyntax methodDeclaration in typeState.TypeDeclaration.Members
            .OfType<MethodDeclarationSyntax>())
        {
            IMethodSymbol sourceMethod = semanticModel.GetDeclaredSymbol(methodDeclaration);
            if (sourceMethod == null
                || sourceMethod.MethodKind != MethodKind.Ordinary
                || sourceMethod.Name != compiledMethod.Name
                || sourceMethod.Arity != compiledMethod.Arity
                || sourceMethod.IsStatic != compiledMethod.IsStatic
                || sourceMethod.Parameters.Length != compiledParameterTypeFullNames.Length)
            {
                continue;
            }

            bool parametersMatch = true;
            for (int index = 0; index < compiledParameterTypeFullNames.Length; index++)
            {
                if (CecilTypeNames.ToParameterTypeFullName(sourceMethod.Parameters[index])
                    != compiledParameterTypeFullNames[index])
                {
                    parametersMatch = false;
                    break;
                }
            }

            if (parametersMatch)
            {
                return true;
            }
        }

        return false;
    }

    // Why strip current always, snapshot only when equivalent: a return-type-only
    // change keeps the same syntax key (name+params). Stripping only the current tree
    // leaves the snapshot's old return type as unhandled outside-body drift. Stripping
    // both unconditionally hid attribute/accessibility diffs that still need the warning.
    private static void RecordHandledAddedMethodSyntaxKey(
        AddedMethodCatalog addedMethodCatalog,
        string syntaxKey,
        bool replacesCompiledMethod,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap)
    {
        addedMethodCatalog.AddAddedSyntaxKey(syntaxKey);
        if (!replacesCompiledMethod || snapshotMethodMap == null || plainCurrentMethodMap == null)
        {
            return;
        }

        snapshotMethodMap.TryGetValue(syntaxKey, out MethodDeclarationSyntax snapshotDecl);
        plainCurrentMethodMap.TryGetValue(syntaxKey, out MethodDeclarationSyntax currentDecl);
        if (AreDeclarationsEquivalentIgnoringBodyAndReturnType(snapshotDecl, currentDecl))
        {
            addedMethodCatalog.AddRemovedSyntaxKey(syntaxKey);
        }
    }

    private static bool AreDeclarationsEquivalentIgnoringBodyAndReturnType(
        MethodDeclarationSyntax snapshotDecl,
        MethodDeclarationSyntax currentDecl)
    {
        if (snapshotDecl == null || currentDecl == null)
        {
            return false;
        }

        MethodDeclarationSyntax normalizedSnapshot =
            NormalizeDeclarationIgnoringBodyAndReturnType(snapshotDecl);
        MethodDeclarationSyntax normalizedCurrent =
            NormalizeDeclarationIgnoringBodyAndReturnType(currentDecl);
        return SyntaxFactory.AreEquivalent(normalizedSnapshot, normalizedCurrent, topLevel: false);
    }

    private static MethodDeclarationSyntax NormalizeDeclarationIgnoringBodyAndReturnType(
        MethodDeclarationSyntax method)
    {
        TypeSyntax placeholderReturn = SyntaxFactory.PredefinedType(
            SyntaxFactory.Token(SyntaxKind.VoidKeyword));
        return method
            .WithReturnType(placeholderReturn)
            .WithBody(null)
            .WithExpressionBody(null)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            .NormalizeWhitespace();
    }

    private static void AddRemovedMethodName(List<WorkerRemovedMember> removedMembers, string name)
    {
        foreach (WorkerRemovedMember existing in removedMembers)
        {
            if (existing.Kind == RemovedMemberKinds.Method && existing.Name == name)
            {
                return;
            }
        }

        removedMembers.Add(new WorkerRemovedMember
        {
            Kind = RemovedMemberKinds.Method,
            Name = name
        });
    }

    private static void AddRemovedMethodSignature(
        List<WorkerRemovedMethodSignature> removedMethodSignatures,
        INamedTypeSymbol sourceType,
        string methodName,
        string[] parameterTypeFullNames,
        int genericArity)
    {
        string typeMetadataName = CecilTypeNames.ToMetadataName(sourceType);
        foreach (WorkerRemovedMethodSignature existing in removedMethodSignatures)
        {
            if (existing.TypeMetadataName == typeMetadataName
                && existing.MethodName == methodName
                && existing.GenericArity == genericArity
                && ParameterTypeFullNamesEqual(existing.ParameterTypeFullNames, parameterTypeFullNames))
            {
                return;
            }
        }

        removedMethodSignatures.Add(new WorkerRemovedMethodSignature
        {
            TypeMetadataName = typeMetadataName,
            MethodName = methodName,
            ParameterTypeFullNames = parameterTypeFullNames,
            GenericArity = genericArity
        });
    }

    private static bool ParameterTypeFullNamesEqual(string[] left, string[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static void CollectRemovedFields(
        Dictionary<string, VariableDeclaratorSyntax> snapshotFieldMap,
        Dictionary<string, VariableDeclaratorSyntax> plainCurrentFieldMap,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerRemovedMember> removedMembers)
    {
        HashSet<string> seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, VariableDeclaratorSyntax> pair in snapshotFieldMap)
        {
            if (plainCurrentFieldMap.ContainsKey(pair.Key))
            {
                continue;
            }

            addedFieldCatalog.AddRemovedSyntaxKey(pair.Key);
            string name = pair.Value.Identifier.Text;
            if (!seenNames.Add(name))
            {
                continue;
            }

            removedMembers.Add(new WorkerRemovedMember
            {
                Kind = RemovedMemberKinds.Field,
                Name = name
            });
        }
    }

    private static void SkipAllMethodsOnUncompiledType(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped,
        AddedMethodCatalog addedMethodCatalog)
    {
        typeState.TypeIsAbsentFromCompiledAssembly = true;
        addedMethodCatalog.AddAddedTypeSyntaxKey(typeState.TypeMetadataNameFromSyntax);
        foreach (MethodDeclarationSyntax methodDeclaration in typeState.TypeDeclaration.Members
            .OfType<MethodDeclarationSyntax>())
        {
            IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);
            if (methodSymbol == null)
            {
                continue;
            }

            skipped.Add(new WorkerSkipped
            {
                Method = FormatMethodLabel(methodSymbol),
                Reason = AddedMethodSkipReasons.NewTypeOutOfScope
            });
        }
    }

    private static void SkipPropertyGetterOnUncompiledType(
        PropertyDeclarationSyntax propertyDeclaration,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped)
    {
        IPropertySymbol propertySymbol = semanticModel.GetDeclaredSymbol(propertyDeclaration);
        if (propertySymbol == null || propertySymbol.GetMethod == null)
        {
            return;
        }

        (bool hasGetterBody, AccessorDeclarationSyntax _) =
            TryGetPropertyGetterBody(propertyDeclaration);
        if (!hasGetterBody)
        {
            return;
        }

        skipped.Add(new WorkerSkipped
        {
            Method = FormatMethodLabel(propertySymbol.GetMethod),
            Reason = AddedMethodSkipReasons.NewTypeOutOfScope
        });
    }

    private static void SkipBodiesThatCannotUseAddedMethods(
        List<TypeEmitState> typeEmitStates,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerSkipped> skipped)
    {
        bool progressed;
        do
        {
            progressed = false;
            foreach (TypeEmitState typeState in typeEmitStates)
            {
                List<QueuedShimMethod> remaining = new List<QueuedShimMethod>();
                foreach (QueuedShimMethod queued in typeState.QueuedMethods)
                {
                    SyntaxNode bodyNode =
                        (SyntaxNode)queued.MethodDeclaration.Body ?? queued.MethodDeclaration.ExpressionBody;
                    string skipReason;
                    string calledAddedMethodKey;
                    (skipReason, calledAddedMethodKey) = EvaluateAddedCallSiteSkipReason(
                        bodyNode,
                        semanticModel,
                        addedMethodCatalog,
                        addedFieldCatalog);
                    if (skipReason != null)
                    {
                        skipped.Add(new WorkerSkipped
                        {
                            Method = FormatMethodLabel(queued.MethodSymbol),
                            Reason = skipReason,
                            CalledAddedMethodKey = calledAddedMethodKey,
                            MethodKey = calledAddedMethodKey == null ? null : queued.MethodKey
                        });
                        if (queued.IsAddedMethod)
                        {
                            addedMethodCatalog.Unregister(queued.MethodKey);
                        }

                        progressed = true;
                        continue;
                    }

                    remaining.Add(queued);
                }

                typeState.QueuedMethods.Clear();
                typeState.QueuedMethods.AddRange(remaining);
            }
        }
        while (progressed);
    }

    private static (string Reason, string CalledAddedMethodKey) EvaluateAddedCallSiteSkipReason(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        if (bodyNode == null)
        {
            return (null, null);
        }

        foreach (InvocationExpressionSyntax invocation in bodyNode.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>())
        {
            if (NameofRules.IsNameofInvocation(invocation))
            {
                continue;
            }

            ISymbol symbol = semanticModel.GetSymbolInfo(invocation).Symbol;
            if (symbol is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            string calledKey = BuildMethodKeyFromSymbol(methodSymbol);
            // Why the receiver spine (not a WhenNotNull ancestor walk): other?.Inner.AddedPing()
            // and other?.Get().AddedPing() walk left to a MemberBinding. An ancestor walk also
            // matches argument-list / lambda invocations that are ordinary rewrite targets.
            if (IsConditionalAccessReceiverSpine(invocation)
                && addedMethodCatalog.IsClassifiedAdded(calledKey))
            {
                return (AddedMethodSkipReasons.ConditionalAccess, null);
            }

            if (addedMethodCatalog.IsUnavailableAdded(calledKey))
            {
                return (AddedMethodSkipReasons.UnavailableAddedCall, calledKey);
            }
        }

        if (BodyReferencesAddedMethodGroup(bodyNode, semanticModel, addedMethodCatalog))
        {
            return (AddedMethodSkipReasons.MethodGroupReference, null);
        }

        return (EvaluateAddedFieldSkipReason(bodyNode, semanticModel, addedFieldCatalog), null);
    }

    private static bool BodyReferencesAddedMethodGroup(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog)
    {
        foreach (IdentifierNameSyntax name in bodyNode.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (IsInvocationCalleeName(name) || NameofRules.IsInsideNameofArgument(name))
            {
                continue;
            }

            ISymbol symbol = semanticModel.GetSymbolInfo(name).Symbol;
            if (symbol is IMethodSymbol methodSymbol
                && addedMethodCatalog.IsClassifiedAdded(BuildMethodKeyFromSymbol(methodSymbol)))
            {
                return true;
            }
        }

        foreach (MemberAccessExpressionSyntax access in bodyNode.DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>())
        {
            if ((access.Parent is InvocationExpressionSyntax invocation && invocation.Expression == access)
                || NameofRules.IsInsideNameofArgument(access))
            {
                continue;
            }

            ISymbol symbol = semanticModel.GetSymbolInfo(access).Symbol;
            if (symbol is IMethodSymbol methodSymbol
                && addedMethodCatalog.IsClassifiedAdded(BuildMethodKeyFromSymbol(methodSymbol)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInvocationCalleeName(IdentifierNameSyntax name)
    {
        if (name.Parent is InvocationExpressionSyntax invocation && invocation.Expression == name)
        {
            return true;
        }

        if (name.Parent is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name == name
            && memberAccess.Parent is InvocationExpressionSyntax memberInvocation
            && memberInvocation.Expression == memberAccess)
        {
            return true;
        }

        if (name.Parent is MemberBindingExpressionSyntax memberBinding
            && memberBinding.Name == name
            && memberBinding.Parent is InvocationExpressionSyntax bindingInvocation
            && bindingInvocation.Expression == memberBinding)
        {
            return true;
        }

        return false;
    }

    // Why unknown→false: MemberBinding/ElementBinding can appear as the leftmost receiver
    // only along MemberAccess / ElementAccess / Invocation / postfix ! / ConditionalAccess /
    // Parenthesized. Cast / new / await / ternary / literals are complete expressions;
    // ExtractReceiver splices them as valid source. Returning true here would skip ordinary
    // calls with a "conditional access" reason and would suppress accessor rewrite of private
    // methods on those receivers (fields would still rewrite — VisitMemberAccess has no guard).
    internal static bool IsConditionalAccessReceiverSpine(InvocationExpressionSyntax invocation)
    {
        ExpressionSyntax current = invocation.Expression;
        while (current != null)
        {
            if (current is MemberBindingExpressionSyntax || current is ElementBindingExpressionSyntax)
            {
                return true;
            }

            ExpressionSyntax unwrapped = TryUnwrapReceiverSpineExpression(current);
            if (unwrapped != null)
            {
                current = unwrapped;
                continue;
            }

            return false;
        }

        return false;
    }

    private static ExpressionSyntax TryUnwrapReceiverSpineExpression(ExpressionSyntax expression)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Expression;
        }

        if (expression is ElementAccessExpressionSyntax elementAccess)
        {
            return elementAccess.Expression;
        }

        if (expression is InvocationExpressionSyntax innerInvocation)
        {
            return innerInvocation.Expression;
        }

        if (expression is PostfixUnaryExpressionSyntax postfix
            && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        {
            return postfix.Operand;
        }

        if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
        {
            return conditionalAccess.Expression;
        }

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return parenthesized.Expression;
        }

        return null;
    }

    private static string[] CollectCalledAddedMethodKeys(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        string selfMethodKey)
    {
        if (bodyNode == null)
        {
            return Array.Empty<string>();
        }

        HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (InvocationExpressionSyntax invocation in bodyNode.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>())
        {
            if (NameofRules.IsNameofInvocation(invocation))
            {
                continue;
            }

            ISymbol symbol = semanticModel.GetSymbolInfo(invocation).Symbol;
            if (symbol is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            string calledKey = BuildMethodKeyFromSymbol(methodSymbol);
            if (calledKey == selfMethodKey)
            {
                continue;
            }

            if (addedMethodCatalog.Contains(calledKey))
            {
                keys.Add(calledKey);
            }
        }

        if (keys.Count == 0)
        {
            return Array.Empty<string>();
        }

        string[] result = new string[keys.Count];
        keys.CopyTo(result);
        return result;
    }

    private static INamedTypeSymbol FindCompiledType(
        INamedTypeSymbol sourceType,
        IAssemblySymbol targetTypesAssemblySymbol)
    {
        if (sourceType == null || targetTypesAssemblySymbol == null)
        {
            return null;
        }

        return targetTypesAssemblySymbol.GetTypeByMetadataName(ToReflectionMetadataName(sourceType));
    }

    private static CompiledMethodMatch MatchCompiledOrdinaryMethod(
        INamedTypeSymbol compiledType,
        IMethodSymbol sourceMethod)
    {
        string[] sourceParameterTypeFullNames = sourceMethod.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        foreach (ISymbol member in compiledType.GetMembers(sourceMethod.Name))
        {
            // Why compare Arity: Caller(int) and Caller<T>(int) must not share a compiled match.
            if (member is not IMethodSymbol compiledMethod
                || compiledMethod.MethodKind != MethodKind.Ordinary
                || compiledMethod.Arity != sourceMethod.Arity
                || compiledMethod.IsStatic != sourceMethod.IsStatic
                || compiledMethod.Parameters.Length != sourceParameterTypeFullNames.Length)
            {
                continue;
            }

            bool parametersMatch = true;
            for (int index = 0; index < sourceParameterTypeFullNames.Length; index++)
            {
                if (CecilTypeNames.ToParameterTypeFullName(compiledMethod.Parameters[index])
                    != sourceParameterTypeFullNames[index])
                {
                    parametersMatch = false;
                    break;
                }
            }

            if (!parametersMatch)
            {
                continue;
            }

            if (ReturnTypesMatch(compiledMethod, sourceMethod))
            {
                return CompiledMethodMatch.Matched;
            }

            return CompiledMethodMatch.ReturnTypeChanged;
        }

        return CompiledMethodMatch.NotFound;
    }

    private static bool ReturnTypesMatch(IMethodSymbol compiledMethod, IMethodSymbol sourceMethod)
    {
        if (CecilTypeNames.ToCecilFullName(compiledMethod.ReturnType)
            != CecilTypeNames.ToCecilFullName(sourceMethod.ReturnType))
        {
            return false;
        }

        // Why compare byref flags separately: ToCecilFullName sees only ITypeSymbol, so
        // int F() and ref int F() both become System.Int32. Missing this would transplant
        // the new body onto the old non-byref signature.
        return compiledMethod.ReturnsByRef == sourceMethod.ReturnsByRef
            && compiledMethod.ReturnsByRefReadonly == sourceMethod.ReturnsByRefReadonly;
    }

    /// <summary>
    /// What: matches a source field to a compiled member by name, then reports type,
    /// static/const, or property/event kind drift so callers can skip instead of
    /// rewriting storage.
    /// </summary>
    private static CompiledFieldMatch MatchCompiledField(
        INamedTypeSymbol compiledType,
        IFieldSymbol sourceField)
    {
        foreach (ISymbol member in compiledType.GetMembers(sourceField.Name))
        {
            if (member is not IFieldSymbol compiledField)
            {
                // Why: a compiled property or event still owns this name. Treating the
                // source field as added would duplicate storage in the side table.
                if (member.Kind == SymbolKind.Property || member.Kind == SymbolKind.Event)
                {
                    return CompiledFieldMatch.MemberKindChanged;
                }

                continue;
            }

            // Why return here: C# field names are unique in a type, so a modifier
            // mismatch is the compiled field, not a miss that should become a store.
            if (compiledField.IsStatic != sourceField.IsStatic
                || compiledField.IsConst != sourceField.IsConst)
            {
                return CompiledFieldMatch.FieldModifiersChanged;
            }

            if (CecilTypeNames.ToCecilFullName(compiledField.Type)
                == CecilTypeNames.ToCecilFullName(sourceField.Type))
            {
                return CompiledFieldMatch.Matched;
            }

            return CompiledFieldMatch.FieldTypeChanged;
        }

        return CompiledFieldMatch.NotFound;
    }

    private static bool IsCompiledFieldDeclarationChange(CompiledFieldMatch fieldMatch)
    {
        return fieldMatch == CompiledFieldMatch.FieldTypeChanged
            || fieldMatch == CompiledFieldMatch.FieldModifiersChanged
            || fieldMatch == CompiledFieldMatch.MemberKindChanged;
    }

    private static string TryFormatCompiledFieldDeclarationChangeReason(
        CompiledFieldMatch fieldMatch,
        string fieldName)
    {
        if (fieldMatch == CompiledFieldMatch.FieldTypeChanged)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                AddedFieldSkipReasons.FieldTypeChanged,
                fieldName);
        }

        if (fieldMatch == CompiledFieldMatch.FieldModifiersChanged)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                AddedFieldSkipReasons.FieldModifiersChanged,
                fieldName);
        }

        if (fieldMatch == CompiledFieldMatch.MemberKindChanged)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                AddedFieldSkipReasons.MemberKindChanged,
                fieldName);
        }

        return null;
    }

    /// <summary>
    /// What: classifies source fields missing from the compiled type as added, and records
    /// store/const/unavailable bindings used by skip evaluation and body rewrite.
    /// </summary>
    private static void ClassifyAddedFields(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        INamedTypeSymbol compiledType,
        IAssemblySymbol targetTypesAssemblySymbol,
        AddedFieldCatalog addedFieldCatalog,
        List<string> declarationDriftWarnings)
    {
        foreach (FieldDeclarationSyntax fieldDeclaration in typeState.TypeDeclaration.Members
            .OfType<FieldDeclarationSyntax>())
        {
            foreach (VariableDeclaratorSyntax variable in fieldDeclaration.Declaration.Variables)
            {
                IFieldSymbol fieldSymbol = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (fieldSymbol == null)
                {
                    continue;
                }

                CompiledFieldMatch fieldMatch = MatchCompiledField(compiledType, fieldSymbol);
                if (fieldMatch == CompiledFieldMatch.Matched)
                {
                    continue;
                }

                ClassifyOneAddedField(
                    typeState,
                    semanticModel,
                    targetTypesAssemblySymbol,
                    fieldDeclaration,
                    variable,
                    fieldSymbol,
                    addedFieldCatalog,
                    declarationDriftWarnings,
                    fieldMatch);
            }
        }
    }

    private static void ClassifyOneAddedField(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol,
        FieldDeclarationSyntax fieldDeclaration,
        VariableDeclaratorSyntax variable,
        IFieldSymbol fieldSymbol,
        AddedFieldCatalog addedFieldCatalog,
        List<string> declarationDriftWarnings,
        CompiledFieldMatch fieldMatch)
    {
        string syntaxKey = BuildSyntaxFieldKey(typeState.TypeMetadataNameFromSyntax, fieldSymbol.Name);
        string fieldKey = FormatAddedFieldStoreKey(
            CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
            fieldSymbol.Name);
        addedFieldCatalog.MarkClassifiedAdded(fieldKey);
        addedFieldCatalog.AddAddedSyntaxKey(syntaxKey);

        if (!IsCompiledFieldDeclarationChange(fieldMatch)
            && FieldHasSerializationAttribute(fieldDeclaration))
        {
            declarationDriftWarnings.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    AddedFieldSkipReasons.SerializeWarningFormat,
                    fieldSymbol.Name));
        }

        AddedFieldBinding binding = new AddedFieldBinding
        {
            FieldKey = fieldKey,
            SyntaxKey = syntaxKey,
            FieldName = fieldSymbol.Name,
            FieldType = fieldSymbol.Type,
            IsStatic = fieldSymbol.IsStatic,
            IsConst = fieldSymbol.IsConst,
            ConstantValue = fieldSymbol.HasConstantValue ? fieldSymbol.ConstantValue : null,
            Initializer = variable.Initializer != null ? variable.Initializer.Value : null
        };

        string declarationChangeReason = TryFormatCompiledFieldDeclarationChangeReason(
            fieldMatch,
            fieldSymbol.Name);
        if (declarationChangeReason != null)
        {
            // Why not RegisterStore: rewriting to the side table would hide the
            // declaration change and leave compiled callers on the old field.
            binding.UnavailableReason = declarationChangeReason;
            addedFieldCatalog.RegisterUnavailable(binding);
            return;
        }

        binding.UnavailableReason = EvaluateAddedFieldAvailability(
            typeState.TypeSymbol,
            semanticModel,
            targetTypesAssemblySymbol,
            fieldSymbol,
            binding);

        if (binding.UnavailableReason != null)
        {
            addedFieldCatalog.RegisterUnavailable(binding);
            return;
        }

        if (fieldSymbol.IsConst)
        {
            addedFieldCatalog.RegisterConst(binding);
            return;
        }

        addedFieldCatalog.RegisterStore(binding);
    }

    private static string EvaluateAddedFieldAvailability(
        INamedTypeSymbol hostType,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol,
        IFieldSymbol fieldSymbol,
        AddedFieldBinding binding)
    {
        if (fieldSymbol.IsConst)
        {
            if (TryCreateConstantLiteral(binding.ConstantValue, fieldSymbol.Type) == null)
            {
                return AddedFieldSkipReasons.UnavailableAddedField;
            }

            return null;
        }

        // Why after const: added consts on struct hosts still fold to literals; the store
        // identity problem only applies to instance/static storage.
        if (hostType.TypeKind == TypeKind.Struct)
        {
            return AddedFieldSkipReasons.StructHost;
        }

        if (!AccessibilityRules.IsExternallyVisibleType(fieldSymbol.Type))
        {
            return AddedFieldSkipReasons.FieldTypeNotExternallyVisible;
        }

        if (binding.Initializer != null
            && InitializerCannotEmitInShimLambda(
                binding.Initializer,
                semanticModel,
                hostType,
                targetTypesAssemblySymbol))
        {
            return AddedFieldSkipReasons.InitializerNotLiteralOrExternalStatic;
        }

        return null;
    }

    // Why this gate (not inaccessible-only): the initializer is spliced into a static lambda on
    // a shim type, so even public instance members of the host are CS0103 / CS0026, and
    // same-file added members do not exist on the compiled type the shim references.
    private static bool InitializerCannotEmitInShimLambda(
        ExpressionSyntax initializer,
        SemanticModel semanticModel,
        INamedTypeSymbol hostType,
        IAssemblySymbol targetTypesAssemblySymbol)
    {
        foreach (SyntaxNode node in initializer.DescendantNodesAndSelf())
        {
            if (NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            if (node is ThisExpressionSyntax || node is BaseExpressionSyntax)
            {
                return true;
            }

            if (HasDisallowedInitializerSymbol(
                semanticModel.GetSymbolInfo(node).Symbol,
                hostType,
                targetTypesAssemblySymbol,
                initializer.SyntaxTree))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDisallowedInitializerSymbol(
        ISymbol symbol,
        INamedTypeSymbol hostType,
        IAssemblySymbol targetTypesAssemblySymbol,
        SyntaxTree currentTree)
    {
        if (symbol == null
            || symbol is INamespaceSymbol
            || symbol is ITypeSymbol
            || symbol is ILabelSymbol
            || symbol is IRangeVariableSymbol)
        {
            return false;
        }

        if (symbol is not IFieldSymbol
            && symbol is not IPropertySymbol
            && symbol is not IMethodSymbol
            && symbol is not IEventSymbol)
        {
            return false;
        }

        if (!symbol.IsStatic)
        {
            return true;
        }

        if (hostType != null
            && SymbolEqualityComparer.Default.Equals(symbol.ContainingType, hostType))
        {
            return true;
        }

        if (IsSameFileAddedMember(symbol, targetTypesAssemblySymbol, currentTree))
        {
            return true;
        }

        return AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol);
    }

    private static bool IsSameFileAddedMember(
        ISymbol symbol,
        IAssemblySymbol targetTypesAssemblySymbol,
        SyntaxTree currentTree)
    {
        if (symbol.ContainingType == null || currentTree == null)
        {
            return false;
        }

        bool declaredInCurrentTree = false;
        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree == currentTree)
            {
                declaredInCurrentTree = true;
                break;
            }
        }

        if (!declaredInCurrentTree)
        {
            return false;
        }

        INamedTypeSymbol compiledType = FindCompiledType(symbol.ContainingType, targetTypesAssemblySymbol);
        if (compiledType == null)
        {
            return true;
        }

        if (symbol is IFieldSymbol fieldSymbol)
        {
            // Why map any non-Matched result to added: FieldTypeChanged,
            // FieldModifiersChanged, and MemberKindChanged still name compiled
            // storage, so treating them as a direct shim reference would bind it.
            return MatchCompiledField(compiledType, fieldSymbol) != CompiledFieldMatch.Matched;
        }

        if (symbol is IMethodSymbol methodSymbol && methodSymbol.MethodKind == MethodKind.Ordinary)
        {
            CompiledMethodMatch match = MatchCompiledOrdinaryMethod(compiledType, methodSymbol);
            // Why map ReturnTypeChanged to added: the compiled method still has the old
            // signature, so treating it as a direct shim reference would bind the old body.
            return match != CompiledMethodMatch.Matched;
        }

        foreach (ISymbol member in compiledType.GetMembers(symbol.Name))
        {
            if (member.Kind == symbol.Kind)
            {
                return false;
            }
        }

        return true;
    }

    private static bool FieldHasSerializationAttribute(FieldDeclarationSyntax fieldDeclaration)
    {
        foreach (AttributeListSyntax attributeList in fieldDeclaration.AttributeLists)
        {
            foreach (AttributeSyntax attribute in attributeList.Attributes)
            {
                string name = attribute.Name.ToString();
                int lastDot = name.LastIndexOf('.');
                string simpleName = lastDot >= 0 ? name.Substring(lastDot + 1) : name;
                if (simpleName == "SerializeField"
                    || simpleName == "SerializeReference"
                    || simpleName == "FormerlySerializedAs")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string FormatAddedFieldStoreKey(string typeMetadataName, string fieldName)
    {
        return typeMetadataName + TransformWorkerProgramMarker.AddedFieldKeySeparator + fieldName;
    }

    private static string EvaluateAddedFieldSkipReason(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        if (bodyNode == null || addedFieldCatalog == null || !addedFieldCatalog.HasClassifiedAdded)
        {
            return null;
        }

        string unavailable = BodyReferencesUnavailableAddedField(bodyNode, semanticModel, addedFieldCatalog);
        if (unavailable != null)
        {
            return unavailable;
        }

        if (BodyPassesAddedFieldByRef(bodyNode, semanticModel, addedFieldCatalog))
        {
            return AddedFieldSkipReasons.RefOutIn;
        }

        if (BodyHasUnsupportedAddedFieldCompound(bodyNode, semanticModel, addedFieldCatalog))
        {
            return AddedFieldSkipReasons.UnavailableAddedField;
        }

        if (BodyHasNonNumericAddedFieldIncrement(bodyNode, semanticModel, addedFieldCatalog))
        {
            return AddedFieldSkipReasons.IncrementNotNumeric;
        }

        if (BodyHasConsumedAddedFieldWrite(bodyNode, semanticModel, addedFieldCatalog))
        {
            return AddedFieldSkipReasons.ConsumedWrite;
        }

        if (BodyHasDoubleEvalAddedFieldReceiver(bodyNode, semanticModel, addedFieldCatalog))
        {
            return AddedFieldSkipReasons.DoubleEvalReceiver;
        }

        if (BodyHasValueTypeAddedFieldMemberWrite(bodyNode, semanticModel, addedFieldCatalog))
        {
            return AddedFieldSkipReasons.ValueTypeMemberWrite;
        }

        return null;
    }

    private static string BodyReferencesUnavailableAddedField(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            IFieldSymbol field = TryGetFieldSymbolOrCandidate(semanticModel, node);
            if (field == null)
            {
                continue;
            }

            AddedFieldBinding binding = addedFieldCatalog.FindOrNull(FormatAddedFieldKeyFromSymbol(field));
            if (binding != null && binding.UnavailableReason != null)
            {
                return binding.UnavailableReason;
            }
        }

        return null;
    }

    private static bool BodyPassesAddedFieldByRef(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (ArgumentSyntax argument in bodyNode.DescendantNodesAndSelf().OfType<ArgumentSyntax>())
        {
            if (argument.RefKindKeyword.Kind() != SyntaxKind.RefKeyword
                && argument.RefKindKeyword.Kind() != SyntaxKind.OutKeyword
                && argument.RefKindKeyword.Kind() != SyntaxKind.InKeyword)
            {
                continue;
            }

            if (IsStoreAddedField(semanticModel, argument.Expression, addedFieldCatalog))
            {
                return true;
            }
        }

        foreach (RefExpressionSyntax refExpression in bodyNode.DescendantNodesAndSelf()
            .OfType<RefExpressionSyntax>())
        {
            if (IsStoreAddedField(semanticModel, refExpression.Expression, addedFieldCatalog))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BodyHasUnsupportedAddedFieldCompound(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (AssignmentExpressionSyntax assignment in bodyNode.DescendantNodesAndSelf()
            .OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                || AccessorEligibility.IsSupportedCompoundAssignmentKind(assignment.Kind()))
            {
                continue;
            }

            if (IsStoreAddedField(semanticModel, assignment.Left, addedFieldCatalog))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BodyHasConsumedAddedFieldWrite(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (AssignmentExpressionSyntax assignment in bodyNode.DescendantNodesAndSelf()
            .OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Parent is ExpressionStatementSyntax)
            {
                continue;
            }

            if (IsStoreAddedField(semanticModel, assignment.Left, addedFieldCatalog))
            {
                return true;
            }
        }

        foreach (PrefixUnaryExpressionSyntax prefix in bodyNode.DescendantNodesAndSelf()
            .OfType<PrefixUnaryExpressionSyntax>())
        {
            if (!IsIncrementOrDecrement(prefix.Kind()) || prefix.Parent is ExpressionStatementSyntax)
            {
                continue;
            }

            if (IsStoreAddedField(semanticModel, prefix.Operand, addedFieldCatalog))
            {
                return true;
            }
        }

        foreach (PostfixUnaryExpressionSyntax postfix in bodyNode.DescendantNodesAndSelf()
            .OfType<PostfixUnaryExpressionSyntax>())
        {
            if (!IsIncrementOrDecrement(postfix.Kind()) || postfix.Parent is ExpressionStatementSyntax)
            {
                continue;
            }

            if (IsStoreAddedField(semanticModel, postfix.Operand, addedFieldCatalog))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BodyHasDoubleEvalAddedFieldReceiver(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (AssignmentExpressionSyntax assignment in bodyNode.DescendantNodesAndSelf()
            .OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                || !IsStoreAddedInstanceField(semanticModel, assignment.Left, addedFieldCatalog))
            {
                continue;
            }

            if (!AccessorEligibility.IsSideEffectFreeAssignmentReceiver(semanticModel, assignment.Left))
            {
                return true;
            }
        }

        foreach (PrefixUnaryExpressionSyntax prefix in bodyNode.DescendantNodesAndSelf()
            .OfType<PrefixUnaryExpressionSyntax>())
        {
            if (!IsIncrementOrDecrement(prefix.Kind())
                || !IsStoreAddedInstanceField(semanticModel, prefix.Operand, addedFieldCatalog))
            {
                continue;
            }

            if (!AccessorEligibility.IsSideEffectFreeAssignmentReceiver(semanticModel, prefix.Operand))
            {
                return true;
            }
        }

        foreach (PostfixUnaryExpressionSyntax postfix in bodyNode.DescendantNodesAndSelf()
            .OfType<PostfixUnaryExpressionSyntax>())
        {
            if (!IsIncrementOrDecrement(postfix.Kind())
                || !IsStoreAddedInstanceField(semanticModel, postfix.Operand, addedFieldCatalog))
            {
                continue;
            }

            if (!AccessorEligibility.IsSideEffectFreeAssignmentReceiver(semanticModel, postfix.Operand))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BodyHasNonNumericAddedFieldIncrement(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (PrefixUnaryExpressionSyntax prefix in bodyNode.DescendantNodesAndSelf()
            .OfType<PrefixUnaryExpressionSyntax>())
        {
            if (IsNonNumericAddedFieldIncrement(semanticModel, prefix.Kind(), prefix.Operand, addedFieldCatalog))
            {
                return true;
            }
        }

        foreach (PostfixUnaryExpressionSyntax postfix in bodyNode.DescendantNodesAndSelf()
            .OfType<PostfixUnaryExpressionSyntax>())
        {
            if (IsNonNumericAddedFieldIncrement(semanticModel, postfix.Kind(), postfix.Operand, addedFieldCatalog))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNonNumericAddedFieldIncrement(
        SemanticModel semanticModel,
        SyntaxKind kind,
        ExpressionSyntax operand,
        AddedFieldCatalog addedFieldCatalog)
    {
        if (!IsIncrementOrDecrement(kind) || !IsStoreAddedField(semanticModel, operand, addedFieldCatalog))
        {
            return false;
        }

        IFieldSymbol field = TryGetFieldSymbol(semanticModel, operand);
        return field != null && !IsIncrementablePrimitiveOrEnum(field.Type);
    }

    private static bool IsIncrementablePrimitiveOrEnum(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
        {
            return false;
        }

        if (typeSymbol.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        return IsIncrementableSpecialType(typeSymbol.SpecialType);
    }

    private static bool IsIncrementableSpecialType(SpecialType specialType)
    {
        switch (specialType)
        {
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Char:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return true;
            default:
                return false;
        }
    }

    private static bool BodyHasValueTypeAddedFieldMemberWrite(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (AssignmentExpressionSyntax assignment in bodyNode.DescendantNodesAndSelf()
            .OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is MemberAccessExpressionSyntax memberAccess
                && IsStoreAddedValueTypeField(semanticModel, memberAccess.Expression, addedFieldCatalog))
            {
                return true;
            }
        }

        foreach (InvocationExpressionSyntax invocation in bodyNode.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>())
        {
            if (NameofRules.IsNameofInvocation(invocation)
                || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                continue;
            }

            ISymbol invoked = semanticModel.GetSymbolInfo(invocation).Symbol;
            if (invoked is IMethodSymbol methodSymbol
                && !methodSymbol.IsStatic
                && IsStoreAddedValueTypeField(semanticModel, memberAccess.Expression, addedFieldCatalog))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsIncrementOrDecrement(SyntaxKind kind)
    {
        return kind == SyntaxKind.PreIncrementExpression
            || kind == SyntaxKind.PreDecrementExpression
            || kind == SyntaxKind.PostIncrementExpression
            || kind == SyntaxKind.PostDecrementExpression;
    }

    private static bool IsStoreAddedField(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        AddedFieldCatalog addedFieldCatalog)
    {
        IFieldSymbol field = TryGetFieldSymbol(semanticModel, expression);
        if (field == null)
        {
            return false;
        }

        AddedFieldBinding binding = addedFieldCatalog.FindOrNull(FormatAddedFieldKeyFromSymbol(field));
        return binding != null && binding.IsStoreRewriteable;
    }

    private static bool IsStoreAddedInstanceField(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        AddedFieldCatalog addedFieldCatalog)
    {
        IFieldSymbol field = TryGetFieldSymbol(semanticModel, expression);
        if (field == null || field.IsStatic)
        {
            return false;
        }

        AddedFieldBinding binding = addedFieldCatalog.FindOrNull(FormatAddedFieldKeyFromSymbol(field));
        return binding != null && binding.IsStoreRewriteable;
    }

    private static bool IsStoreAddedValueTypeField(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        AddedFieldCatalog addedFieldCatalog)
    {
        IFieldSymbol field = TryGetFieldSymbol(semanticModel, expression);
        if (field == null || !field.Type.IsValueType)
        {
            return false;
        }

        AddedFieldBinding binding = addedFieldCatalog.FindOrNull(FormatAddedFieldKeyFromSymbol(field));
        return binding != null && binding.IsStoreRewriteable;
    }

    private static IFieldSymbol TryGetFieldSymbol(SemanticModel semanticModel, SyntaxNode node)
    {
        if (node == null)
        {
            return null;
        }

        ISymbol symbol = semanticModel.GetSymbolInfo(node).Symbol;
        return symbol as IFieldSymbol;
    }

    private static IFieldSymbol TryGetFieldSymbolOrCandidate(
        SemanticModel semanticModel,
        SyntaxNode node)
    {
        IFieldSymbol field = TryGetFieldSymbol(semanticModel, node);
        if (field != null)
        {
            return field;
        }

        if (node == null)
        {
            return null;
        }

        // Why candidates: assigning to a const (or other illegal field use) still
        // names that field, but GetSymbolInfo leaves it in CandidateSymbols.
        foreach (ISymbol candidate in semanticModel.GetSymbolInfo(node).CandidateSymbols)
        {
            if (candidate is IFieldSymbol candidateField)
            {
                return candidateField;
            }
        }

        return null;
    }

    internal static string FormatAddedFieldKeyFromSymbol(IFieldSymbol fieldSymbol)
    {
        if (fieldSymbol.ContainingType == null)
        {
            return fieldSymbol.Name;
        }

        return FormatAddedFieldStoreKey(
            CecilTypeNames.ToMetadataName(fieldSymbol.ContainingType),
            fieldSymbol.Name);
    }

    internal static ExpressionSyntax TryCreateConstantLiteral(object value, ITypeSymbol type)
    {
        if (value == null)
        {
            return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
        }

        if (type != null && type.TypeKind == TypeKind.Enum)
        {
            ExpressionSyntax underlyingLiteral = TryCreateNumericOrBoolLiteral(value);
            if (underlyingLiteral == null)
            {
                return null;
            }

            return SyntaxFactory.CastExpression(
                SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                underlyingLiteral);
        }

        if (value is string text)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(text));
        }

        if (value is char character)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.CharacterLiteralExpression,
                SyntaxFactory.Literal(character));
        }

        return TryCreateNumericOrBoolLiteral(value);
    }

    private static ExpressionSyntax TryCreateNumericOrBoolLiteral(object value)
    {
        if (value is bool flag)
        {
            return SyntaxFactory.LiteralExpression(
                flag ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);
        }

        ExpressionSyntax integerLiteral = TryCreateInt32ThroughUInt64Literal(value);
        if (integerLiteral != null)
        {
            return integerLiteral;
        }

        ExpressionSyntax floatingLiteral = TryCreateFloatingLiteral(value);
        if (floatingLiteral != null)
        {
            return floatingLiteral;
        }

        return TryCreateDecimalOrSmallIntegerLiteral(value);
    }

    private static ExpressionSyntax TryCreateInt32ThroughUInt64Literal(object value)
    {
        if (value is int intValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(intValue));
        }

        if (value is uint uintValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(uintValue));
        }

        if (value is long longValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(longValue));
        }

        if (value is ulong ulongValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(ulongValue));
        }

        return null;
    }

    private static ExpressionSyntax TryCreateFloatingLiteral(object value)
    {
        if (value is float floatValue)
        {
            if (float.IsNaN(floatValue) || float.IsInfinity(floatValue))
            {
                return null;
            }

            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(floatValue));
        }

        if (value is double doubleValue)
        {
            if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue))
            {
                return null;
            }

            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(doubleValue));
        }

        return null;
    }

    private static ExpressionSyntax TryCreateDecimalOrSmallIntegerLiteral(object value)
    {
        if (value is decimal decimalValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(decimalValue));
        }

        if (value is byte byteValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(byteValue));
        }

        if (value is sbyte sbyteValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(sbyteValue));
        }

        if (value is short shortValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(shortValue));
        }

        if (value is ushort ushortValue)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(ushortValue));
        }

        return null;
    }

    // Why a second plan pass: DecideMethodTransform only sets UsesDelegation for
    // async/iterator/closure bodies. An ordinary added method JIT-compiles in the
    // shim assembly, so inaccessible compiled members must take the same accessor
    // rewrite or be Skipped — Success plus a raw FieldAccessException is the FB bug.
    private static MethodTransformDecision DecideAddedMethodAccessors(
        IMethodSymbol methodSymbol,
        INamedTypeSymbol typeSymbol,
        SyntaxNode methodBodyNode,
        SemanticModel semanticModel,
        MethodTransformDecision current)
    {
        if (current.UsesDelegation)
        {
            return MethodTransformDecision.AddedMethod(true);
        }

        if (!SubtreeHasInaccessibleMemberAccess(semanticModel, new[] { methodBodyNode }))
        {
            return MethodTransformDecision.AddedMethod(false);
        }

        if (!AccessorEligibility.TryBuildPlan(
                semanticModel,
                methodSymbol,
                typeSymbol,
                methodBodyNode,
                out AccessorPlan feasibilityPlan,
                out string accessorRejectReason))
        {
            return MethodTransformDecision.Skip(
                AddedMethodSkipReasons.InaccessibleAccessNoRewrite
                + " Accessor rewrite unavailable: "
                + accessorRejectReason
                + " Run 'uloop compile'.");
        }

        bool usesDelegation = feasibilityPlan.Entries.Count > 0;
        return MethodTransformDecision.AddedMethod(usesDelegation);
    }

    private static string EvaluateAddedMethodSkipReason(
        IMethodSymbol methodSymbol,
        MethodDeclarationSyntax methodDeclaration)
    {
        if (methodSymbol.IsAbstract || methodSymbol.IsVirtual || methodSymbol.IsOverride)
        {
            return AddedMethodSkipReasons.VirtualOrAbstract;
        }

        bool hasTypeParameters = methodDeclaration != null && methodDeclaration.TypeParameterList != null;
        if (methodSymbol.IsGenericMethod || hasTypeParameters)
        {
            return AddedMethodSkipReasons.Generic;
        }

        return null;
    }

    private static void AppendUnityMessageWarningIfNeeded(
        INamedTypeSymbol typeSymbol,
        IMethodSymbol methodSymbol,
        List<string> declarationDriftWarnings)
    {
        if (!IsUnityEngineMonoBehaviourDerived(typeSymbol)
            || !UnityMessageNames.Contains(methodSymbol.Name))
        {
            return;
        }

        declarationDriftWarnings.Add(
            string.Format(
                CultureInfo.InvariantCulture,
                UnityMessageNames.AddedMessageWarningFormat,
                methodSymbol.Name,
                typeSymbol.ToDisplayString()));
    }

    private static string BuildMethodKeyFromSymbol(IMethodSymbol methodSymbol)
    {
        string[] parameterTypeFullNames = methodSymbol.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        return BuildMethodKey(
            CecilTypeNames.ToMetadataName(methodSymbol.ContainingType),
            methodSymbol.Name,
            parameterTypeFullNames,
            methodSymbol.Arity);
    }

    private static MethodDeclarationSyntax RewriteMethodBody(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol targetType,
        SemanticModel semanticModel,
        AccessorPlan accessorPlan,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        // Why a single rewriter: rewriting the tree invalidates SemanticModel for new nodes.
        // Qualify + accessor rewrite both classify symbols on the original tree in one Visit pass.
        ShimBodyRewriter rewriter = new ShimBodyRewriter(
            semanticModel,
            targetType,
            accessorPlan,
            addedMethodCatalog,
            addedFieldCatalog);
        MethodDeclarationSyntax rewritten = (MethodDeclarationSyntax)rewriter.Visit(methodDeclaration);
        return ShimMethodFactory.ToShimMethod(rewritten, methodSymbol);
    }

    // Keep in sync with HotReloadPatcher.FormatMethodKeyParts.
    // Why FormatMethodKeyParts shape: Methods[].Method must use one label for every Kind.
    // Roslyn FullyQualifiedFormat (global::, type arguments) was the Skipped-only outlier.
    private static string FormatMethodLabel(IMethodSymbol methodSymbol)
    {
        string typeMetadataName =
            CecilTypeNames.ToMetadataName(methodSymbol.ContainingType).Replace('/', '+');
        string[] parameterTypeFullNames = methodSymbol.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        StringBuilder builder = new StringBuilder();
        builder.Append(typeMetadataName);
        builder.Append('.');
        builder.Append(methodSymbol.Name);
        if (methodSymbol.Arity > 0)
        {
            builder.Append('`');
            builder.Append(methodSymbol.Arity.ToString(CultureInfo.InvariantCulture));
        }

        builder.Append('(');
        for (int index = 0; index < parameterTypeFullNames.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(parameterTypeFullNames[index].Replace('/', '+'));
        }

        builder.Append(')');
        return builder.ToString();
    }

    private static List<UsingDirectiveSyntax> CollectUsingsForType(
        CompilationUnitSyntax root,
        TypeDeclarationSyntax typeDeclaration,
        List<UsingDirectiveSyntax> assemblyGlobalUsings)
    {
        List<UsingDirectiveSyntax> usings = new List<UsingDirectiveSyntax>();
        foreach (UsingDirectiveSyntax usingDirective in root.Usings)
        {
            usings.Add(usingDirective.WithoutTrivia());
        }

        for (SyntaxNode node = typeDeclaration.Parent; node != null; node = node.Parent)
        {
            if (node is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                foreach (UsingDirectiveSyntax usingDirective in namespaceDeclaration.Usings)
                {
                    usings.Add(usingDirective.WithoutTrivia());
                }
            }
        }

        foreach (UsingDirectiveSyntax assemblyUsing in assemblyGlobalUsings)
        {
            if (!ShouldSkipAssemblyUsing(usings, assemblyUsing))
            {
                usings.Add(assemblyUsing);
            }
        }

        return usings;
    }

    // Why skip same alias name regardless of target: C# lets a namespace-scoped alias shadow a
    // global one. Flattening both into the shim's single namespace is CS1537.
    private static bool ShouldSkipAssemblyUsing(
        List<UsingDirectiveSyntax> existingUsings,
        UsingDirectiveSyntax assemblyUsing)
    {
        if (ContainsEquivalentUsing(existingUsings, assemblyUsing))
        {
            return true;
        }

        if (assemblyUsing.Alias == null)
        {
            return false;
        }

        string aliasName = assemblyUsing.Alias.Name.ToString();
        foreach (UsingDirectiveSyntax existing in existingUsings)
        {
            if (existing.Alias != null && existing.Alias.Name.ToString() == aliasName)
            {
                return true;
            }
        }

        return false;
    }

    // Why skip SourcePath: the edited file's usings already come from the in-memory tree.
    // Reading the on-disk copy would pick up the pre-edit source.
    private static List<UsingDirectiveSyntax> CollectAssemblyGlobalUsings(
        WorkerInput input,
        CSharpParseOptions parseOptions)
    {
        List<UsingDirectiveSyntax> collected = new List<UsingDirectiveSyntax>();
        foreach (string assemblySourcePath in input.AssemblySourcePaths)
        {
            if (string.IsNullOrEmpty(assemblySourcePath)
                || PathsReferToSameSourceFile(assemblySourcePath, input.SourcePath)
                || !File.Exists(assemblySourcePath))
            {
                continue;
            }

            string text = File.ReadAllText(
                assemblySourcePath,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (!FileContainsGlobalUsingLine(text))
            {
                continue;
            }

            AppendGlobalUsingsFromParsedText(collected, text, parseOptions, assemblySourcePath);
        }

        return collected;
    }

    private static void AppendGlobalUsingsFromParsedText(
        List<UsingDirectiveSyntax> collected,
        string text,
        CSharpParseOptions parseOptions,
        string assemblySourcePath)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            SourceText.From(text, Encoding.UTF8),
            parseOptions,
            path: assemblySourcePath);
        CompilationUnitSyntax unit = tree.GetCompilationUnitRoot();
        foreach (UsingDirectiveSyntax usingDirective in unit.Usings)
        {
            if (!usingDirective.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
            {
                continue;
            }

            UsingDirectiveSyntax asOrdinary = usingDirective
                .WithGlobalKeyword(default)
                .WithoutTrivia();
            if (!ContainsEquivalentUsing(collected, asOrdinary))
            {
                collected.Add(asOrdinary);
            }
        }
    }

    private static bool FileContainsGlobalUsingLine(string text)
    {
        using StringReader reader = new StringReader(text);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (LineStartsWithGlobalUsing(line))
            {
                return true;
            }
        }

        return false;
    }

    // Why same-line tokens (not ParseText): the prefilter must stay cheaper than parsing every
    // assembly file. Extra whitespace between global and using is allowed; a comment or line
    // break between those tokens is out of scope for this filter.
    private static bool LineStartsWithGlobalUsing(string line)
    {
        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith("global", StringComparison.Ordinal) || trimmed.Length <= 6)
        {
            return false;
        }

        char afterGlobal = trimmed[6];
        if (afterGlobal != ' ' && afterGlobal != '\t')
        {
            return false;
        }

        return trimmed.Substring(6).TrimStart().StartsWith("using", StringComparison.Ordinal);
    }

    private static bool PathsReferToSameSourceFile(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return false;
        }

        string normalizedLeft = Path.GetFullPath(left);
        string normalizedRight = Path.GetFullPath(right);
        StringComparison comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(normalizedLeft, normalizedRight, comparison);
    }

    private static bool ContainsEquivalentUsing(
        List<UsingDirectiveSyntax> usings,
        UsingDirectiveSyntax candidate)
    {
        foreach (UsingDirectiveSyntax existing in usings)
        {
            if (UsingDirectivesMatch(existing, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UsingDirectivesMatch(UsingDirectiveSyntax left, UsingDirectiveSyntax right)
    {
        bool leftStatic = left.StaticKeyword.IsKind(SyntaxKind.StaticKeyword);
        bool rightStatic = right.StaticKeyword.IsKind(SyntaxKind.StaticKeyword);
        if (leftStatic != rightStatic)
        {
            return false;
        }

        string leftAlias = left.Alias == null ? string.Empty : left.Alias.Name.ToString();
        string rightAlias = right.Alias == null ? string.Empty : right.Alias.Name.ToString();
        if (leftAlias != rightAlias)
        {
            return false;
        }

        string leftName = left.Name == null ? string.Empty : left.Name.ToString();
        string rightName = right.Name == null ? string.Empty : right.Name.ToString();
        return leftName == rightName;
    }
}
