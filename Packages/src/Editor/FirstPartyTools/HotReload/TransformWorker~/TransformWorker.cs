// Hot-reload transform worker: parse + semantic analysis of one edited C# file, emit static
// shim method sources (no Prefix wrappers) plus a per-method manifest / skip list.
// Runs out-of-process on the Unity-bundled .NET host against the Unity-bundled Roslyn.
// Generated shims mirror user method signatures verbatim; repo style rules apply to
// hand-written code only.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
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

    private static readonly SymbolDisplayFormat FullyQualifiedTypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat;

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
        List<string> parseErrors = new List<string>();

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

        string sourceText;
        try
        {
            sourceText = File.ReadAllText(input.SourcePath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception exception)
        {
            return new WorkerOutput
            {
                ShimSource = string.Empty,
                Entries = Array.Empty<WorkerEntry>(),
                Skipped = Array.Empty<WorkerSkipped>(),
                ParseErrors = new[] { "Failed to read sourcePath: " + exception.Message }
            };
        }

        CSharpParseOptions parseOptions = new CSharpParseOptions(
            languageVersion: LanguageVersion.Latest,
            preprocessorSymbols: input.Defines);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(sourceText, Encoding.UTF8),
            parseOptions,
            path: input.SourcePath);

        ImmutableArray<Diagnostic> parseDiagnostics = syntaxTree.GetDiagnostics().ToImmutableArray();
        foreach (Diagnostic diagnostic in parseDiagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                parseErrors.Add(diagnostic.ToString());
            }
        }

        // Why capture plainRoot before annotate: StatementSyntax annotations make
        // SyntaxFactory.AreEquivalent(topLevel:false) return false for some method shapes
        // (long single return / unchecked multi-statement / switch) even when the source text
        // is identical. Baseline comparison must use unannotated nodes on both sides.
        // Why annotate before CSharpCompilation.Create: annotating after GetSemanticModel
        // detaches nodes from the bound tree and ShimBodyRewriter's GetSymbolInfo throws
        // "Syntax node is not within syntax tree". Binding the SemanticModel to the annotated
        // tree keeps rewriter lookups valid while uloop-line annotations ride through to Emit.
        CompilationUnitSyntax plainRoot = syntaxTree.GetCompilationUnitRoot();
        CompilationUnitSyntax annotatedRoot = AnnotateOriginalSourceLines(plainRoot);
        syntaxTree = syntaxTree.WithRootAndOptions(annotatedRoot, syntaxTree.Options);

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

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "UloopHotReloadTransformWorkerCompilation",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
        CompilationUnitSyntax root = syntaxTree.GetCompilationUnitRoot();

        // The drift comparison must see private and internal consts in the compiled target
        // assembly, which the default MetadataImportOptions (Public) hides. Widening the main
        // compilation would also widen what every classification query can bind to, so the
        // wider import is confined to a throwaway compilation used only for this lookup.
        IAssemblySymbol targetTypesAssemblySymbol = null;
        if (targetTypesReference != null)
        {
            CSharpCompilation driftCompilation = compilation.WithOptions(
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithMetadataImportOptions(MetadataImportOptions.All));
            targetTypesAssemblySymbol =
                driftCompilation.GetAssemblyOrModuleSymbol(targetTypesReference) as IAssemblySymbol;
        }
        List<string> declarationDriftWarnings = CollectConstDriftWarnings(
            root,
            semanticModel,
            targetTypesAssemblySymbol);

        // Syntax-key maps for edited-method detection. Distinct from BuildMethodKey (Cecil names):
        // same-file old/new comparison only needs syntax keys to stay consistent with each other.
        bool hasBaseline = false;
        bool baselineDisabledByDuplicateKeys = false;
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap = null;
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap = null;
        Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap = null;
        Dictionary<string, IndexerDeclarationSyntax> snapshotIndexerMap = null;
        Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap = null;
        Dictionary<string, IndexerDeclarationSyntax> plainCurrentIndexerMap = null;
        CompilationUnitSyntax baselineSnapshotRoot = null;
        // Null disables comparison; empty string is a real (empty) baseline text.
        if (input.SnapshotSource != null)
        {
            baselineSnapshotRoot = CSharpSyntaxTree.ParseText(
                    SourceText.From(input.SnapshotSource, Encoding.UTF8),
                    parseOptions)
                .GetCompilationUnitRoot();
            Dictionary<string, MethodDeclarationSyntax> snapMethods = BuildSyntaxMethodMapOrNull(baselineSnapshotRoot);
            // Why plainRoot: annotated current nodes break AreEquivalent for some shapes (see plainRoot above).
            Dictionary<string, MethodDeclarationSyntax> currentMethods = BuildSyntaxMethodMapOrNull(plainRoot);
            if (snapMethods != null && currentMethods != null)
            {
                // Why both maps: a duplicate key on either side makes AreEquivalent matching
                // ambiguous, so fail closed to no-baseline (patch all) instead of guessing.
                hasBaseline = true;
                snapshotMethodMap = snapMethods;
                plainCurrentMethodMap = currentMethods;
                // Why null is kept as-is: a colliding property/indexer key only disables accessor
                // gating for this file; method-level baseline matching still applies.
                snapshotPropertyMap = BuildSyntaxPropertyMapOrNull(baselineSnapshotRoot);
                snapshotIndexerMap = BuildSyntaxIndexerMapOrNull(baselineSnapshotRoot);
                plainCurrentPropertyMap = BuildSyntaxPropertyMapOrNull(plainRoot);
                plainCurrentIndexerMap = BuildSyntaxIndexerMapOrNull(plainRoot);
            }
            else
            {
                // Why surface: previously a colliding key silently disabled baseline and patched all.
                baselineDisabledByDuplicateKeys = true;
            }
        }

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
            AppendExplicitAccessorSkips(
                typeDeclaration,
                typeMetadataNameFromSyntax,
                semanticModel,
                skipped,
                hasBaseline ? snapshotPropertyMap : null,
                hasBaseline ? snapshotIndexerMap : null,
                hasBaseline ? plainCurrentPropertyMap : null,
                hasBaseline ? plainCurrentIndexerMap : null);

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
                hasBaseline,
                snapshotMethodMap,
                plainCurrentMethodMap,
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

        if (hasBaseline)
        {
            CollectRemovedMethods(
                snapshotMethodMap,
                plainCurrentMethodMap,
                addedMethodCatalog,
                removedMembers);
            CollectRemovedMethodSignaturesForDeletedNames(
                typeEmitStates,
                semanticModel,
                targetTypesAssemblySymbol,
                removedMembers,
                removedMethodSignatures);
            Dictionary<string, VariableDeclaratorSyntax> snapshotFieldMap =
                BuildSyntaxFieldMapOrNull(baselineSnapshotRoot);
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

        SkipBodiesThatCannotUseAddedMethods(
            typeEmitStates,
            semanticModel,
            addedMethodCatalog,
            addedFieldCatalog,
            skipped);

        if (hasBaseline && baselineSnapshotRoot != null)
        {
            // Why after classification: added/removed method and field declarations must be
            // stripped before AreEquivalent, or every addition would fire "not applied".
            AppendOutsideMethodBodyDriftWarningIfNeeded(
                baselineSnapshotRoot,
                plainRoot,
                Path.GetFileName(input.SourcePath),
                declarationDriftWarnings,
                addedMethodCatalog,
                addedFieldCatalog);
        }

        foreach (TypeEmitState typeState in typeEmitStates)
        {
            EmitQueuedMethods(
                typeState,
                semanticModel,
                addedMethodCatalog,
                addedFieldCatalog,
                entries);
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
                        hasBaseline,
                        snapshotPropertyMap,
                        plainCurrentPropertyMap,
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
        }

        bool hasAccessorDelegates = false;
        foreach (ShimTypeBuilder shimType in shimTypes)
        {
            if (shimType.AccessorPlan.Entries.Count > 0)
            {
                hasAccessorDelegates = true;
                break;
            }
        }

        string shimSource = ShimSourceEmitter.Emit(root, shimTypes, input.ProjectRelativePath);
        return new WorkerOutput
        {
            ShimSource = shimSource,
            Entries = entries.ToArray(),
            Skipped = skipped.ToArray(),
            DeclarationDriftWarnings = declarationDriftWarnings.ToArray(),
            ParseErrors = parseErrors.ToArray(),
            UnchangedMethods = unchangedMethods.ToArray(),
            BaselineDisabledByDuplicateKeys = baselineDisabledByDuplicateKeys,
            RemovedMembers = removedMembers.ToArray(),
            RemovedMethodSignatures = removedMethodSignatures.ToArray(),
            HasAccessorDelegates = hasAccessorDelegates,
            HasAddedFieldRewrites = addedFieldCatalog.HasStoreRewrites
        };
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

    private static CompilationUnitSyntax AnnotateOriginalSourceLines(CompilationUnitSyntax root)
    {
        List<SyntaxNode> nodesToAnnotate = new List<SyntaxNode>();
        nodesToAnnotate.AddRange(root.DescendantNodes().OfType<MethodDeclarationSyntax>());
        nodesToAnnotate.AddRange(root.DescendantNodes().OfType<StatementSyntax>());
        // Why property/accessor arrows: expression-bodied getters are rewritten into synthetic
        // MethodDeclarations that would otherwise carry no #line annotations into the shim.
        foreach (PropertyDeclarationSyntax propertyDeclaration in root.DescendantNodes()
            .OfType<PropertyDeclarationSyntax>())
        {
            if (propertyDeclaration.ExpressionBody != null)
            {
                nodesToAnnotate.Add(propertyDeclaration.ExpressionBody);
            }
        }

        foreach (AccessorDeclarationSyntax accessor in root.DescendantNodes()
            .OfType<AccessorDeclarationSyntax>())
        {
            if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration) && accessor.ExpressionBody != null)
            {
                nodesToAnnotate.Add(accessor.ExpressionBody);
            }
        }

        if (nodesToAnnotate.Count == 0)
        {
            return root;
        }

        // Why rewritten (not original): ReplaceNodes applies nested replacements first; basing the
        // parent annotation on original would drop statement annotations already applied inside.
        return root.ReplaceNodes(
            nodesToAnnotate,
            (original, rewritten) =>
            {
                int line = ResolveUloopLineAnnotationLine(original);
                return rewritten.WithAdditionalAnnotations(
                    new SyntaxAnnotation(
                        UloopLineAnnotationKind,
                        line.ToString(CultureInfo.InvariantCulture)));
            });
    }

    private static int ResolveUloopLineAnnotationLine(SyntaxNode node)
    {
        if (node is MethodDeclarationSyntax methodDeclaration && methodDeclaration.ExpressionBody != null)
        {
            // Why arrow expression (not declaration start): NormalizeWhitespace collapses the
            // method to one line, so mapping to the arrow expression's original start is the only
            // location that still matches the user's intent for expression-bodied methods.
            return methodDeclaration.ExpressionBody.Expression.GetLocation()
                .GetLineSpan().StartLinePosition.Line + 1;
        }

        if (node is ArrowExpressionClauseSyntax arrowExpressionClause)
        {
            return arrowExpressionClause.Expression.GetLocation()
                .GetLineSpan().StartLinePosition.Line + 1;
        }

        return node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }

    /// <summary>
    /// Detects const declarations (including enum members) in the edited source whose values
    /// differ from the compiled target assembly. C# inlines const values at compile time and
    /// shims compile against the already-compiled assembly, so such edits silently keep the old
    /// value at runtime; each drift becomes a response warning instead of a silent no-op.
    /// </summary>
    private static List<string> CollectConstDriftWarnings(
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

    private const string ExplicitAccessorSkipReason =
        "Property setter, init, or indexer accessors are out of scope for v1; "
        + "run 'uloop compile' to apply accessor edits.";

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
                Array.Empty<string>());
        StripHandledMemberDeclarationsRewriter stripCurrent =
            new StripHandledMemberDeclarationsRewriter(
                currentKeys,
                addedMethodCatalog.AddedTypeSyntaxKeys);
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

        public StripHandledMemberDeclarationsRewriter(
            IReadOnlyCollection<string> syntaxKeysToStrip,
            IReadOnlyCollection<string> typeSyntaxKeysToStrip)
        {
            _syntaxKeysToStrip = new HashSet<string>(syntaxKeysToStrip, StringComparer.Ordinal);
            _typeSyntaxKeysToStrip = new HashSet<string>(
                typeSyntaxKeysToStrip ?? Array.Empty<string>(),
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
        Dictionary<string, IndexerDeclarationSyntax> plainCurrentIndexerMap)
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
                    skipped);
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
                    skipped);
            }
        }
    }

    private static void AppendExplicitAccessorSkipsForProperty(
        BasePropertyDeclarationSyntax propertyDeclaration,
        IPropertySymbol propertySymbol,
        List<WorkerSkipped> skipped)
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

        if (hasBaseline
            && snapshotPropertyMap != null
            && plainCurrentPropertyMap != null)
        {
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
                return (currentShimType, shimTypeCounter, globalShimMethodCounter);
            }
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
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        string addedCallSiteSkip = EvaluateAddedCallSiteSkipReason(
            getterBodyNode,
            semanticModel,
            addedMethodCatalog,
            addedFieldCatalog);
        if (addedCallSiteSkip != null)
        {
            skipped.Add(new WorkerSkipped
            {
                Method = FormatMethodLabel(getterSymbol),
                Reason = addedCallSiteSkip
            });
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

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
                out AccessorPlan disposablePlan,
                out string accessorRejectReason))
        {
            return MethodTransformDecision.Skip(
                v1Reason + " Accessor rewrite unavailable: " + accessorRejectReason);
        }

        // Safety net: detection said "needs accessors" but eligibility found nothing to rewrite
        // (e.g. local-function-only async body). Transplant is correct — the body is unchanged.
        if (disposablePlan.Entries.Count == 0)
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
            return "Generic methods and methods inside generic types cannot be safely patched with Harmony.";
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
            if (assignment.Parent is InitializerExpressionSyntax)
            {
                // Initializer assignments are always writes (including ImplicitElementAccess indexers).
                ISymbol initializerSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
                if (initializerSymbol is IPropertySymbol initializerProperty)
                {
                    return AccessibilityRules.IsInaccessibleAccessor(initializerProperty.SetMethod);
                }

                return initializerSymbol != null
                    && AccessibilityRules.IsInaccessibleFromExternalAssembly(initializerSymbol);
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

            return leftSymbol != null
                && AccessibilityRules.IsInaccessibleFromExternalAssembly(leftSymbol);
        }

        if (node is PostfixUnaryExpressionSyntax postfix
            && (postfix.IsKind(SyntaxKind.PostIncrementExpression)
                || postfix.IsKind(SyntaxKind.PostDecrementExpression)))
        {
            return IsInaccessibleIncrementOperand(semanticModel, postfix.Operand);
        }

        if (node is PrefixUnaryExpressionSyntax prefix
            && (prefix.IsKind(SyntaxKind.PreIncrementExpression)
                || prefix.IsKind(SyntaxKind.PreDecrementExpression)))
        {
            return IsInaccessibleIncrementOperand(semanticModel, prefix.Operand);
        }

        if (node is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax)
        {
            ISymbol ctorSymbol = semanticModel.GetSymbolInfo(node).Symbol;
            return ctorSymbol != null
                && AccessibilityRules.IsInaccessibleFromExternalAssembly(ctorSymbol);
        }

        if (node is InvocationExpressionSyntax invocation)
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

        if (node is ElementAccessExpressionSyntax elementAccess)
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

            return symbol != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol);
        }

        if (node is MemberBindingExpressionSyntax memberBinding)
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
                && AccessibilityRules.IsInaccessibleFromExternalAssembly(bound);
        }

        if (node is IdentifierNameSyntax or GenericNameSyntax)
        {
            SimpleNameSyntax name = (SimpleNameSyntax)node;
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

            return symbol != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol);
        }

        if (node is MemberAccessExpressionSyntax memberAccess)
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

            return symbol != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol);
        }

        return false;
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

        return symbol != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol);
    }

    internal static class NameofRules
    {
        // Why text-only: nameof is a language keyword with a null symbol; a user-defined method
        // literally named "nameof" would also match, but that pathological case is ignored in practice.
        public static bool IsNameofInvocation(InvocationExpressionSyntax invocation)
        {
            return invocation.Expression is IdentifierNameSyntax identifier
                && identifier.Identifier.ValueText == "nameof";
        }

        public static bool IsInsideNameofArgument(SyntaxNode node)
        {
            for (SyntaxNode current = node; current != null; current = current.Parent)
            {
                if (current is ArgumentSyntax
                    && current.Parent is ArgumentListSyntax argumentList
                    && argumentList.Parent is InvocationExpressionSyntax invocation
                    && IsNameofInvocation(invocation))
                {
                    return true;
                }
            }

            return false;
        }
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
            IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);
            if (methodSymbol == null)
            {
                continue;
            }

            string[] parameterTypeFullNames = methodSymbol.Parameters
                .Select(CecilTypeNames.ToParameterTypeFullName)
                .ToArray();
            string methodKey = BuildMethodKey(
                CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
                methodSymbol.Name,
                parameterTypeFullNames,
                methodSymbol.Arity);
            // Why skip explicit-interface methods: compiled GetMembers(simpleName) does not
            // see them (metadata name is Interface.Method), so they would be misclassified as
            // Added and skip the unchanged/baseline path.
            bool isAddedMethod = false;
            bool replacesCompiledMethod = false;
            if (methodDeclaration.ExplicitInterfaceSpecifier == null)
            {
                CompiledMethodMatch compiledMatch = MatchCompiledOrdinaryMethod(compiledType, methodSymbol);
                isAddedMethod = compiledMatch != CompiledMethodMatch.Matched;
                replacesCompiledMethod = compiledMatch == CompiledMethodMatch.ReturnTypeChanged;
            }

            if (isAddedMethod)
            {
                addedMethodCatalog.MarkClassifiedAdded(methodKey);
                if (input.ExcludedAddedMethodKeys.Contains(methodKey))
                {
                    addedMethodCatalog.AddAddedSyntaxKey(
                        BuildSyntaxMethodKey(typeState.TypeMetadataNameFromSyntax, methodDeclaration));
                    continue;
                }
            }
            else if (input.ExcludedMethodKeys.Contains(methodKey))
            {
                continue;
            }

            string syntaxMethodKey = BuildSyntaxMethodKey(
                typeState.TypeMetadataNameFromSyntax,
                methodDeclaration);
            if (typeState.TypeSymbol.TypeKind == TypeKind.Interface)
            {
                if (!isAddedMethod && hasBaseline
                    && snapshotMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax snapshotDecl)
                    && plainCurrentMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax plainDecl)
                    && SyntaxFactory.AreEquivalent(snapshotDecl, plainDecl, topLevel: false))
                {
                    // Why not unchangedMethods: RevertUnchangedPatches Resolve/ReadAssembly is
                    // wasted for members Harmony will never patch. Stay inert.
                    continue;
                }

                skipped.Add(new WorkerSkipped
                {
                    Method = FormatMethodLabel(methodSymbol),
                    Reason = AddedMethodSkipReasons.InterfaceMember
                });
                if (isAddedMethod)
                {
                    addedMethodCatalog.AddAddedSyntaxKey(syntaxMethodKey);
                }

                continue;
            }

            if (!isAddedMethod && hasBaseline)
            {
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
                    continue;
                }
            }

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
                    addedMethodCatalog.AddAddedSyntaxKey(syntaxMethodKey);
                }

                continue;
            }

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
                addedMethodCatalog.AddAddedSyntaxKey(syntaxMethodKey);
                AppendUnityMessageWarningIfNeeded(
                    typeState.TypeSymbol,
                    methodSymbol,
                    declarationDriftWarnings);
            }
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
                    string skipReason = EvaluateAddedCallSiteSkipReason(
                        bodyNode,
                        semanticModel,
                        addedMethodCatalog,
                        addedFieldCatalog);
                    if (skipReason != null)
                    {
                        skipped.Add(new WorkerSkipped
                        {
                            Method = FormatMethodLabel(queued.MethodSymbol),
                            Reason = skipReason
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

    private static string EvaluateAddedCallSiteSkipReason(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        if (bodyNode == null)
        {
            return null;
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
                return AddedMethodSkipReasons.ConditionalAccess;
            }

            if (addedMethodCatalog.IsUnavailableAdded(calledKey))
            {
                return AddedMethodSkipReasons.UnavailableAddedCall;
            }
        }

        if (BodyReferencesAddedMethodGroup(bodyNode, semanticModel, addedMethodCatalog))
        {
            return AddedMethodSkipReasons.MethodGroupReference;
        }

        return EvaluateAddedFieldSkipReason(bodyNode, semanticModel, addedFieldCatalog);
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

        switch (typeSymbol.SpecialType)
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
                out AccessorPlan disposablePlan,
                out string accessorRejectReason))
        {
            return MethodTransformDecision.Skip(
                AddedMethodSkipReasons.InaccessibleAccessNoRewrite
                + " Accessor rewrite unavailable: "
                + accessorRejectReason);
        }

        bool usesDelegation = disposablePlan != null && disposablePlan.Entries.Count > 0;
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

    private static string FormatMethodLabel(IMethodSymbol methodSymbol)
    {
        return AccessorPlan.BuildMemberKey(methodSymbol);
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

internal static class AccessibilityRules
{
    public static bool IsInaccessibleFromExternalAssembly(ISymbol symbol)
    {
        if (symbol is ILocalSymbol
            || symbol is IParameterSymbol
            || symbol is IRangeVariableSymbol
            || symbol is ITypeParameterSymbol
            || symbol is INamespaceSymbol
            || symbol is ILabelSymbol
            || symbol is IDiscardSymbol)
        {
            return false;
        }

        // Local functions / lambdas are emitted into the shim assembly itself, so they have no
        // cross-assembly accessibility problem and must not be treated as accessor targets.
        if (symbol is IMethodSymbol methodKindSymbol
            && (methodKindSymbol.MethodKind == MethodKind.LocalFunction
                || methodKindSymbol.MethodKind == MethodKind.AnonymousFunction))
        {
            return false;
        }

        if (symbol is ITypeSymbol typeSymbol)
        {
            return HasInaccessibleAccessibility(typeSymbol.DeclaredAccessibility)
                || (typeSymbol.ContainingType != null
                    && IsInaccessibleFromExternalAssembly(typeSymbol.ContainingType));
        }

        if (symbol is IFieldSymbol
            || symbol is IPropertySymbol
            || symbol is IMethodSymbol
            || symbol is IEventSymbol)
        {
            if (HasInaccessibleAccessibility(symbol.DeclaredAccessibility))
            {
                return true;
            }

            // Recurse through nested containing types (same rule as the type-symbol branch).
            if (symbol.ContainingType != null
                && IsInaccessibleFromExternalAssembly(symbol.ContainingType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What: accessibility of a property get/set accessor method (not the property declaration).
    /// Missing accessors are treated as inaccessible so write-only/read-only misuse fails closed.
    /// </summary>
    public static bool IsInaccessibleAccessor(IMethodSymbol accessorMethod)
    {
        if (accessorMethod == null)
        {
            return true;
        }

        return IsInaccessibleFromExternalAssembly(accessorMethod);
    }

    private static bool HasInaccessibleAccessibility(Accessibility accessibility)
    {
        return accessibility == Accessibility.Private
            || accessibility == Accessibility.Internal
            || accessibility == Accessibility.Protected
            || accessibility == Accessibility.ProtectedAndInternal
            || accessibility == Accessibility.ProtectedOrInternal;
    }

    /// <summary>
    /// What: whether a type can appear in an accessor signature / as a type mention in a shim
    /// that will JIT outside Harmony skip-visibility.
    /// </summary>
    public static bool IsExternallyVisibleType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null || typeSymbol.TypeKind == TypeKind.Error)
        {
            return false;
        }

        if (typeSymbol is ITypeParameterSymbol)
        {
            return true;
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return IsExternallyVisibleType(arrayType.ElementType);
        }

        if (typeSymbol is IPointerTypeSymbol pointerType)
        {
            return IsExternallyVisibleType(pointerType.PointedAtType);
        }

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            if (IsInaccessibleFromExternalAssembly(namedType))
            {
                return false;
            }

            foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
            {
                if (!IsExternallyVisibleType(typeArgument))
                {
                    return false;
                }
            }

            return true;
        }

        return !IsInaccessibleFromExternalAssembly(typeSymbol);
    }
}

/// <summary>
/// What: per-shim-type registry of Harmony accessor delegates to emit and bind.
/// </summary>
internal sealed class AccessorPlan
{
    private readonly List<AccessorEntry> _entries = new List<AccessorEntry>();
    private readonly Dictionary<string, AccessorEntry> _byKey =
        new Dictionary<string, AccessorEntry>(StringComparer.Ordinal);

    public IReadOnlyList<AccessorEntry> Entries => _entries;

    public AccessorEntry GetOrAddField(IFieldSymbol fieldSymbol)
    {
        string key = "F:" + BuildMemberKey(fieldSymbol);
        if (_byKey.TryGetValue(key, out AccessorEntry existing))
        {
            return existing;
        }

        string fieldName = AllocateName("__F_" + SanitizeIdentifier(fieldSymbol.Name));
        AccessorEntry entry = AccessorEntry.ForField(fieldSymbol, fieldName, key);
        _byKey[key] = entry;
        _entries.Add(entry);
        return entry;
    }

    public AccessorEntry GetOrAddMethod(IMethodSymbol methodSymbol)
    {
        string key = "M:" + BuildMemberKey(methodSymbol);
        if (_byKey.TryGetValue(key, out AccessorEntry existing))
        {
            return existing;
        }

        string fieldName = AllocateName("__M_" + SanitizeIdentifier(methodSymbol.Name));
        AccessorEntry entry = AccessorEntry.ForMethod(methodSymbol, fieldName, key);
        _byKey[key] = entry;
        _entries.Add(entry);
        return entry;
    }

    public AccessorEntry GetOrAddPropertyGetter(IPropertySymbol propertySymbol)
    {
        string key = "PG:" + BuildMemberKey(propertySymbol);
        if (_byKey.TryGetValue(key, out AccessorEntry existing))
        {
            return existing;
        }

        string fieldName = AllocateName("__P_get_" + SanitizeIdentifier(propertySymbol.Name));
        AccessorEntry entry = AccessorEntry.ForPropertyGetter(propertySymbol, fieldName, key);
        _byKey[key] = entry;
        _entries.Add(entry);
        return entry;
    }

    public AccessorEntry GetOrAddPropertySetter(IPropertySymbol propertySymbol)
    {
        string key = "PS:" + BuildMemberKey(propertySymbol);
        if (_byKey.TryGetValue(key, out AccessorEntry existing))
        {
            return existing;
        }

        string fieldName = AllocateName("__P_set_" + SanitizeIdentifier(propertySymbol.Name));
        AccessorEntry entry = AccessorEntry.ForPropertySetter(propertySymbol, fieldName, key);
        _byKey[key] = entry;
        _entries.Add(entry);
        return entry;
    }

    private string AllocateName(string preferred)
    {
        if (!_entries.Any(entry => entry.DelegateFieldName == preferred))
        {
            return preferred;
        }

        int suffix = 2;
        while (_entries.Any(entry => entry.DelegateFieldName == preferred + suffix))
        {
            suffix++;
        }

        return preferred + suffix;
    }

    /// <summary>
    /// What: stable identity for a member across overloads and same-named members on different
    /// types — containing type FQ + name + (parameter type FQs).
    /// </summary>
    public static string BuildMemberKey(ISymbol symbol)
    {
        string typePart = symbol.ContainingType == null
            ? string.Empty
            : symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string name = symbol.Name;

        if (symbol is IMethodSymbol methodSymbol)
        {
            string args = string.Join(
                ",",
                methodSymbol.Parameters.Select(
                    parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            return typePart + "." + name + "(" + args + ")";
        }

        if (symbol is IPropertySymbol propertySymbol)
        {
            string args = string.Join(
                ",",
                propertySymbol.Parameters.Select(
                    parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            return typePart + "." + name + "(" + args + ")";
        }

        return typePart + "." + name + "()";
    }

    private static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "member";
        }

        StringBuilder builder = new StringBuilder(name.Length);
        foreach (char character in name)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('_');
            }
        }

        return builder.Length == 0 ? "member" : builder.ToString();
    }
}

internal enum AccessorKind
{
    FieldRef,
    MethodDelegate,
    PropertyGetter,
    PropertySetter
}

/// <summary>
/// What: one Harmony accessor delegate field plus the statements that bind it in __BindAccessors.
/// </summary>
internal sealed class AccessorEntry
{
    public AccessorKind Kind { get; private set; }

    public string RegistryKey { get; private set; }

    public string DelegateFieldName { get; private set; }

    public IFieldSymbol FieldSymbol { get; private set; }

    public IMethodSymbol MethodSymbol { get; private set; }

    public IPropertySymbol PropertySymbol { get; private set; }

    public static AccessorEntry ForField(
        IFieldSymbol fieldSymbol,
        string delegateFieldName,
        string registryKey)
    {
        return new AccessorEntry
        {
            Kind = AccessorKind.FieldRef,
            RegistryKey = registryKey,
            DelegateFieldName = delegateFieldName,
            FieldSymbol = fieldSymbol
        };
    }

    public static AccessorEntry ForMethod(
        IMethodSymbol methodSymbol,
        string delegateFieldName,
        string registryKey)
    {
        return new AccessorEntry
        {
            Kind = AccessorKind.MethodDelegate,
            RegistryKey = registryKey,
            DelegateFieldName = delegateFieldName,
            MethodSymbol = methodSymbol
        };
    }

    public static AccessorEntry ForPropertyGetter(
        IPropertySymbol propertySymbol,
        string delegateFieldName,
        string registryKey)
    {
        return new AccessorEntry
        {
            Kind = AccessorKind.PropertyGetter,
            RegistryKey = registryKey,
            DelegateFieldName = delegateFieldName,
            PropertySymbol = propertySymbol
        };
    }

    public static AccessorEntry ForPropertySetter(
        IPropertySymbol propertySymbol,
        string delegateFieldName,
        string registryKey)
    {
        return new AccessorEntry
        {
            Kind = AccessorKind.PropertySetter,
            RegistryKey = registryKey,
            DelegateFieldName = delegateFieldName,
            PropertySymbol = propertySymbol
        };
    }

    public bool TryGetVisibilityFailure(out string reason)
    {
        foreach (ITypeSymbol typeSymbol in EnumerateSignatureTypes())
        {
            if (!AccessibilityRules.IsExternallyVisibleType(typeSymbol))
            {
                reason = "accessor signature type is not visible from an external assembly: "
                    + typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return true;
            }
        }

        reason = null;
        return false;
    }

    public IEnumerable<ITypeSymbol> EnumerateSignatureTypes()
    {
        // Bind statements always emit typeof(ContainingType), including for static members.
        switch (Kind)
        {
            case AccessorKind.FieldRef:
                yield return FieldSymbol.ContainingType;
                yield return FieldSymbol.Type;
                yield break;
            case AccessorKind.MethodDelegate:
                yield return MethodSymbol.ContainingType;
                foreach (IParameterSymbol parameter in MethodSymbol.Parameters)
                {
                    yield return parameter.Type;
                }

                if (!MethodSymbol.ReturnsVoid)
                {
                    yield return MethodSymbol.ReturnType;
                }

                yield break;
            case AccessorKind.PropertyGetter:
            case AccessorKind.PropertySetter:
                yield return PropertySymbol.ContainingType;
                yield return PropertySymbol.Type;
                yield break;
        }
    }

    public FieldDeclarationSyntax EmitFieldDeclaration()
    {
        TypeSyntax fieldType = SyntaxFactory.ParseTypeName(BuildDelegateTypeDisplayString());
        return SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(fieldType)
                    .WithVariables(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(DelegateFieldName))))
            .WithModifiers(
                SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)));
    }

    public StatementSyntax EmitBindStatement()
    {
        string statement = Kind switch
        {
            AccessorKind.FieldRef => BuildFieldRefBindStatement(),
            AccessorKind.MethodDelegate => BuildMethodDelegateBindStatement(
                MethodSymbol.Name,
                MethodSymbol),
            AccessorKind.PropertyGetter => BuildMethodDelegateBindStatement(
                PropertySymbol.GetMethod.Name,
                PropertySymbol.GetMethod),
            AccessorKind.PropertySetter => BuildMethodDelegateBindStatement(
                PropertySymbol.SetMethod.Name,
                PropertySymbol.SetMethod),
            _ => throw new InvalidOperationException("Unknown accessor kind.")
        };

        return SyntaxFactory.ParseStatement(statement);
    }

    private string BuildDelegateTypeDisplayString()
    {
        switch (Kind)
        {
            case AccessorKind.FieldRef:
                if (FieldSymbol.IsStatic)
                {
                    return "global::HarmonyLib.AccessTools.FieldRef<"
                        + TypeDisplay(FieldSymbol.Type) + ">";
                }

                return "global::HarmonyLib.AccessTools.FieldRef<"
                    + TypeDisplay(FieldSymbol.ContainingType) + ", "
                    + TypeDisplay(FieldSymbol.Type) + ">";
            case AccessorKind.MethodDelegate:
                return BuildFuncOrActionType(MethodSymbol);
            case AccessorKind.PropertyGetter:
                return BuildFuncOrActionType(PropertySymbol.GetMethod);
            case AccessorKind.PropertySetter:
                return BuildFuncOrActionType(PropertySymbol.SetMethod);
            default:
                throw new InvalidOperationException("Unknown accessor kind.");
        }
    }

    private static string BuildFuncOrActionType(IMethodSymbol methodSymbol)
    {
        List<string> typeArguments = new List<string>();
        foreach (ITypeSymbol parameterType in EnumerateDelegateParameterTypes(methodSymbol))
        {
            typeArguments.Add(TypeDisplay(parameterType));
        }

        if (methodSymbol.ReturnsVoid)
        {
            if (typeArguments.Count == 0)
            {
                return "global::System.Action";
            }

            return "global::System.Action<" + string.Join(", ", typeArguments) + ">";
        }

        typeArguments.Add(TypeDisplay(methodSymbol.ReturnType));
        return "global::System.Func<" + string.Join(", ", typeArguments) + ">";
    }

    private string BuildFieldRefBindStatement()
    {
        if (FieldSymbol.IsStatic)
        {
            // Why FieldInfo: the Type+name StaticFieldRefAccess overloads return ref F,
            // not a FieldRef`1 that __BindAccessors can store.
            return DelegateFieldName + " = global::HarmonyLib.AccessTools.StaticFieldRefAccess<"
                + TypeDisplay(FieldSymbol.Type)
                + ">(global::HarmonyLib.AccessTools.Field(typeof("
                + TypeDisplay(FieldSymbol.ContainingType) + "), \""
                + EscapeStringLiteral(FieldSymbol.Name) + "\"));";
        }

        return DelegateFieldName + " = global::HarmonyLib.AccessTools.FieldRefAccess<"
            + TypeDisplay(FieldSymbol.ContainingType) + ", "
            + TypeDisplay(FieldSymbol.Type) + ">(\""
            + EscapeStringLiteral(FieldSymbol.Name) + "\");";
    }

    private string BuildMethodDelegateBindStatement(string metadataName, IMethodSymbol methodSymbol)
    {
        string declaringType = TypeDisplay(methodSymbol.ContainingType);
        string delegateType = BuildFuncOrActionType(methodSymbol);
        List<ITypeSymbol> delegateParameterTypes = EnumerateDelegateParameterTypes(methodSymbol);
        // AccessTools.Method matches metadata parameters only; the instance receiver is not one.
        IReadOnlyList<ITypeSymbol> methodLookupTypes = methodSymbol.IsStatic
            ? delegateParameterTypes
            : delegateParameterTypes.GetRange(1, delegateParameterTypes.Count - 1);
        string typeArray = BuildTypeArrayLiteral(methodLookupTypes);
        // virtualCall must stay true for virtual/override/abstract instance members so a derived
        // override is dispatched; non-virtual private/internal targets keep false (exact method).
        bool virtualCall = !methodSymbol.IsStatic
            && (methodSymbol.IsVirtual || methodSymbol.IsOverride || methodSymbol.IsAbstract);
        string virtualCallLiteral = virtualCall ? "true" : "false";
        // Why not null: Harmony then uses Func<> generic arguments including TResult as
        // DynamicMethod parameters, so Func<Host,T> becomes T(Host,T) and bind fails.
        string delegateArgs = BuildTypeArrayLiteral(delegateParameterTypes);
        return DelegateFieldName + " = global::HarmonyLib.AccessTools.MethodDelegate<"
            + delegateType + ">(global::HarmonyLib.AccessTools.Method(typeof("
            + declaringType + "), \"" + EscapeStringLiteral(metadataName) + "\", "
            + typeArray + "), null, " + virtualCallLiteral + ", " + delegateArgs + ");";
    }

    // Open-delegate parameter types: declaring type first for instance methods, then each
    // method parameter. Excludes Func TResult so Harmony arity matches the delegate Invoke.
    private static List<ITypeSymbol> EnumerateDelegateParameterTypes(IMethodSymbol methodSymbol)
    {
        List<ITypeSymbol> types = new List<ITypeSymbol>();
        if (!methodSymbol.IsStatic)
        {
            types.Add(methodSymbol.ContainingType);
        }

        foreach (IParameterSymbol parameter in methodSymbol.Parameters)
        {
            types.Add(parameter.Type);
        }

        return types;
    }

    private static string BuildTypeArrayLiteral(IReadOnlyList<ITypeSymbol> types)
    {
        if (types.Count == 0)
        {
            return "new global::System.Type[] { }";
        }

        IEnumerable<string> typeofs = types.Select(type => "typeof(" + TypeDisplay(type) + ")");
        return "new global::System.Type[] { " + string.Join(", ", typeofs) + " }";
    }

    private static string TypeDisplay(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static string EscapeStringLiteral(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

/// <summary>
/// What: decides whether an async/iterator/closure private-access skip can be rescued by
/// rewriting inaccessible member accesses into Harmony accessor delegates (conditions b/c).
/// </summary>
internal static class AccessorEligibility
{
    public static bool TryBuildPlan(
        SemanticModel semanticModel,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol typeSymbol,
        SyntaxNode bodyNode,
        out AccessorPlan plan,
        out string rejectReason)
    {
        plan = null;
        rejectReason = null;

        if (!AccessibilityRules.IsExternallyVisibleType(typeSymbol))
        {
            rejectReason = "containing type is not visible from an external assembly (condition c).";
            return false;
        }

        if (!AreMethodSignatureTypesVisible(methodSymbol, out rejectReason))
        {
            return false;
        }

        if (!AreBodyTypeUsagesVisible(semanticModel, bodyNode, out rejectReason))
        {
            return false;
        }

        AccessorPlan built = new AccessorPlan();
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (TransformWorkerProgram.NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            if (!TryRegisterInaccessibleAccess(semanticModel, node, built, out rejectReason))
            {
                if (rejectReason != null)
                {
                    return false;
                }
            }
        }

        foreach (AccessorEntry entry in built.Entries)
        {
            if (entry.TryGetVisibilityFailure(out rejectReason))
            {
                rejectReason = rejectReason + " (condition c).";
                return false;
            }
        }

        if (NeedsPropertyIncrementRewrite(semanticModel, bodyNode))
        {
            rejectReason =
                "inaccessible property increment/decrement has no accessor rewrite shape (condition b).";
            return false;
        }

        plan = built;
        rejectReason = null;
        return true;
    }

    private static bool AreMethodSignatureTypesVisible(IMethodSymbol methodSymbol, out string rejectReason)
    {
        if (!AccessibilityRules.IsExternallyVisibleType(methodSymbol.ReturnType))
        {
            rejectReason = "method return type is not visible from an external assembly (condition c).";
            return false;
        }

        foreach (IParameterSymbol parameter in methodSymbol.Parameters)
        {
            if (!AccessibilityRules.IsExternallyVisibleType(parameter.Type))
            {
                rejectReason =
                    "method parameter type is not visible from an external assembly (condition c).";
                return false;
            }
        }

        rejectReason = null;
        return true;
    }

    private static bool AreBodyTypeUsagesVisible(
        SemanticModel semanticModel,
        SyntaxNode bodyNode,
        out string rejectReason)
    {
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (TransformWorkerProgram.NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            ITypeSymbol typeSymbol = null;
            if (node is TypeSyntax typeSyntax)
            {
                typeSymbol = semanticModel.GetTypeInfo(typeSyntax).Type
                    ?? semanticModel.GetSymbolInfo(typeSyntax).Symbol as ITypeSymbol;
            }
            else if (node is VariableDeclarationSyntax variableDeclaration
                && variableDeclaration.Type.IsVar)
            {
                typeSymbol = semanticModel.GetTypeInfo(variableDeclaration.Type).Type;
            }
            else if (node is ImplicitObjectCreationExpressionSyntax implicitObjectCreation)
            {
                typeSymbol = semanticModel.GetTypeInfo(implicitObjectCreation).Type;
            }

            if (typeSymbol == null || typeSymbol.TypeKind == TypeKind.Error)
            {
                continue;
            }

            if (!AccessibilityRules.IsExternallyVisibleType(typeSymbol))
            {
                rejectReason = "body uses a type that is not visible from an external assembly: "
                    + typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    + " (condition c).";
                return false;
            }
        }

        rejectReason = null;
        return true;
    }

    private static bool NeedsPropertyIncrementRewrite(SemanticModel semanticModel, SyntaxNode bodyNode)
    {
        foreach (SyntaxNode node in bodyNode.DescendantNodes())
        {
            if (TransformWorkerProgram.NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            ExpressionSyntax operand = null;
            if (node is PostfixUnaryExpressionSyntax postfix
                && (postfix.IsKind(SyntaxKind.PostIncrementExpression)
                    || postfix.IsKind(SyntaxKind.PostDecrementExpression)))
            {
                operand = postfix.Operand;
            }
            else if (node is PrefixUnaryExpressionSyntax prefix
                && (prefix.IsKind(SyntaxKind.PreIncrementExpression)
                    || prefix.IsKind(SyntaxKind.PreDecrementExpression)))
            {
                operand = prefix.Operand;
            }

            if (operand == null)
            {
                continue;
            }

            ISymbol symbol = semanticModel.GetSymbolInfo(operand).Symbol;
            if (symbol is IPropertySymbol propertySymbol
                && (AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod)
                    || AccessibilityRules.IsInaccessibleAccessor(propertySymbol.SetMethod)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns false with null rejectReason when the node is not an inaccessible access site.
    /// Returns false with a reason when the site is inaccessible but not rewriteable (condition b).
    /// Returns true when the site was registered (or was already present).
    /// </summary>
    private static bool TryRegisterInaccessibleAccess(
        SemanticModel semanticModel,
        SyntaxNode node,
        AccessorPlan plan,
        out string rejectReason)
    {
        rejectReason = null;

        if (node is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax)
        {
            ISymbol ctorSymbol = semanticModel.GetSymbolInfo(node).Symbol;
            if (ctorSymbol != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(ctorSymbol))
            {
                rejectReason =
                    "inaccessible constructor call has no accessor rewrite shape (condition b).";
                return false;
            }

            return false;
        }

        if (node is AssignmentExpressionSyntax assignment
            && assignment.Parent is InitializerExpressionSyntax)
        {
            // Initializer assignments are always writes (including ImplicitElementAccess indexers).
            ISymbol initializerSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
            bool inaccessibleWrite = initializerSymbol is IPropertySymbol initializerProperty
                ? AccessibilityRules.IsInaccessibleAccessor(initializerProperty.SetMethod)
                : initializerSymbol != null
                    && AccessibilityRules.IsInaccessibleFromExternalAssembly(initializerSymbol);
            if (inaccessibleWrite)
            {
                rejectReason =
                    "inaccessible member assignment in an object/collection initializer has no "
                    + "accessor rewrite shape (condition b).";
                return false;
            }

            return false;
        }

        if (node is InvocationExpressionSyntax invocation)
        {
            return TryRegisterInvocation(semanticModel, invocation, plan, out rejectReason);
        }

        if (node is ElementAccessExpressionSyntax elementAccess)
        {
            // Assignment-left ElementAccess is owned by the assignment branch (write context).
            if (elementAccess.Parent is AssignmentExpressionSyntax parentElementAssignment
                && parentElementAssignment.Left == elementAccess)
            {
                return false;
            }

            ISymbol symbol = semanticModel.GetSymbolInfo(elementAccess).Symbol;
            if (symbol is IPropertySymbol indexer && indexer.IsIndexer)
            {
                // Standalone ElementAccess is a read — only the getter matters.
                if (AccessibilityRules.IsInaccessibleAccessor(indexer.GetMethod))
                {
                    rejectReason =
                        "inaccessible indexer access has no accessor rewrite shape (condition b).";
                    return false;
                }
            }
            else if (symbol != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol))
            {
                rejectReason = "inaccessible indexer access has no accessor rewrite shape (condition b).";
                return false;
            }

            return false;
        }

        if (node is MemberBindingExpressionSyntax memberBinding)
        {
            ISymbol bound = semanticModel.GetSymbolInfo(memberBinding.Name).Symbol;
            if (bound != null
                && bound is not INamespaceSymbol
                && bound is not ITypeSymbol
                && IsInaccessibleBindingTarget(bound))
            {
                rejectReason =
                    "inaccessible member access via conditional access has no rewrite shape (condition b).";
                return false;
            }

            return false;
        }

        if (node is AssignmentExpressionSyntax propertyOrFieldAssignment)
        {
            return TryRegisterAssignment(
                semanticModel,
                propertyOrFieldAssignment,
                plan,
                out rejectReason);
        }

        if (node is MemberAccessExpressionSyntax memberAccess)
        {
            // Method-group invocation targets are owned by the invocation branch; delegate-typed
            // field invokes (`this._cb()` / `other._cb()`) register as field reads here.
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

            return TryRegisterPropertyOrFieldRead(
                semanticModel.GetSymbolInfo(memberAccess).Symbol
                ?? semanticModel.GetSymbolInfo(memberAccess.Name).Symbol,
                plan,
                out rejectReason);
        }

        if (node is IdentifierNameSyntax or GenericNameSyntax)
        {
            SimpleNameSyntax name = (SimpleNameSyntax)node;
            if (IsNameHandledByParent(name))
            {
                return false;
            }

            if (name.Parent is AssignmentExpressionSyntax parentAssignment
                && parentAssignment.Left == name)
            {
                return false;
            }

            // Method-group invocation targets are owned by the invocation branch; delegate-typed
            // field invokes (`_cb()`) register as field reads here.
            if (name.Parent is InvocationExpressionSyntax parentInvocation
                && parentInvocation.Expression == name)
            {
                ISymbol invocationTarget = semanticModel.GetSymbolInfo(name).Symbol;
                if (invocationTarget is IMethodSymbol)
                {
                    return false;
                }
            }

            return TryRegisterPropertyOrFieldRead(
                semanticModel.GetSymbolInfo(name).Symbol,
                plan,
                out rejectReason);
        }

        return false;
    }

    private static bool IsInaccessibleBindingTarget(ISymbol bound)
    {
        // Member binding only appears in read/invoke contexts (x?.P = v is not valid C#).
        if (bound is IPropertySymbol propertySymbol)
        {
            return AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod);
        }

        return AccessibilityRules.IsInaccessibleFromExternalAssembly(bound);
    }

    private static bool TryRegisterAssignment(
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment,
        AccessorPlan plan,
        out string rejectReason)
    {
        rejectReason = null;
        ISymbol leftSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
        if (leftSymbol is IFieldSymbol fieldSymbol)
        {
            if (!AccessibilityRules.IsInaccessibleFromExternalAssembly(fieldSymbol))
            {
                return false;
            }

            plan.GetOrAddField(fieldSymbol);
            return true;
        }

        if (leftSymbol is IPropertySymbol propertySymbol)
        {
            return TryRegisterPropertyWrite(
                semanticModel,
                assignment,
                propertySymbol,
                plan,
                out rejectReason);
        }

        if (leftSymbol is IEventSymbol)
        {
            rejectReason = "inaccessible event add/remove is out of scope for accessor rewrite (condition b).";
            return false;
        }

        return false;
    }

    private static bool TryRegisterInvocation(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        AccessorPlan plan,
        out string rejectReason)
    {
        rejectReason = null;
        if (TransformWorkerProgram.NameofRules.IsNameofInvocation(invocation))
        {
            return false;
        }

        ISymbol symbol = semanticModel.GetSymbolInfo(invocation).Symbol;
        if (symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        if (methodSymbol.MethodKind != MethodKind.Ordinary)
        {
            return false;
        }

        if (!AccessibilityRules.IsInaccessibleFromExternalAssembly(methodSymbol))
        {
            return false;
        }

        if (methodSymbol.IsExtensionMethod)
        {
            rejectReason = "inaccessible extension method calls are not rewritten (condition b).";
            return false;
        }

        if (methodSymbol.IsGenericMethod)
        {
            rejectReason = "inaccessible generic method calls are not rewritten (condition b).";
            return false;
        }

        if (methodSymbol.ReturnsByRef || methodSymbol.ReturnsByRefReadonly)
        {
            rejectReason =
                "inaccessible methods that return by ref have no accessor rewrite shape (condition b).";
            return false;
        }

        foreach (IParameterSymbol parameter in methodSymbol.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                rejectReason =
                    "inaccessible method calls with ref/out/in parameters are not rewritten (condition b).";
                return false;
            }
        }

        foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon != null)
            {
                rejectReason =
                    "inaccessible method calls with named arguments are not rewritten (condition b).";
                return false;
            }
        }

        if (invocation.ArgumentList.Arguments.Count != methodSymbol.Parameters.Length)
        {
            rejectReason =
                "inaccessible method calls with omitted optional or expanded params arguments "
                + "are not rewritten (condition b).";
            return false;
        }

        plan.GetOrAddMethod(methodSymbol);
        return true;
    }

    private static bool TryRegisterPropertyOrFieldRead(
        ISymbol symbol,
        AccessorPlan plan,
        out string rejectReason)
    {
        rejectReason = null;
        if (symbol is IFieldSymbol fieldSymbol)
        {
            if (!AccessibilityRules.IsInaccessibleFromExternalAssembly(fieldSymbol))
            {
                return false;
            }

            plan.GetOrAddField(fieldSymbol);
            return true;
        }

        if (symbol is IPropertySymbol propertySymbol)
        {
            if (!AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod))
            {
                return false;
            }

            return TryRegisterPropertyRead(propertySymbol, plan, out rejectReason);
        }

        if (symbol is IEventSymbol)
        {
            rejectReason = "inaccessible event add/remove is out of scope for accessor rewrite (condition b).";
            return false;
        }

        if (symbol is IMethodSymbol methodSymbol
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(methodSymbol))
        {
            rejectReason =
                "inaccessible method group (non-invocation) has no accessor rewrite shape (condition b).";
            return false;
        }

        if (symbol != null
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol)
            && symbol is not INamespaceSymbol
            && symbol is not ITypeSymbol
            && symbol is not ILocalSymbol
            && symbol is not IParameterSymbol)
        {
            rejectReason = "inaccessible member kind is not field/method/property access (condition b).";
            return false;
        }

        return false;
    }

    private static bool TryRegisterPropertyRead(
        IPropertySymbol propertySymbol,
        AccessorPlan plan,
        out string rejectReason)
    {
        rejectReason = null;
        if (propertySymbol.IsIndexer)
        {
            rejectReason = "inaccessible indexer access has no accessor rewrite shape (condition b).";
            return false;
        }

        if (propertySymbol.IsStatic)
        {
            rejectReason =
                "inaccessible static property access has no accessor rewrite shape (condition b).";
            return false;
        }

        if (propertySymbol.ReturnsByRef || propertySymbol.ReturnsByRefReadonly)
        {
            rejectReason =
                "inaccessible ref-returning properties have no accessor rewrite shape (condition b).";
            return false;
        }

        plan.GetOrAddPropertyGetter(propertySymbol);
        return true;
    }

    private static bool TryRegisterPropertyWrite(
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment,
        IPropertySymbol propertySymbol,
        AccessorPlan plan,
        out string rejectReason)
    {
        rejectReason = null;
        bool needsGetter = !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);

        // Why accessibility first: shape gates (indexer/static/ref-return) must not reject fully
        // public writes such as dict[key]=value or Time.timeScale=0f. The read-side path already
        // pre-filters with IsInaccessibleAccessor/IsInaccessibleFromExternalAssembly before shape
        // checks — keep that order here for symmetry.
        bool setterInaccessible = AccessibilityRules.IsInaccessibleAccessor(propertySymbol.SetMethod);
        bool getterInaccessible = needsGetter
            && AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod);
        if (!setterInaccessible && !getterInaccessible)
        {
            return false;
        }

        if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            rejectReason =
                "null-coalescing assignment writes conditionally and has no accessor rewrite shape "
                + "(condition b).";
            return false;
        }

        if (needsGetter && !IsSupportedCompoundAssignmentKind(assignment.Kind()))
        {
            rejectReason =
                "unsupported compound assignment kind has no accessor rewrite shape (condition b).";
            return false;
        }

        // Compound assignment with a private getter and a public setter has no rewrite shape:
        // RewritePropertyAssignment only fires when the setter is inaccessible.
        if (getterInaccessible && !setterInaccessible)
        {
            rejectReason =
                "compound assignment reading an inaccessible getter with an accessible setter "
                + "has no accessor rewrite shape (condition b).";
            return false;
        }

        // Setter delegates are void — consuming the assignment expression value cannot compile.
        if (assignment.Parent is not ExpressionStatementSyntax)
        {
            rejectReason =
                "assignment value is consumed; the setter delegate returns void (condition b).";
            return false;
        }

        // Compound/get+set rewrite embeds the receiver twice; reject side-effecting receivers.
        if (needsGetter && !IsSideEffectFreeAssignmentReceiver(semanticModel, assignment.Left))
        {
            rejectReason =
                "receiver with possible side effects would be evaluated twice (condition b).";
            return false;
        }

        if (propertySymbol.IsIndexer)
        {
            rejectReason = "inaccessible indexer access has no accessor rewrite shape (condition b).";
            return false;
        }

        if (propertySymbol.IsStatic)
        {
            rejectReason =
                "inaccessible static property access has no accessor rewrite shape (condition b).";
            return false;
        }

        if (propertySymbol.ReturnsByRef || propertySymbol.ReturnsByRefReadonly)
        {
            rejectReason =
                "inaccessible ref-returning properties have no accessor rewrite shape (condition b).";
            return false;
        }

        if (setterInaccessible)
        {
            if (propertySymbol.SetMethod == null)
            {
                rejectReason = "inaccessible property has no setter to bind (condition b).";
                return false;
            }

            plan.GetOrAddPropertySetter(propertySymbol);
        }

        if (getterInaccessible)
        {
            if (propertySymbol.GetMethod == null)
            {
                rejectReason = "inaccessible property has no getter to bind (condition b).";
                return false;
            }

            plan.GetOrAddPropertyGetter(propertySymbol);
        }

        return true;
    }

    internal static bool IsSupportedCompoundAssignmentKind(SyntaxKind kind)
    {
        return kind == SyntaxKind.AddAssignmentExpression
            || kind == SyntaxKind.SubtractAssignmentExpression
            || kind == SyntaxKind.MultiplyAssignmentExpression
            || kind == SyntaxKind.DivideAssignmentExpression
            || kind == SyntaxKind.ModuloAssignmentExpression
            || kind == SyntaxKind.AndAssignmentExpression
            || kind == SyntaxKind.ExclusiveOrAssignmentExpression
            || kind == SyntaxKind.OrAssignmentExpression
            || kind == SyntaxKind.LeftShiftAssignmentExpression
            || kind == SyntaxKind.RightShiftAssignmentExpression
            || kind == SyntaxKind.UnsignedRightShiftAssignmentExpression;
    }

    /// <summary>
    /// What: whether an assignment left's receiver chain is free of re-evaluable members
    /// (properties/methods). Only this/locals/parameters/fields (and type/namespace qualifiers)
    /// are allowed — FieldRef re-reads the same storage, so field links are idempotent.
    /// </summary>
    internal static bool IsSideEffectFreeAssignmentReceiver(
        SemanticModel semanticModel,
        ExpressionSyntax left)
    {
        ExpressionSyntax receiver = left is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Expression
            : null;
        if (receiver == null)
        {
            // Bare member — implicit this.
            return true;
        }

        ExpressionSyntax current = receiver;
        while (current is MemberAccessExpressionSyntax nested)
        {
            ISymbol linkSymbol = semanticModel.GetSymbolInfo(nested.Name).Symbol
                ?? semanticModel.GetSymbolInfo(nested).Symbol;
            if (!IsSideEffectFreeReceiverLink(linkSymbol))
            {
                return false;
            }

            current = nested.Expression;
        }

        if (current is ThisExpressionSyntax || current is BaseExpressionSyntax)
        {
            return true;
        }

        if (current is IdentifierNameSyntax)
        {
            ISymbol headSymbol = semanticModel.GetSymbolInfo(current).Symbol;
            return headSymbol is ILocalSymbol
                || headSymbol is IParameterSymbol
                || headSymbol is IFieldSymbol
                || headSymbol is ITypeSymbol
                || headSymbol is INamespaceSymbol;
        }

        return false;
    }

    private static bool IsSideEffectFreeReceiverLink(ISymbol linkSymbol)
    {
        // Fields re-read the same storage; type/namespace qualifiers are not evaluated.
        return linkSymbol is IFieldSymbol
            || linkSymbol is ITypeSymbol
            || linkSymbol is INamespaceSymbol;
    }

    public static bool IsNameHandledByParent(SimpleNameSyntax node)
    {
        if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node)
        {
            return true;
        }

        if (node.Parent is QualifiedNameSyntax qualifiedName && qualifiedName.Right == node)
        {
            return true;
        }

        if (node.Parent is MemberBindingExpressionSyntax memberBinding && memberBinding.Name == node)
        {
            return true;
        }

        // Invocation targets are NOT handled here: method groups are skipped by the caller after
        // a symbol check; delegate-typed field invokes must reach the field-read path.
        return false;
    }
}

