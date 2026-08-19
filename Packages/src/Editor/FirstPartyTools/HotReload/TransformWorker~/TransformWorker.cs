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
            TypeEmitPlanner.QueueAllTypeEmitStates(
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
            (shimTypeCounter, globalShimMethodCounter) = PropertyGetterEmitter.EmitPropertyGettersForType(
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
            string[] calledAddedMethodKeys = AddedCallSiteGuard.CollectCalledAddedMethodKeys(
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
