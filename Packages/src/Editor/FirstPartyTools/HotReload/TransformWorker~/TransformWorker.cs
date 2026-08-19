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
        List<string> declarationDriftWarnings = ConstDriftCollector.CollectConstDriftWarnings(
            root,
            semanticModel,
            targetTypesAssemblySymbol);
        // Why here: a compiled property/event can disappear or change kind with no
        // touched body, so the generic outside-body warning would bury the name.
        CompiledMemberKindChangeWarnings.AppendCompiledPropertyOrEventKindChangeWarnings(
            root,
            semanticModel,
            targetTypesAssemblySymbol,
            declarationDriftWarnings);

        BaselineSnapshotState baseline = BaselineSnapshotBuilder.BuildBaselineSnapshotState(input, parseOptions, plainRoot);

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
            OutsideMethodBodyDriftChecker.AppendOutsideMethodBodyDriftWarningIfNeeded(
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

            string typeMetadataNameFromSyntax = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);

            // Property setters/init and all indexer accessors with bodies stay Skipped.
            // Property getters are patched below (not reported here).
            (Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
                Dictionary<string, IndexerDeclarationSyntax> snapshotIndexerMap,
                Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap,
                Dictionary<string, IndexerDeclarationSyntax> plainCurrentIndexerMap) =
                baseline.GetAccessorBaselineMaps();
            UnsupportedMemberSkipCollector.AppendExplicitAccessorSkips(
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
            UnsupportedMemberSkipCollector.AppendUnsupportedMemberKindSkips(
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
            WorkerSyntaxIndex.BuildSyntaxFieldMapOrNull(baseline.SnapshotRoot);
        Dictionary<string, VariableDeclaratorSyntax> currentFieldMap =
            WorkerSyntaxIndex.BuildSyntaxFieldMapOrNull(plainRoot);
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

        string propertyKey = WorkerSyntaxIndex.BuildSyntaxPropertyKey(typeMetadataNameFromSyntax, propertyDeclaration);
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

        string addedPropertyKey = WorkerSyntaxIndex.BuildSyntaxPropertyKey(typeMetadataNameFromSyntax, propertyDeclaration);
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

        bool closureInaccessible = InaccessibleAccessScanner.SubtreeHasInaccessibleMemberAccess(
            semanticModel,
            FindClosureBodies(bodyNode));
        bool asyncIteratorInaccessible = IsAsyncOrIterator(methodDeclaration, bodyNode)
            && InaccessibleAccessScanner.SubtreeHasInaccessibleMemberAccess(semanticModel, new[] { bodyNode });

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

        string syntaxMethodKey = WorkerSyntaxIndex.BuildSyntaxMethodKey(
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
                    WorkerSyntaxIndex.BuildSyntaxMethodKey(typeState.TypeMetadataNameFromSyntax, methodDeclaration),
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

        return targetTypesAssemblySymbol.GetTypeByMetadataName(ConstDriftCollector.ToReflectionMetadataName(sourceType));
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
        string syntaxKey = WorkerSyntaxIndex.BuildSyntaxFieldKey(typeState.TypeMetadataNameFromSyntax, fieldSymbol.Name);
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

        if (!InaccessibleAccessScanner.SubtreeHasInaccessibleMemberAccess(semanticModel, new[] { methodBodyNode }))
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