/// <summary>
/// Qualifies bare instance/static member references and, when an accessor plan is supplied,
/// rewrites inaccessible field/method/property accesses into Harmony accessor delegate calls.
/// </summary>
internal sealed class ShimBodyRewriter : CSharpSyntaxRewriter
{
    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol _targetType;
    private readonly AccessorPlan _accessorPlan;
    private readonly AddedMethodCatalog _addedMethodCatalog;
    private readonly AddedFieldCatalog _addedFieldCatalog;

    public ShimBodyRewriter(
        SemanticModel semanticModel,
        INamedTypeSymbol targetType,
        AccessorPlan accessorPlan,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        _semanticModel = semanticModel;
        _targetType = targetType;
        _accessorPlan = accessorPlan;
        _addedMethodCatalog = addedMethodCatalog ?? new AddedMethodCatalog();
        _addedFieldCatalog = addedFieldCatalog ?? new AddedFieldCatalog();
    }

    public override SyntaxNode VisitThisExpression(ThisExpressionSyntax node)
    {
        return SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName)
            .WithTriviaFrom(node);
    }

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        return VisitName(node, node);
    }

    public override SyntaxNode VisitGenericName(GenericNameSyntax node)
    {
        return VisitName(node, node);
    }

    public override SyntaxNode VisitInterpolation(InterpolationSyntax node)
    {
        InterpolationSyntax visited = (InterpolationSyntax)base.VisitInterpolation(node);

        // Why: a top-level ':' in an interpolation hole starts a format clause, so a
        // rewrite that inserts bare `global::` yields CS0103 ('global'). Parenthesizing
        // keeps the alias qualifier out of the format-clause scan and still coexists
        // with alignment/format clauses. Nested positions do not need parentheses, but
        // wrapping whenever an AliasQualifiedNameSyntax is present is always safe.
        // Alignment widths are integer expressions and hit the same ':' scan, so they
        // need the same wrapping; format clauses are literal text and need none.
        ExpressionSyntax parenthesizedExpression = ParenthesizeIfAliasQualified(visited.Expression);
        if (!ReferenceEquals(parenthesizedExpression, visited.Expression))
        {
            visited = visited.WithExpression(parenthesizedExpression);
        }

        InterpolationAlignmentClauseSyntax alignmentClause = visited.AlignmentClause;
        if (alignmentClause != null)
        {
            ExpressionSyntax parenthesizedAlignment = ParenthesizeIfAliasQualified(alignmentClause.Value);
            if (!ReferenceEquals(parenthesizedAlignment, alignmentClause.Value))
            {
                visited = visited.WithAlignmentClause(
                    alignmentClause.WithValue(parenthesizedAlignment));
            }
        }

        return visited;
    }

    private static ExpressionSyntax ParenthesizeIfAliasQualified(ExpressionSyntax expression)
    {
        if (expression is ParenthesizedExpressionSyntax)
        {
            return expression;
        }

        foreach (SyntaxNode descendant in expression.DescendantNodesAndSelf())
        {
            if (descendant is AliasQualifiedNameSyntax)
            {
                return SyntaxFactory.ParenthesizedExpression(expression);
            }
        }

        return expression;
    }

    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // Why first: added-member rewrite must not depend on _accessorPlan. Transplant bodies
        // have a null plan and would skip rewrite; delegation bodies would otherwise bind
        // Harmony accessors onto members that do not exist on the compiled type (B1).
        if (TransformWorkerProgram.NameofRules.IsNameofInvocation(node))
        {
            ExpressionSyntax folded = TryFoldNameofAddedMember(node);
            if (folded != null)
            {
                return folded;
            }

            return base.VisitInvocationExpression(node);
        }

        if (TransformWorkerProgram.NameofRules.IsInsideNameofArgument(node))
        {
            return base.VisitInvocationExpression(node);
        }

        ISymbol invokedSymbol = _semanticModel.GetSymbolInfo(node).Symbol;
        if (TransformWorkerProgram.IsConditionalAccessReceiverSpine(node))
        {
            // Why not rewrite the spine invocation: ExtractReceiver cannot recover a
            // MemberBinding/ElementBinding receiver and would emit a parse-invalid shim.
            // Arguments and lambdas are not on the spine; base.Visit still rewrites those.
            return base.VisitInvocationExpression(node);
        }

        if (invokedSymbol is IMethodSymbol addedMethod
            && addedMethod.MethodKind == MethodKind.Ordinary)
        {
            AddedMethodBinding binding = _addedMethodCatalog.FindOrNull(BuildAddedMethodKey(addedMethod));
            if (binding != null)
            {
                return RewriteAddedMethodInvocation(node, addedMethod, binding);
            }
        }

        if (_accessorPlan == null)
        {
            return base.VisitInvocationExpression(node);
        }

        ISymbol symbol = invokedSymbol;
        if (symbol is not IMethodSymbol methodSymbol
            || methodSymbol.MethodKind != MethodKind.Ordinary
            || !AccessibilityRules.IsInaccessibleFromExternalAssembly(methodSymbol)
            || methodSymbol.IsExtensionMethod)
        {
            return base.VisitInvocationExpression(node);
        }

        AccessorEntry entry = _accessorPlan.GetOrAddMethod(methodSymbol);
        List<ArgumentSyntax> arguments = new List<ArgumentSyntax>();
        if (!methodSymbol.IsStatic)
        {
            ExpressionSyntax receiver = ExtractReceiver(node.Expression);
            arguments.Add(SyntaxFactory.Argument(VisitReceiver(receiver)));
        }

        foreach (ArgumentSyntax argument in node.ArgumentList.Arguments)
        {
            arguments.Add((ArgumentSyntax)Visit(argument));
        }

        return SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(entry.DelegateFieldName),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)))
            .WithTriviaFrom(node);
    }

    private ExpressionSyntax TryFoldNameofAddedMember(InvocationExpressionSyntax nameofInvocation)
    {
        if (nameofInvocation.ArgumentList.Arguments.Count != 1)
        {
            return null;
        }

        ExpressionSyntax argument = nameofInvocation.ArgumentList.Arguments[0].Expression;
        ISymbol symbol = ResolveNameofArgumentSymbol(argument);
        if (symbol is IMethodSymbol methodSymbol
            && _addedMethodCatalog.Contains(BuildAddedMethodKey(methodSymbol)))
        {
            return SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(methodSymbol.Name))
                .WithTriviaFrom(nameofInvocation);
        }

        if (symbol is IFieldSymbol fieldSymbol
            && _addedFieldCatalog.FindOrNull(
                TransformWorkerProgram.FormatAddedFieldKeyFromSymbol(fieldSymbol)) != null)
        {
            return SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(fieldSymbol.Name))
                .WithTriviaFrom(nameofInvocation);
        }

        return null;
    }

    private ISymbol ResolveNameofArgumentSymbol(ExpressionSyntax argument)
    {
        // Why CandidateSymbols: nameof(method) is a method group, so Symbol is often null
        // and the unique candidate is the added method we need to fold.
        SymbolInfo symbolInfo = _semanticModel.GetSymbolInfo(argument);
        if (symbolInfo.Symbol != null)
        {
            return symbolInfo.Symbol;
        }

        if (symbolInfo.CandidateSymbols.Length == 1)
        {
            return symbolInfo.CandidateSymbols[0];
        }

        return null;
    }

    private SyntaxNode RewriteAddedMethodInvocation(
        InvocationExpressionSyntax node,
        IMethodSymbol addedMethod,
        AddedMethodBinding binding)
    {
        string qualifiedShimType = string.IsNullOrEmpty(binding.NamespaceName)
            ? "global::" + binding.ShimTypeName
            : "global::" + binding.NamespaceName + "." + binding.ShimTypeName;
        ExpressionSyntax shimTypeExpression = SyntaxFactory.ParseTypeName(qualifiedShimType);
        ExpressionSyntax shimAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            shimTypeExpression,
            SyntaxFactory.IdentifierName(binding.ShimMethodName));

        List<ArgumentSyntax> arguments = new List<ArgumentSyntax>();
        if (!addedMethod.IsStatic)
        {
            ExpressionSyntax receiver = ExtractReceiver(node.Expression);
            arguments.Add(SyntaxFactory.Argument(VisitReceiver(receiver)));
        }

        foreach (ArgumentSyntax argument in node.ArgumentList.Arguments)
        {
            arguments.Add((ArgumentSyntax)Visit(argument));
        }

        return SyntaxFactory.InvocationExpression(
                shimAccess,
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)))
            .WithTriviaFrom(node);
    }

    private static string BuildAddedMethodKey(IMethodSymbol methodSymbol)
    {
        string[] parameterTypeFullNames = methodSymbol.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        return methodSymbol.ContainingType == null
            ? methodSymbol.Name
            : CecilTypeNames.ToMetadataName(methodSymbol.ContainingType)
                + "::" + methodSymbol.Name + "("
                + string.Join(",", parameterTypeFullNames) + ")";
    }

    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        // Object/collection initializer member names must stay bare identifiers.
        if (node.Parent is InitializerExpressionSyntax)
        {
            return base.VisitAssignmentExpression(node);
        }

        if (TransformWorkerProgram.NameofRules.IsInsideNameofArgument(node))
        {
            return base.VisitAssignmentExpression(node);
        }

        AddedFieldBinding assignedField = FindStoreBinding(_semanticModel.GetSymbolInfo(node.Left).Symbol);
        if (assignedField != null)
        {
            return RewriteAddedFieldAssignment(node, assignedField);
        }

        if (_accessorPlan == null)
        {
            return base.VisitAssignmentExpression(node);
        }

        ISymbol leftSymbol = _semanticModel.GetSymbolInfo(node.Left).Symbol;
        if (leftSymbol is IPropertySymbol propertySymbol
            && !propertySymbol.IsIndexer
            && !propertySymbol.IsStatic
            && AccessibilityRules.IsInaccessibleAccessor(propertySymbol.SetMethod))
        {
            return RewritePropertyAssignment(node, propertySymbol);
        }

        if (leftSymbol is IFieldSymbol fieldSymbol
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(fieldSymbol))
        {
            AccessorEntry entry = _accessorPlan.GetOrAddField(fieldSymbol);
            ExpressionSyntax fieldRefCall = CreateFieldRefInvocation(
                entry,
                VisitReceiver(ExtractReceiver(node.Left)));
            return node
                .WithLeft(fieldRefCall)
                .WithRight((ExpressionSyntax)Visit(node.Right))
                .WithTriviaFrom(node);
        }

        return base.VisitAssignmentExpression(node);
    }

    public override SyntaxNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        if (TransformWorkerProgram.IsIncrementOrDecrement(node.Kind()))
        {
            AddedFieldBinding binding = FindStoreBinding(_semanticModel.GetSymbolInfo(node.Operand).Symbol);
            if (binding != null)
            {
                return RewriteAddedFieldIncrement(node.Operand, binding, node);
            }
        }

        return base.VisitPrefixUnaryExpression(node);
    }

    public override SyntaxNode VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        if (TransformWorkerProgram.IsIncrementOrDecrement(node.Kind()))
        {
            AddedFieldBinding binding = FindStoreBinding(_semanticModel.GetSymbolInfo(node.Operand).Symbol);
            if (binding != null)
            {
                return RewriteAddedFieldIncrement(node.Operand, binding, node);
            }
        }

        return base.VisitPostfixUnaryExpression(node);
    }

    private AddedFieldBinding FindStoreBinding(ISymbol symbol)
    {
        if (symbol is not IFieldSymbol fieldSymbol)
        {
            return null;
        }

        AddedFieldBinding binding = _addedFieldCatalog.FindOrNull(
            TransformWorkerProgram.FormatAddedFieldKeyFromSymbol(fieldSymbol));
        if (binding == null || !binding.IsStoreRewriteable)
        {
            return null;
        }

        return binding;
    }

    private AddedFieldBinding FindAnyAddedBinding(ISymbol symbol)
    {
        if (symbol is not IFieldSymbol fieldSymbol)
        {
            return null;
        }

        return _addedFieldCatalog.FindOrNull(
            TransformWorkerProgram.FormatAddedFieldKeyFromSymbol(fieldSymbol));
    }

    private SyntaxNode TryRewriteAddedFieldRead(
        ISymbol symbol,
        ExpressionSyntax receiverSyntax,
        SyntaxNode triviaSource)
    {
        AddedFieldBinding binding = FindAnyAddedBinding(symbol);
        if (binding == null || binding.UnavailableReason != null)
        {
            return null;
        }

        if (binding.IsConst)
        {
            ExpressionSyntax literal = TransformWorkerProgram.TryCreateConstantLiteral(
                binding.ConstantValue,
                binding.FieldType);
            if (literal == null)
            {
                return null;
            }

            return literal.WithTriviaFrom(triviaSource);
        }

        if (!binding.IsStoreRewriteable)
        {
            return null;
        }

        return CreateAddedFieldGetOrInit(binding, receiverSyntax).WithTriviaFrom(triviaSource);
    }

    private SyntaxNode RewriteAddedFieldAssignment(
        AssignmentExpressionSyntax node,
        AddedFieldBinding binding)
    {
        ExpressionSyntax receiver = ExtractAddedFieldReceiver(node.Left, binding.IsStatic);
        ExpressionSyntax visitedRight = (ExpressionSyntax)Visit(node.Right);
        if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return CreateAddedFieldSet(binding, receiver, visitedRight).WithTriviaFrom(node);
        }

        SyntaxKind binaryKind = GetCompoundAssignmentBinaryKind(node.Kind());
        ExpressionSyntax getCall = CreateAddedFieldGetOrInit(binding, receiver);
        ExpressionSyntax combined = SyntaxFactory.BinaryExpression(binaryKind, getCall, visitedRight);
        return CreateAddedFieldSet(
                binding,
                receiver,
                CastToAddedFieldType(combined, binding.FieldType))
            .WithTriviaFrom(node);
    }

    private SyntaxNode RewriteAddedFieldIncrement(
        ExpressionSyntax operand,
        AddedFieldBinding binding,
        SyntaxNode triviaSource)
    {
        ExpressionSyntax receiver = ExtractAddedFieldReceiver(operand, binding.IsStatic);
        ExpressionSyntax getCall = CreateAddedFieldGetOrInit(binding, receiver);
        SyntaxKind binaryKind = IsDecrementNode(triviaSource)
            ? SyntaxKind.SubtractExpression
            : SyntaxKind.AddExpression;
        ExpressionSyntax combined = SyntaxFactory.BinaryExpression(
            binaryKind,
            getCall,
            SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(1)));
        return CreateAddedFieldSet(
                binding,
                receiver,
                CastToAddedFieldType(combined, binding.FieldType))
            .WithTriviaFrom(triviaSource);
    }

    private static bool IsDecrementNode(SyntaxNode node)
    {
        if (node is PrefixUnaryExpressionSyntax prefix)
        {
            return prefix.IsKind(SyntaxKind.PreDecrementExpression);
        }

        return node is PostfixUnaryExpressionSyntax postfix
            && postfix.IsKind(SyntaxKind.PostDecrementExpression);
    }

    // Why cast: C# compound assignment and ++/-- apply a conversion back to the field type
    // (byte += 1 is (byte)(byte + 1)). Emitting the binary without that conversion is CS1503.
    private static ExpressionSyntax CastToAddedFieldType(ExpressionSyntax expression, ITypeSymbol fieldType)
    {
        TypeSyntax typeSyntax = SyntaxFactory.ParseTypeName(
            fieldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        return SyntaxFactory.CastExpression(
            typeSyntax,
            SyntaxFactory.ParenthesizedExpression(expression));
    }

    private ExpressionSyntax ExtractAddedFieldReceiver(ExpressionSyntax expression, bool isStatic)
    {
        if (isStatic)
        {
            return null;
        }

        ExpressionSyntax receiver = ExtractReceiver(expression);
        if (receiver is ThisExpressionSyntax || receiver is BaseExpressionSyntax)
        {
            return SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName);
        }

        return VisitReceiver(receiver);
    }

    private InvocationExpressionSyntax CreateAddedFieldGetOrInit(
        AddedFieldBinding binding,
        ExpressionSyntax receiver)
    {
        _addedFieldCatalog.MarkStoreRewrite();
        string methodName = binding.IsStatic
            ? TransformWorkerProgramMarker.AddedFieldGetOrInitStaticMethodName
            : TransformWorkerProgramMarker.AddedFieldGetOrInitMethodName;
        List<ArgumentSyntax> arguments = new List<ArgumentSyntax>();
        if (!binding.IsStatic)
        {
            arguments.Add(SyntaxFactory.Argument(receiver));
        }

        arguments.Add(
            SyntaxFactory.Argument(
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(binding.FieldKey))));
        arguments.Add(SyntaxFactory.Argument(CreateAddedFieldInitializer(binding)));
        return CreateAddedFieldStoreInvocation(methodName, binding.FieldType, arguments);
    }

    private InvocationExpressionSyntax CreateAddedFieldSet(
        AddedFieldBinding binding,
        ExpressionSyntax receiver,
        ExpressionSyntax value)
    {
        _addedFieldCatalog.MarkStoreRewrite();
        string methodName = binding.IsStatic
            ? TransformWorkerProgramMarker.AddedFieldSetStaticMethodName
            : TransformWorkerProgramMarker.AddedFieldSetMethodName;
        List<ArgumentSyntax> arguments = new List<ArgumentSyntax>();
        if (!binding.IsStatic)
        {
            arguments.Add(SyntaxFactory.Argument(receiver));
        }

        arguments.Add(
            SyntaxFactory.Argument(
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(binding.FieldKey))));
        arguments.Add(SyntaxFactory.Argument(value));
        return CreateAddedFieldStoreInvocation(methodName, binding.FieldType, arguments);
    }

    private static ExpressionSyntax CreateAddedFieldInitializer(AddedFieldBinding binding)
    {
        if (binding.Initializer == null)
        {
            return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
        }

        ExpressionSyntax cloned = SyntaxFactory.ParseExpression(binding.Initializer.ToString());
        return SyntaxFactory.ParenthesizedLambdaExpression(
                SyntaxFactory.ParameterList(),
                cloned)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword)));
    }

    private static InvocationExpressionSyntax CreateAddedFieldStoreInvocation(
        string methodName,
        ITypeSymbol fieldType,
        List<ArgumentSyntax> arguments)
    {
        TypeSyntax storeType = SyntaxFactory.ParseTypeName(
            TransformWorkerProgramMarker.AddedFieldStoreTypeName);
        TypeSyntax typeArgument = SyntaxFactory.ParseTypeName(
            fieldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        GenericNameSyntax genericName = SyntaxFactory.GenericName(SyntaxFactory.Identifier(methodName))
            .WithTypeArgumentList(
                SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(typeArgument)));
        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                storeType,
                genericName),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)));
    }

    private static bool IsAssignmentLeft(SyntaxNode node)
    {
        return node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node;
    }

    private static bool IsIncrementOperand(SyntaxNode node)
    {
        if (node.Parent is PrefixUnaryExpressionSyntax prefix
            && TransformWorkerProgram.IsIncrementOrDecrement(prefix.Kind()))
        {
            return true;
        }

        return node.Parent is PostfixUnaryExpressionSyntax postfix
            && TransformWorkerProgram.IsIncrementOrDecrement(postfix.Kind());
    }

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (TransformWorkerProgram.NameofRules.IsInsideNameofArgument(node))
        {
            return base.VisitMemberAccessExpression(node);
        }

        ISymbol symbol = _semanticModel.GetSymbolInfo(node).Symbol
            ?? _semanticModel.GetSymbolInfo(node.Name).Symbol;
        if (!IsAssignmentLeft(node) && !IsIncrementOperand(node))
        {
            SyntaxNode addedFieldRead = TryRewriteAddedFieldRead(symbol, node.Expression, node);
            if (addedFieldRead != null)
            {
                return addedFieldRead;
            }
        }

        if (_accessorPlan == null)
        {
            return base.VisitMemberAccessExpression(node);
        }

        // Method-group invocation targets stay with VisitInvocationExpression; field/property
        // delegate invokes (`this._cb()`) must rewrite here so the call becomes `__F__(recv)()`.
        if (node.Parent is InvocationExpressionSyntax invocation
            && invocation.Expression == node
            && symbol is IMethodSymbol)
        {
            return base.VisitMemberAccessExpression(node);
        }

        if (node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node)
        {
            return base.VisitMemberAccessExpression(node);
        }

        ExpressionSyntax rewritten = TryRewriteInaccessibleRead(symbol, node.Expression, node);
        if (rewritten != null)
        {
            return rewritten;
        }

        return base.VisitMemberAccessExpression(node);
    }

    private SyntaxNode VisitName(SimpleNameSyntax node, SyntaxNode original)
    {
        if (IsMemberAccessNameSide(node)
            || IsQualifiedNameRightSide(node)
            || IsMemberBindingName(node)
            || IsObjectOrCollectionInitializerMemberName(node))
        {
            return original;
        }

        ISymbol symbol = _semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol == null)
        {
            return original;
        }

        if (!IsAssignmentLeft(node)
            && !IsIncrementOperand(node)
            && !TransformWorkerProgram.NameofRules.IsInsideNameofArgument(node))
        {
            SyntaxNode addedFieldRead = TryRewriteAddedFieldRead(
                symbol,
                SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName),
                node);
            if (addedFieldRead != null)
            {
                return addedFieldRead;
            }
        }

        // Local/anonymous functions are emitted into the shim assembly — keep bare calls.
        if (symbol is IMethodSymbol methodSymbol
            && (methodSymbol.MethodKind == MethodKind.LocalFunction
                || methodSymbol.MethodKind == MethodKind.AnonymousFunction))
        {
            return original;
        }

        // nameof(...) and assignment left sides must keep a member-reference shape: qualify only,
        // never rewrite to an accessor read (Func<> call results are not assignable).
        bool suppressAccessorRead = TransformWorkerProgram.NameofRules.IsInsideNameofArgument(node)
            || (node.Parent is AssignmentExpressionSyntax assignmentLeft
                && assignmentLeft.Left == node);
        if (_accessorPlan != null && !suppressAccessorRead)
        {
            ExpressionSyntax accessorRead = TryRewriteInaccessibleRead(
                symbol,
                SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName),
                node);
            if (accessorRead != null)
            {
                return accessorRead;
            }
        }

        (bool owned, bool isStatic, INamedTypeSymbol containingType) ownership = ResolveOwnedMember(symbol);
        if (!ownership.owned)
        {
            return original;
        }

        if (ownership.isStatic)
        {
            TypeSyntax typeSyntax = SyntaxFactory.ParseTypeName(
                ownership.containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    typeSyntax,
                    (SimpleNameSyntax)node.WithoutTrivia())
                .WithTriviaFrom(node);
        }

        return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName),
                (SimpleNameSyntax)node.WithoutTrivia())
            .WithTriviaFrom(node);
    }

    private ExpressionSyntax TryRewriteInaccessibleRead(
        ISymbol symbol,
        ExpressionSyntax receiverSyntax,
        SyntaxNode triviaSource)
    {
        if (symbol is IFieldSymbol fieldSymbol
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(fieldSymbol))
        {
            AccessorEntry entry = _accessorPlan.GetOrAddField(fieldSymbol);
            return CreateFieldRefInvocation(entry, VisitReceiver(receiverSyntax))
                .WithTriviaFrom(triviaSource);
        }

        if (symbol is IPropertySymbol propertySymbol
            && !propertySymbol.IsIndexer
            && !propertySymbol.IsStatic
            && AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod))
        {
            AccessorEntry entry = _accessorPlan.GetOrAddPropertyGetter(propertySymbol);
            return CreateDelegateInvocation(
                    entry.DelegateFieldName,
                    new[] { VisitReceiver(receiverSyntax) })
                .WithTriviaFrom(triviaSource);
        }

        return null;
    }

    private SyntaxNode RewritePropertyAssignment(
        AssignmentExpressionSyntax node,
        IPropertySymbol propertySymbol)
    {
        ExpressionSyntax receiver = ExtractReceiver(node.Left);
        ExpressionSyntax visitedReceiver = VisitReceiver(receiver);
        ExpressionSyntax visitedRight = (ExpressionSyntax)Visit(node.Right);
        AccessorEntry setter = _accessorPlan.GetOrAddPropertySetter(propertySymbol);

        if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return CreateDelegateInvocation(
                    setter.DelegateFieldName,
                    new[] { visitedReceiver, visitedRight })
                .WithTriviaFrom(node);
        }

        AccessorEntry getter = _accessorPlan.GetOrAddPropertyGetter(propertySymbol);
        ExpressionSyntax getCall = CreateDelegateInvocation(
            getter.DelegateFieldName,
            new[] { visitedReceiver });
        SyntaxKind binaryKind = GetCompoundAssignmentBinaryKind(node.Kind());
        ExpressionSyntax combined = SyntaxFactory.BinaryExpression(binaryKind, getCall, visitedRight);
        return CreateDelegateInvocation(
                setter.DelegateFieldName,
                new[] { visitedReceiver, combined })
            .WithTriviaFrom(node);
    }

    private static SyntaxKind GetCompoundAssignmentBinaryKind(SyntaxKind assignmentKind)
    {
        return assignmentKind switch
        {
            SyntaxKind.AddAssignmentExpression => SyntaxKind.AddExpression,
            SyntaxKind.SubtractAssignmentExpression => SyntaxKind.SubtractExpression,
            SyntaxKind.MultiplyAssignmentExpression => SyntaxKind.MultiplyExpression,
            SyntaxKind.DivideAssignmentExpression => SyntaxKind.DivideExpression,
            SyntaxKind.ModuloAssignmentExpression => SyntaxKind.ModuloExpression,
            SyntaxKind.AndAssignmentExpression => SyntaxKind.BitwiseAndExpression,
            SyntaxKind.ExclusiveOrAssignmentExpression => SyntaxKind.ExclusiveOrExpression,
            SyntaxKind.OrAssignmentExpression => SyntaxKind.BitwiseOrExpression,
            SyntaxKind.LeftShiftAssignmentExpression => SyntaxKind.LeftShiftExpression,
            SyntaxKind.RightShiftAssignmentExpression => SyntaxKind.RightShiftExpression,
            SyntaxKind.UnsignedRightShiftAssignmentExpression => SyntaxKind.UnsignedRightShiftExpression,
            // Eligibility must reject unsupported compounds (including ??=) before rewrite.
            _ => throw new System.InvalidOperationException(
                "Unsupported compound assignment kind reached property rewrite: " + assignmentKind)
        };
    }

    private static ExpressionSyntax CreateFieldRefInvocation(
        AccessorEntry entry,
        ExpressionSyntax visitedReceiver)
    {
        if (entry.FieldSymbol.IsStatic)
        {
            return CreateDelegateInvocation(entry.DelegateFieldName, Array.Empty<ExpressionSyntax>());
        }

        return CreateDelegateInvocation(entry.DelegateFieldName, new[] { visitedReceiver });
    }

    private static ExpressionSyntax CreateDelegateInvocation(
        string delegateFieldName,
        IReadOnlyList<ExpressionSyntax> arguments)
    {
        SeparatedSyntaxList<ArgumentSyntax> argumentList = SyntaxFactory.SeparatedList(
            arguments.Select(SyntaxFactory.Argument));
        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(delegateFieldName),
            SyntaxFactory.ArgumentList(argumentList));
    }

    private ExpressionSyntax ExtractReceiver(ExpressionSyntax expression)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Expression;
        }

        return SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName);
    }

    // Why not Visit synthetic nodes: GetSymbolInfo requires nodes from the original SemanticModel
    // tree. Bare-member rewrite invents IdentifierName(InstanceParameterName), which must not be re-visited.
    private ExpressionSyntax VisitReceiver(ExpressionSyntax receiver)
    {
        if (receiver.SyntaxTree != _semanticModel.SyntaxTree)
        {
            return receiver;
        }

        return (ExpressionSyntax)Visit(receiver);
    }

    private (bool owned, bool isStatic, INamedTypeSymbol containingType) ResolveOwnedMember(ISymbol symbol)
    {
        INamedTypeSymbol containingType;
        bool isStatic;

        if (symbol is IMethodSymbol methodSymbol)
        {
            if (methodSymbol.IsExtensionMethod)
            {
                return (false, false, null);
            }

            containingType = methodSymbol.ContainingType;
            isStatic = methodSymbol.IsStatic;
        }
        else if (symbol is IFieldSymbol fieldSymbol)
        {
            containingType = fieldSymbol.ContainingType;
            isStatic = fieldSymbol.IsStatic;
        }
        else if (symbol is IPropertySymbol propertySymbol)
        {
            containingType = propertySymbol.ContainingType;
            isStatic = propertySymbol.IsStatic;
        }
        else if (symbol is IEventSymbol eventSymbol)
        {
            containingType = eventSymbol.ContainingType;
            isStatic = eventSymbol.IsStatic;
        }
        else
        {
            return (false, false, null);
        }

        if (containingType == null)
        {
            return (false, false, null);
        }

        if (!IsInInheritanceHierarchy(_targetType, containingType))
        {
            return (false, false, null);
        }

        return (true, isStatic, containingType);
    }

    private static bool IsInInheritanceHierarchy(INamedTypeSymbol derived, INamedTypeSymbol candidate)
    {
        for (INamedTypeSymbol current = derived; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMemberAccessNameSide(SimpleNameSyntax node)
    {
        return node.Parent is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name == node;
    }

    private static bool IsQualifiedNameRightSide(SimpleNameSyntax node)
    {
        return node.Parent is QualifiedNameSyntax qualifiedName
            && qualifiedName.Right == node;
    }

    private static bool IsMemberBindingName(SimpleNameSyntax node)
    {
        return node.Parent is MemberBindingExpressionSyntax memberBinding
            && memberBinding.Name == node;
    }

    // `new T { _field = 1 }` must keep the bare member name; qualifying to instance._field is
    // invalid inside an object/collection initializer.
    private static bool IsObjectOrCollectionInitializerMemberName(SimpleNameSyntax node)
    {
        if (node.Parent is not AssignmentExpressionSyntax assignment || assignment.Left != node)
        {
            return false;
        }

        return assignment.Parent is InitializerExpressionSyntax;
    }
}

// Nested access to the instance parameter name without exposing TransformWorkerProgram fields
// to the rewriter as a circular partial. Kept as a tiny marker type so the rewriter stays free
// of string literals scattered across Visit overrides.
internal static class TransformWorkerProgramMarker
{
    // Why "__uloopInstance": the shim prepends this receiver parameter to the user's own
    // parameter list verbatim, so a plain name like "instance" collides (CS0100) with any
    // user parameter or local of that name. The uloop-prefixed name makes collisions
    // practically impossible.
    public const string InstanceParameterName = "__uloopInstance";

    // Keep in sync with HotReloadAddedFieldStore in ToolContracts.
    public const string AddedFieldStoreTypeName =
        "global::io.github.hatayama.UnityCliLoop.ToolContracts.HotReloadAddedFieldStore";

    public const string AddedFieldGetOrInitMethodName = "GetOrInit";

    public const string AddedFieldSetMethodName = "Set";

    public const string AddedFieldGetOrInitStaticMethodName = "GetOrInitStatic";

    public const string AddedFieldSetStaticMethodName = "SetStatic";

    // Keep in sync with HotReloadAddedFieldStore.FieldKeySeparator.
    public const string AddedFieldKeySeparator = "::";
}

internal static class ShimMethodFactory
{
    public static MethodDeclarationSyntax ToShimMethod(
        MethodDeclarationSyntax rewrittenOriginal,
        IMethodSymbol methodSymbol)
    {
        TypeSyntax returnType = rewrittenOriginal.ReturnType.WithoutTrivia();
        SyntaxTokenList modifiers = SyntaxFactory.TokenList(
            SyntaxFactory.Token(SyntaxKind.PublicKeyword),
            SyntaxFactory.Token(SyntaxKind.StaticKeyword));

        // Async is preserved so the shim assembly still emits a state machine when the original
        // was async (transplant covers the stub; MoveNext stays in the shim assembly).
        if (rewrittenOriginal.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.AsyncKeyword)))
        {
            modifiers = modifiers.Add(SyntaxFactory.Token(SyntaxKind.AsyncKeyword));
        }

        SeparatedSyntaxList<ParameterSyntax> parameters = BuildShimParameters(rewrittenOriginal, methodSymbol);
        MethodDeclarationSyntax shim = rewrittenOriginal
            .WithAttributeLists(default)
            .WithModifiers(modifiers)
            .WithReturnType(returnType)
            .WithParameterList(SyntaxFactory.ParameterList(parameters))
            .WithExplicitInterfaceSpecifier(null)
            .WithConstraintClauses(default)
            .WithLeadingTrivia(StripDirectiveTrivia(rewrittenOriginal.GetLeadingTrivia()))
            .WithTrailingTrivia(StripDirectiveTrivia(rewrittenOriginal.GetTrailingTrivia()));

        // Expression-bodied methods must keep their terminating semicolon; block bodies must not.
        return rewrittenOriginal.ExpressionBody != null
            ? shim.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            : shim.WithSemicolonToken(default);
    }

    // Why strip directives: #if sits on the method's leading trivia while its matching #endif
    // belongs to the next token, so copied directives are unbalanced in the shim; #line mapping
    // is injected later from annotations and needs no user directives.
    private static SyntaxTriviaList StripDirectiveTrivia(SyntaxTriviaList trivia)
    {
        List<SyntaxTrivia> kept = new List<SyntaxTrivia>();
        foreach (SyntaxTrivia item in trivia)
        {
            if (!item.IsDirective)
            {
                kept.Add(item);
            }
        }

        return SyntaxFactory.TriviaList(kept);
    }

    private static SeparatedSyntaxList<ParameterSyntax> BuildShimParameters(
        MethodDeclarationSyntax rewrittenOriginal,
        IMethodSymbol methodSymbol)
    {
        List<ParameterSyntax> parameters = new List<ParameterSyntax>();
        if (!methodSymbol.IsStatic)
        {
            TypeSyntax instanceType = SyntaxFactory.ParseTypeName(
                methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            parameters.Add(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier(TransformWorkerProgramMarker.InstanceParameterName))
                    .WithType(instanceType));
        }

        foreach (ParameterSyntax originalParameter in rewrittenOriginal.ParameterList.Parameters)
        {
            parameters.Add(originalParameter.WithoutTrivia());
        }

        return SyntaxFactory.SeparatedList(parameters);
    }
}

internal sealed class ShimTypeBuilder
{
    private readonly List<MethodDeclarationSyntax> _methods = new List<MethodDeclarationSyntax>();

    public ShimTypeBuilder(
        string shimTypeName,
        string namespaceName,
        List<UsingDirectiveSyntax> usings)
    {
        ShimTypeName = shimTypeName;
        NamespaceName = namespaceName ?? string.Empty;
        Usings = usings ?? new List<UsingDirectiveSyntax>();
        AccessorPlan = new AccessorPlan();
    }

    public string ShimTypeName { get; }

    public string NamespaceName { get; }

    public List<UsingDirectiveSyntax> Usings { get; }

    /// <summary>
    /// Shim-type-level accessor registry — shared across all delegation methods in this type so
    /// AllocateName stays unique and overloads cannot collide after a per-method merge.
    /// </summary>
    public AccessorPlan AccessorPlan { get; }

    public void AddMethod(MethodDeclarationSyntax shimMethod, string shimMethodName)
    {
        MethodDeclarationSyntax named = shimMethod.WithIdentifier(SyntaxFactory.Identifier(shimMethodName));
        _methods.Add(named);
    }

    public IReadOnlyList<MethodDeclarationSyntax> Methods => _methods;

    public IEnumerable<MemberDeclarationSyntax> EmitMembers()
    {
        foreach (AccessorEntry accessor in AccessorPlan.Entries)
        {
            yield return accessor.EmitFieldDeclaration();
        }

        if (AccessorPlan.Entries.Count > 0)
        {
            yield return EmitBindAccessorsMethod();
        }

        foreach (MethodDeclarationSyntax method in _methods)
        {
            yield return method;
        }
    }

    private MethodDeclarationSyntax EmitBindAccessorsMethod()
    {
        List<StatementSyntax> statements = new List<StatementSyntax>();
        foreach (AccessorEntry accessor in AccessorPlan.Entries)
        {
            statements.Add(accessor.EmitBindStatement());
        }

        return SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                "__BindAccessors")
            .WithModifiers(
                SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithBody(SyntaxFactory.Block(statements));
    }
}

internal static class ShimSourceEmitter
{
    public static string Emit(
        CompilationUnitSyntax originalRoot,
        List<ShimTypeBuilder> shimTypes,
        string projectRelativePath)
    {
        if (shimTypes.Count == 0)
        {
            return string.Empty;
        }

        // projectRelativePath shape is validated at TransformFile's input boundary (ParseErrors).

        // Emit each shim type in the original type's namespace (and with that type's usings) so
        // unqualified sibling-type references in transplanted bodies still resolve. Manifest
        // shimTypeName stays the short name; orchestrator resolves by Type.Name.
        CompilationUnitSyntax unit = SyntaxFactory.CompilationUnit();
        foreach (ShimTypeBuilder shimType in shimTypes)
        {
            ClassDeclarationSyntax classDeclaration = SyntaxFactory.ClassDeclaration(shimType.ShimTypeName)
                .WithModifiers(
                    SyntaxFactory.TokenList(
                        SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                        SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithMembers(SyntaxFactory.List(shimType.EmitMembers()));

            if (string.IsNullOrEmpty(shimType.NamespaceName))
            {
                foreach (UsingDirectiveSyntax usingDirective in shimType.Usings)
                {
                    unit = unit.AddUsings(usingDirective);
                }

                unit = unit.AddMembers(classDeclaration);
            }
            else
            {
                NamespaceDeclarationSyntax namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(
                        SyntaxFactory.ParseName(shimType.NamespaceName))
                    .WithUsings(SyntaxFactory.List(shimType.Usings))
                    .WithMembers(
                        SyntaxFactory.SingletonList<MemberDeclarationSyntax>(classDeclaration));
                unit = unit.AddMembers(namespaceDeclaration);
            }
        }

        // Why after NormalizeWhitespace: formatting would otherwise shift #line relative to
        // statements; annotations survive formatting so we inject directives on the final tree.
        unit = unit.NormalizeWhitespace();
        unit = InjectLineDirectives(unit, projectRelativePath);

        StringBuilder builder = new StringBuilder();
        builder.AppendLine(
            "// Generated shims mirror user method signatures verbatim; repo style rules apply to hand-written code only.");
        builder.Append(unit.ToFullString());
        return builder.ToString();
    }

    private static CompilationUnitSyntax InjectLineDirectives(
        CompilationUnitSyntax unit,
        string projectRelativePath)
    {
        List<SyntaxNode> annotatedNodes = unit.GetAnnotatedNodes(TransformWorkerProgram.UloopLineAnnotationKind)
            .ToList();
        if (annotatedNodes.Count > 0)
        {
            unit = unit.ReplaceNodes(
                annotatedNodes,
                (original, rewritten) =>
                {
                    SyntaxAnnotation annotation = original
                        .GetAnnotations(TransformWorkerProgram.UloopLineAnnotationKind)
                        .First();
                    // Why leading trivia starts/ends with newline: #line must occupy its own line.
                    string directiveText =
                        "\n#line " + annotation.Data + " \"" + projectRelativePath + "\"\n";
                    SyntaxTriviaList leading = SyntaxFactory.ParseLeadingTrivia(directiveText);
                    return rewritten.WithLeadingTrivia(leading.AddRange(rewritten.GetLeadingTrivia()));
                });
        }

        // Reset mapping after each method so scaffold (__BindAccessors, fields, class braces)
        // does not inherit the previous method's document/line.
        List<MethodDeclarationSyntax> methods = unit.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .ToList();
        if (methods.Count == 0)
        {
            return unit;
        }

        // Why ParseLeadingTrivia into trailing: ParseTrailingTrivia does not reliably produce
        // LineDirectiveTrivia for "#line default", while ParseLeadingTrivia does — and directive
        // trivia is legal in a trailing trivia list for ToFullString emission.
        return unit.ReplaceNodes(
            methods,
            (original, rewritten) =>
            {
                SyntaxTriviaList defaultDirective = SyntaxFactory.ParseLeadingTrivia("\n#line default\n");
                return rewritten.WithTrailingTrivia(
                    rewritten.GetTrailingTrivia().AddRange(defaultDirective));
            });
    }
}

internal static class CecilTypeNames
{
    public static string ToMetadataName(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.ContainingType != null)
        {
            return ToMetadataName(typeSymbol.ContainingType) + "/" + typeSymbol.MetadataName;
        }

        if (typeSymbol.ContainingNamespace == null || typeSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            return typeSymbol.MetadataName;
        }

        return typeSymbol.ContainingNamespace.ToDisplayString() + "." + typeSymbol.MetadataName;
    }

    public static string ToParameterTypeFullName(IParameterSymbol parameterSymbol)
    {
        // Roslyn's IParameterSymbol.Type omits the byref wrapper; Cecil FullName uses a trailing '&'.
        string typeName = ToCecilFullName(parameterSymbol.Type);
        if (parameterSymbol.RefKind != RefKind.None)
        {
            return typeName + "&";
        }

        return typeName;
    }

    public static string ToCecilFullName(ITypeSymbol typeSymbol)
    {
        // Why here (not only the parameter top level): source `List<dynamic>` / `dynamic[]`
        // nest TypeKind.Dynamic while compiled metadata uses System.Object at the same depth.
        if (typeSymbol != null && typeSymbol.TypeKind == TypeKind.Dynamic)
        {
            return "System.Object";
        }

        if (typeSymbol is IPointerTypeSymbol pointerType)
        {
            return ToCecilFullName(pointerType.PointedAtType) + "*";
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            string elementName = ToCecilFullName(arrayType.ElementType);
            if (arrayType.Rank == 1)
            {
                return elementName + "[]";
            }

            // Cecil ArrayType.FullName for non-vector arrays uses "[0...,0...]" (lower bounds),
            // not the C# syntactic "[,]".
            string[] dimensionMarks = new string[arrayType.Rank];
            for (int index = 0; index < arrayType.Rank; index++)
            {
                dimensionMarks[index] = "0...";
            }

            return elementName + "[" + string.Join(",", dimensionMarks) + "]";
        }

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            // Cecil nests constructed generics as Outer/Inner<all-args-outer-to-inner>, so collect
            // TypeArguments from the containment chain rather than only the leaf type.
            List<ITypeSymbol> typeArguments = new List<ITypeSymbol>();
            CollectConstructedTypeArgumentsOuterToInner(namedType, typeArguments);
            string head = ToMetadataName(namedType.OriginalDefinition);
            if (typeArguments.Count == 0)
            {
                return head;
            }

            string args = string.Join(",", typeArguments.Select(ToCecilFullName));
            return head + "<" + args + ">";
        }

        return typeSymbol.ToDisplayString(
            new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));
    }

    private static void CollectConstructedTypeArgumentsOuterToInner(
        INamedTypeSymbol namedType,
        List<ITypeSymbol> typeArguments)
    {
        if (namedType.ContainingType != null)
        {
            CollectConstructedTypeArgumentsOuterToInner(namedType.ContainingType, typeArguments);
        }

        if (namedType.IsGenericType && !namedType.IsUnboundGenericType)
        {
            typeArguments.AddRange(namedType.TypeArguments);
        }
    }
}

internal sealed class TypeEmitState
{
    public TypeDeclarationSyntax TypeDeclaration { get; set; }

    public INamedTypeSymbol TypeSymbol { get; set; }

    public string TypeMetadataNameFromSyntax { get; set; }

    public ShimTypeBuilder CurrentShimType { get; set; }

    public List<QueuedShimMethod> QueuedMethods { get; } = new List<QueuedShimMethod>();

    public bool TypeIsAbsentFromCompiledAssembly { get; set; }
}

internal sealed class QueuedShimMethod
{
    public MethodDeclarationSyntax MethodDeclaration { get; set; }

    public IMethodSymbol MethodSymbol { get; set; }

    public MethodTransformDecision Decision { get; set; }

    public string ShimMethodName { get; set; }

    public ShimTypeBuilder ShimType { get; set; }

    public int SourceStartLine { get; set; }

    public int SourceEndLine { get; set; }

    public string[] ParameterTypeFullNames { get; set; }

    public string MethodKey { get; set; }

    public bool IsAddedMethod { get; set; }

    public bool ReplacesCompiledMethod { get; set; }
}

internal sealed class AddedMethodBinding
{
    public string MethodKey { get; set; }

    public string ShimTypeName { get; set; }

    public string ShimMethodName { get; set; }

    public string NamespaceName { get; set; }

    public bool IsStatic { get; set; }
}

internal sealed class AddedMethodCatalog
{
    private readonly Dictionary<string, AddedMethodBinding> _byKey =
        new Dictionary<string, AddedMethodBinding>(StringComparer.Ordinal);
    private readonly HashSet<string> _classifiedAddedKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _addedSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _addedTypeSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _removedSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyCollection<string> AddedSyntaxKeys => _addedSyntaxKeys;

    public IReadOnlyCollection<string> AddedTypeSyntaxKeys => _addedTypeSyntaxKeys;

    public IReadOnlyCollection<string> RemovedSyntaxKeys => _removedSyntaxKeys;

    public void Register(AddedMethodBinding binding)
    {
        _byKey[binding.MethodKey] = binding;
        MarkClassifiedAdded(binding.MethodKey);
    }

    public void MarkClassifiedAdded(string methodKey)
    {
        if (methodKey != null)
        {
            _classifiedAddedKeys.Add(methodKey);
        }
    }

    public bool IsClassifiedAdded(string methodKey)
    {
        return methodKey != null && _classifiedAddedKeys.Contains(methodKey);
    }

    public bool IsUnavailableAdded(string methodKey)
    {
        return IsClassifiedAdded(methodKey) && !Contains(methodKey);
    }

    public void AddAddedSyntaxKey(string syntaxKey)
    {
        _addedSyntaxKeys.Add(syntaxKey);
    }

    public void AddAddedTypeSyntaxKey(string typeSyntaxKey)
    {
        if (typeSyntaxKey != null)
        {
            _addedTypeSyntaxKeys.Add(typeSyntaxKey);
        }
    }

    public void AddRemovedSyntaxKey(string syntaxKey)
    {
        _removedSyntaxKeys.Add(syntaxKey);
    }

    public bool Contains(string methodKey)
    {
        return methodKey != null && _byKey.ContainsKey(methodKey);
    }

    public AddedMethodBinding FindOrNull(string methodKey)
    {
        if (methodKey == null)
        {
            return null;
        }

        return _byKey.TryGetValue(methodKey, out AddedMethodBinding binding) ? binding : null;
    }

    public void Unregister(string methodKey)
    {
        if (methodKey != null)
        {
            _byKey.Remove(methodKey);
        }
    }
}

/// <summary>
/// What: one added field's store/const/unavailable binding used by skip evaluation and rewrite.
/// </summary>
internal sealed class AddedFieldBinding
{
    public string FieldKey { get; set; }

    public string SyntaxKey { get; set; }

    public string FieldName { get; set; }

    public ITypeSymbol FieldType { get; set; }

    public bool IsStatic { get; set; }

    public bool IsConst { get; set; }

    public object ConstantValue { get; set; }

    public ExpressionSyntax Initializer { get; set; }

    public string UnavailableReason { get; set; }

    public bool IsStoreRewriteable => UnavailableReason == null && !IsConst;
}

/// <summary>
/// What: file-wide catalog of added fields, syntax keys for drift strip, and whether any
/// shim body actually emitted a HotReloadAddedFieldStore call.
/// </summary>
internal sealed class AddedFieldCatalog
{
    private readonly Dictionary<string, AddedFieldBinding> _byKey =
        new Dictionary<string, AddedFieldBinding>(StringComparer.Ordinal);
    private readonly HashSet<string> _classifiedAddedKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _addedSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _removedSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyCollection<string> AddedSyntaxKeys => _addedSyntaxKeys;

    public IReadOnlyCollection<string> RemovedSyntaxKeys => _removedSyntaxKeys;

    public bool HasClassifiedAdded => _classifiedAddedKeys.Count > 0;

    public bool HasStoreRewrites { get; private set; }

    public void MarkClassifiedAdded(string fieldKey)
    {
        if (fieldKey != null)
        {
            _classifiedAddedKeys.Add(fieldKey);
        }
    }

    public void AddAddedSyntaxKey(string syntaxKey)
    {
        _addedSyntaxKeys.Add(syntaxKey);
    }

    public void AddRemovedSyntaxKey(string syntaxKey)
    {
        _removedSyntaxKeys.Add(syntaxKey);
    }

    public void RegisterStore(AddedFieldBinding binding)
    {
        _byKey[binding.FieldKey] = binding;
        MarkClassifiedAdded(binding.FieldKey);
    }

    public void RegisterConst(AddedFieldBinding binding)
    {
        _byKey[binding.FieldKey] = binding;
        MarkClassifiedAdded(binding.FieldKey);
    }

    public void RegisterUnavailable(AddedFieldBinding binding)
    {
        _byKey[binding.FieldKey] = binding;
        MarkClassifiedAdded(binding.FieldKey);
    }

    public AddedFieldBinding FindOrNull(string fieldKey)
    {
        if (fieldKey == null)
        {
            return null;
        }

        return _byKey.TryGetValue(fieldKey, out AddedFieldBinding binding) ? binding : null;
    }

    public void MarkStoreRewrite()
    {
        HasStoreRewrites = true;
    }
}

/// <summary>
/// What: skip and warning strings for added-field classification. Keep in the existing
/// "reason + run uloop compile" style; the worker cannot reference HotReloadConstants.
/// </summary>
internal static class AddedFieldSkipReasons
{
    public const string StructHost =
        "Added fields on struct types are skipped; the store requires a reference-type instance. "
        + "Run 'uloop compile' to add them.";

    public const string InitializerNotLiteralOrExternalStatic =
        "Added field initializer is not a literal or externally visible static member; "
        + "the shim lambda cannot use instance, host-type, or same-file added members. "
        + "Run 'uloop compile'.";

    public const string FieldTypeNotExternallyVisible =
        "Added field type is not visible to the shim assembly. Run 'uloop compile'.";

    public const string IncrementNotNumeric =
        "Increment or decrement of an added field is skipped unless the type is a numeric "
        + "primitive or enum. Run 'uloop compile'.";

    public const string RefOutIn =
        "Added fields cannot be passed by ref, out, or in. Run 'uloop compile'.";

    public const string ConsumedWrite =
        "The value of an assignment to an added field is consumed; the store write returns void. "
        + "Run 'uloop compile'.";

    public const string DoubleEvalReceiver =
        "Assignment to an added field would evaluate a receiver with possible side effects twice. "
        + "Run 'uloop compile'.";

    public const string ValueTypeMemberWrite =
        "Writes to members of an added value-type field, and instance method calls on that field, "
        + "cannot be rewritten. Run 'uloop compile'.";

    public const string UnavailableAddedField =
        "Uses an added field that hot reload cannot emit. Run 'uloop compile'.";

    public const string FieldTypeChanged =
        "Field '{0}' has a different type in the compiled assembly. Run 'uloop compile'.";

    public const string FieldModifiersChanged =
        "Field '{0}' changed its static or const modifier in the compiled assembly. Run 'uloop compile'.";

    public const string MemberKindChanged =
        "Field '{0}' is declared as a property or an event in the compiled assembly. Run 'uloop compile'.";

    public const string SerializeWarningFormat =
        "Added field '{0}' has a serialization attribute, so it will not appear in the Inspector "
        + "or serialize until 'uloop compile'.";
}

internal static class AddedMethodSkipReasons
{
    public const string VirtualOrAbstract =
        "Added virtual, override, or abstract methods are skipped; the compiled type has no vtable slot. "
        + "Run 'uloop compile' to add them.";

    public const string Generic =
        "Added generic methods are skipped; hot reload cannot emit a typed shim for them. "
        + "Run 'uloop compile'.";

    public const string MethodGroupReference =
        "Methods that capture an added method as a method group or delegate are skipped; "
        + "the shim signature does not match. Run 'uloop compile'.";

    public const string ConditionalAccess =
        "Added-method calls through conditional access are skipped; there is no rewrite shape. "
        + "Run 'uloop compile'.";

    public const string UnavailableAddedCall =
        "Calls an added method that hot reload cannot emit. Run 'uloop compile'.";

    public const string NewTypeOutOfScope =
        "New types are out of scope for hot reload; run 'uloop compile' to add them.";

    public const string InterfaceMember =
        "Interface members are not patchable. Run 'uloop compile'.";

    public const string InaccessibleAccessNoRewrite =
        "Added methods whose bodies access private/internal members are skipped when the access "
        + "has no accessor rewrite (the added method JIT-compiles normally and fails accessibility "
        + "checks). Run 'uloop compile'.";
}

internal static class UnityMessageNames
{
    // Keep in sync with the Unity-message set that PR-5 will document in
    // Packages/src/Editor/FirstPartyTools/HotReload/Skill/SKILL.md.
    public const string AddedMessageWarningFormat =
        "Added Unity message '{0}' on {1} will not be invoked by the engine until 'uloop compile'; "
        + "Unity discovers messages by reflection on the compiled type.";

    private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        "Awake",
        "Start",
        "OnEnable",
        "OnDisable",
        "OnDestroy",
        "Update",
        "LateUpdate",
        "FixedUpdate",
        "OnGUI",
        "Reset",
        "OnValidate",
        "OnCollisionEnter",
        "OnCollisionExit",
        "OnCollisionStay",
        "OnTriggerEnter",
        "OnTriggerExit",
        "OnTriggerStay",
        "OnCollisionEnter2D",
        "OnCollisionExit2D",
        "OnCollisionStay2D",
        "OnTriggerEnter2D",
        "OnTriggerExit2D",
        "OnTriggerStay2D",
        "OnMouseDown",
        "OnMouseUp",
        "OnMouseEnter",
        "OnMouseExit",
        "OnMouseOver",
        "OnMouseDrag",
        "OnBecameVisible",
        "OnBecameInvisible",
        "OnApplicationQuit",
        "OnApplicationPause",
        "OnApplicationFocus",
        "OnTransformChildrenChanged",
        "OnTransformParentChanged",
        "OnRectTransformDimensionsChange",
        "OnParticleCollision",
        "OnParticleTrigger",
        "OnControllerColliderHit",
        "OnJointBreak",
        "OnJointBreak2D",
        "OnAnimatorMove",
        "OnAnimatorIK",
        "OnDrawGizmos",
        "OnDrawGizmosSelected"
    };

    public static bool Contains(string methodName)
    {
        return methodName != null && Names.Contains(methodName);
    }
}

internal sealed class WorkerInput
{
    public string SourcePath { get; set; }

    public string[] Defines { get; set; }

    public string[] ReferencePaths { get; set; }

    public string TargetTypesAssemblyPath { get; set; }

    // Method keys (see TransformWorkerProgram.BuildMethodKey) that the orchestrator already
    // reported Failed from a first compile round; the retry excludes them so it does not fail
    // on the same error again.
    public string[] ExcludedMethodKeys { get; set; }

    // Added-method keys whose shim bodies failed the first compile. Distinct from
    // ExcludedMethodKeys so a healthy added shim is not dropped when an existing method fails.
    public string[] ExcludedAddedMethodKeys { get; set; }

    // Verified snapshot text for edited-method detection. Null = no baseline, patch all methods.
    // Why pass text (not a path): avoids an IO race between orchestrator verification and worker
    // read that would crash the whole file under the no-try-catch policy.
    public string SnapshotSource { get; set; }

    // Project-relative forward-slash path embedded in #line document names.
    public string ProjectRelativePath { get; set; }

    // Absolute paths of every source file in the edited file's compilation assembly.
    // Null/omitted is treated as empty (no sibling global usings collected).
    public string[] AssemblySourcePaths { get; set; }
}

internal sealed class WorkerOutput
{
    public string ShimSource { get; set; }

    public WorkerEntry[] Entries { get; set; }

    public WorkerSkipped[] Skipped { get; set; }

    public string[] DeclarationDriftWarnings { get; set; }

    public string[] ParseErrors { get; set; }

    public WorkerUnchangedMethod[] UnchangedMethods { get; set; }

    public bool BaselineDisabledByDuplicateKeys { get; set; }

    public WorkerRemovedMember[] RemovedMembers { get; set; }

    public WorkerRemovedMethodSignature[] RemovedMethodSignatures { get; set; }

    public bool HasAccessorDelegates { get; set; }

    // True when shim bodies rewrite added-field accesses to HotReloadAddedFieldStore.
    // Keep in sync with TransformWorkerOutputDto.hasAddedFieldRewrites.
    public bool HasAddedFieldRewrites { get; set; }
}

internal sealed class WorkerRemovedMember
{
    public string Kind { get; set; }

    public string Name { get; set; }
}

internal sealed class WorkerRemovedMethodSignature
{
    public string TypeMetadataName { get; set; }

    public string MethodName { get; set; }

    public string[] ParameterTypeFullNames { get; set; }

    public int GenericArity { get; set; }
}

internal enum CompiledMethodMatch
{
    NotFound,
    Matched,
    ReturnTypeChanged
}

internal enum CompiledFieldMatch
{
    NotFound,
    Matched,
    FieldTypeChanged,
    FieldModifiersChanged,
    MemberKindChanged
}

internal sealed class WorkerUnchangedMethod
{
    public string TypeMetadataName { get; set; }

    public string MethodName { get; set; }

    public string[] ParameterTypeFullNames { get; set; }

    public int GenericArity { get; set; }
}

internal sealed class WorkerEntry
{
    public string TypeMetadataName { get; set; }

    public string MethodName { get; set; }

    public string[] ParameterTypeFullNames { get; set; }

    public int GenericArity { get; set; }

    public string ShimTypeName { get; set; }

    public string ShimMethodName { get; set; }

    // "transplant" | "delegation" | "addedMethod" — see PatchKinds.
    public string PatchKind { get; set; }

    // Method keys of added methods this entry's body invokes. Empty when none.
    public string[] CalledAddedMethodKeys { get; set; }

    // 1-based, both ends inclusive, within the original edited source file.
    public int SourceStartLine { get; set; }

    public int SourceEndLine { get; set; }

    // Null when the method is not a one-shot lifecycle method and is not only called from them.
    public string LifecycleNote { get; set; }

    public bool ReplacesCompiledMethod { get; set; }
}

internal static class LifecycleNotes
{
    public static readonly string[] OneShotLifecycleMethodNames =
    {
        "Awake",
        "Start",
        "OnEnable",
        "OnDisable",
        "OnDestroy"
    };

    public const string DirectFormat =
        "{0} is a one-shot lifecycle method; objects that already ran it will not run the "
        + "patched body. It takes effect only for newly created objects.";
}

internal static class PatchKinds
{
    public const string Transplant = "transplant";
    public const string Delegation = "delegation";

    // Keep in sync with HotReloadConstants.PatchKindAddedMethod.
    public const string AddedMethod = "addedMethod";
}

internal static class RemovedMemberKinds
{
    // Keep in sync with HotReloadConstants.RemovedMemberKindMethod / RemovedMemberKindField.
    public const string Method = "method";

    public const string Field = "field";
}

internal sealed class MethodTransformDecision
{
    public string SkipReason { get; private set; }

    public string PatchKind { get; private set; }

    public bool UsesDelegation { get; private set; }

    public static MethodTransformDecision Skip(string reason)
    {
        return new MethodTransformDecision { SkipReason = reason };
    }

    public static MethodTransformDecision Transplant()
    {
        return new MethodTransformDecision { PatchKind = PatchKinds.Transplant };
    }

    public static MethodTransformDecision Delegation()
    {
        return new MethodTransformDecision
        {
            PatchKind = PatchKinds.Delegation,
            UsesDelegation = true
        };
    }

    public static MethodTransformDecision AddedMethod(bool usesDelegation)
    {
        return new MethodTransformDecision
        {
            PatchKind = PatchKinds.AddedMethod,
            UsesDelegation = usesDelegation
        };
    }
}

internal sealed class WorkerSkipped
{
    public string Method { get; set; }

    public string Reason { get; set; }
}
