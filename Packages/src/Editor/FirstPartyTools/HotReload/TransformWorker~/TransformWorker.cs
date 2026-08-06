// Hot-reload transform worker: parse + semantic analysis of one edited C# file, emit static
// shim method sources (no Prefix wrappers) plus a per-method manifest / skip list.
// Runs out-of-process on the Unity-bundled .NET host against the Unity-bundled Roslyn.
// Generated shims mirror user method signatures verbatim; repo style rules apply to
// hand-written code only.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
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
        return input;
    }

    private static void WriteOutput(string outputJsonPath, WorkerOutput output)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(output, JsonOptions);
        File.WriteAllBytes(outputJsonPath, bytes);
    }

    // Keep in sync with HotReloadOrchestrator.BuildMethodKey (Unity package side).
    private static string BuildMethodKey(string typeMetadataName, string methodName, string[] parameterTypeFullNames)
    {
        return typeMetadataName + "::" + methodName + "(" + string.Join(",", parameterTypeFullNames ?? Array.Empty<string>()) + ")";
    }

    private static WorkerOutput TransformFile(WorkerInput input)
    {
        List<string> parseErrors = new List<string>();
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

        // Why before CSharpCompilation.Create: annotating after GetSemanticModel detaches nodes
        // from the bound tree and ShimBodyRewriter's GetSymbolInfo throws "Syntax node is not
        // within syntax tree". Binding the SemanticModel to the annotated tree keeps rewriter
        // lookups valid while uloop-line annotations ride through to Emit.
        CompilationUnitSyntax annotatedRoot = AnnotateOriginalSourceLines(syntaxTree.GetCompilationUnitRoot());
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
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap = null;
        Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap = null;
        Dictionary<string, IndexerDeclarationSyntax> snapshotIndexerMap = null;
        // Null disables comparison; empty string is a real (empty) baseline text.
        if (input.SnapshotSource != null)
        {
            CompilationUnitSyntax snapshotRoot = CSharpSyntaxTree.ParseText(
                    SourceText.From(input.SnapshotSource, Encoding.UTF8),
                    parseOptions)
                .GetCompilationUnitRoot();
            Dictionary<string, MethodDeclarationSyntax> snapMethods = BuildSyntaxMethodMapOrNull(snapshotRoot);
            Dictionary<string, MethodDeclarationSyntax> currentMethods = BuildSyntaxMethodMapOrNull(root);
            if (snapMethods != null && currentMethods != null)
            {
                // Why both maps: a duplicate key on either side makes AreEquivalent matching
                // ambiguous, so fail closed to no-baseline (patch all) instead of guessing.
                hasBaseline = true;
                snapshotMethodMap = snapMethods;
                // Why null is kept as-is: a colliding property/indexer key only disables accessor
                // gating for this file; method-level baseline matching still applies.
                snapshotPropertyMap = BuildSyntaxPropertyMapOrNull(snapshotRoot);
                snapshotIndexerMap = BuildSyntaxIndexerMapOrNull(snapshotRoot);
                AppendOutsideMethodBodyDriftWarningIfNeeded(
                    snapshotRoot,
                    root,
                    Path.GetFileName(input.SourcePath),
                    declarationDriftWarnings);
            }
        }

        List<WorkerEntry> entries = new List<WorkerEntry>();
        List<WorkerSkipped> skipped = new List<WorkerSkipped>();
        List<WorkerUnchangedMethod> unchangedMethods = new List<WorkerUnchangedMethod>();
        List<ShimTypeBuilder> shimTypes = new List<ShimTypeBuilder>();
        int globalShimMethodCounter = 0;
        int shimTypeCounter = 0;

        foreach (TypeDeclarationSyntax typeDeclaration in EnumerateTypeDeclarations(root))
        {
            INamedTypeSymbol typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration);
            if (typeSymbol == null)
            {
                continue;
            }

            string typeMetadataNameFromSyntax = BuildTypeMetadataNameFromSyntax(typeDeclaration);

            // Accessors are never patched in v1; report each explicit-body accessor as Skipped
            // so an edited getter/setter never disappears from the response silently.
            AppendExplicitAccessorSkips(
                typeDeclaration,
                typeMetadataNameFromSyntax,
                semanticModel,
                skipped,
                hasBaseline ? snapshotPropertyMap : null,
                hasBaseline ? snapshotIndexerMap : null);

            List<MethodDeclarationSyntax> methods = typeDeclaration.Members
                .OfType<MethodDeclarationSyntax>()
                .ToList();
            if (methods.Count == 0)
            {
                continue;
            }

            ShimTypeBuilder currentShimType = null;
            foreach (MethodDeclarationSyntax methodDeclaration in methods)
            {
                IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);
                if (methodSymbol == null)
                {
                    continue;
                }

                string[] parameterTypeFullNames = methodSymbol.Parameters
                    .Select(CecilTypeNames.ToParameterTypeFullName)
                    .ToArray();

                // The orchestrator already reported this method as Failed from the first compile
                // round; re-emitting it would fail the retry compile again.
                string methodKey = BuildMethodKey(
                    CecilTypeNames.ToMetadataName(typeSymbol), methodSymbol.Name, parameterTypeFullNames);
                if (input.ExcludedMethodKeys.Contains(methodKey))
                {
                    continue;
                }

                // Why after ExcludedMethodKeys: exclusion means "already Failed on first round", so
                // it must win. Unchanged methods add no per-method signal — skipping them cuts
                // response noise and avoids pause-point collateral on untouched methods. Identities
                // are returned so the orchestrator can revert a leftover patch when source matches
                // the baseline again.
                if (hasBaseline)
                {
                    string syntaxMethodKey = BuildSyntaxMethodKey(typeMetadataNameFromSyntax, methodDeclaration);
                    if (snapshotMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax snapshotDecl)
                        && SyntaxFactory.AreEquivalent(snapshotDecl, methodDeclaration, topLevel: false))
                    {
                        unchangedMethods.Add(new WorkerUnchangedMethod
                        {
                            TypeMetadataName = CecilTypeNames.ToMetadataName(typeSymbol),
                            MethodName = methodSymbol.Name,
                            ParameterTypeFullNames = parameterTypeFullNames
                        });
                        continue;
                    }
                }

                MethodTransformDecision decision = DecideMethodTransform(
                    typeDeclaration,
                    typeSymbol,
                    methodDeclaration,
                    methodSymbol,
                    semanticModel);
                if (decision.SkipReason != null)
                {
                    skipped.Add(new WorkerSkipped
                    {
                        Method = FormatMethodLabel(methodSymbol),
                        Reason = decision.SkipReason
                    });
                    continue;
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
                        CollectUsingsForType(root, typeDeclaration));
                    shimTypes.Add(currentShimType);
                }

                string shimMethodName = methodSymbol.Name + "__shim" + globalShimMethodCounter;
                globalShimMethodCounter++;

                FileLinePositionSpan originalSpan = methodDeclaration.GetLocation().GetLineSpan();
                int sourceStartLine = originalSpan.StartLinePosition.Line + 1;
                int sourceEndLine = originalSpan.EndLinePosition.Line + 1;

                // Eligibility uses a disposable plan; the rewriter lazily GetOrAdd-s into the
                // shim-type-level plan so AllocateName stays unique across methods in the type.
                // methodDeclaration already carries uloop-line annotations from the parse-time pass.
                AccessorPlan rewritePlan = decision.UsesDelegation
                    ? currentShimType.AccessorPlan
                    : null;
                MethodDeclarationSyntax rewrittenMethod = RewriteMethodBody(
                    methodDeclaration,
                    methodSymbol,
                    typeSymbol,
                    semanticModel,
                    rewritePlan);
                currentShimType.AddMethod(rewrittenMethod, shimMethodName);

                entries.Add(new WorkerEntry
                {
                    TypeMetadataName = CecilTypeNames.ToMetadataName(typeSymbol),
                    MethodName = methodSymbol.Name,
                    ParameterTypeFullNames = parameterTypeFullNames,
                    ShimTypeName = currentShimType.ShimTypeName,
                    ShimMethodName = shimMethodName,
                    PatchKind = decision.PatchKind,
                    SourceStartLine = sourceStartLine,
                    SourceEndLine = sourceEndLine
                });
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
            UnchangedMethods = unchangedMethods.ToArray()
        };
    }

    /// <summary>
    /// Attaches original-source 1-based line annotations to every method and statement in the
    /// parsed tree. Must run before compilation so the SemanticModel binds the annotated tree.
    /// </summary>
    private static CompilationUnitSyntax AnnotateOriginalSourceLines(CompilationUnitSyntax root)
    {
        List<SyntaxNode> nodesToAnnotate = new List<SyntaxNode>();
        nodesToAnnotate.AddRange(root.DescendantNodes().OfType<MethodDeclarationSyntax>());
        nodesToAnnotate.AddRange(root.DescendantNodes().OfType<StatementSyntax>());
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
        "Property and indexer accessors are out of scope for v1; run 'uloop compile' to apply accessor edits.";

    private const string OutsideMethodBodyDriftWarningFormat =
        "Edits outside method bodies in {0} (fields, initializers, attributes, or added/removed members) are not applied by hot reload; run uloop compile to pick them up.";

    // Syntax-based method key for same-file snapshot vs current comparison. Do not mix with
    // BuildMethodKey (Cecil/metadata names used by the orchestrator exclusion path).
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

        return typeMetadataName + "::" + methodDeclaration.Identifier.Text + "("
            + string.Join(",", parameterKeys) + ")";
    }

    private static string BuildSyntaxParameterTypeKey(ParameterSyntax parameter)
    {
        string typeText = parameter.Type != null ? parameter.Type.ToString() : string.Empty;
        if (parameter.Modifiers.Any(SyntaxKind.RefKeyword)
            || parameter.Modifiers.Any(SyntaxKind.OutKeyword)
            || parameter.Modifiers.Any(SyntaxKind.InKeyword))
        {
            typeText += "&";
        }

        return typeText;
    }

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

    private static string GetContainingNamespaceName(SyntaxNode node)
    {
        SyntaxNode current = node.Parent;
        while (current != null)
        {
            if (current is NamespaceDeclarationSyntax namespaceDeclaration)
            {
                return namespaceDeclaration.Name.ToString();
            }

            if (current is FileScopedNamespaceDeclarationSyntax fileScopedNamespace)
            {
                return fileScopedNamespace.Name.ToString();
            }

            current = current.Parent;
        }

        return string.Empty;
    }

    private static string BuildSyntaxPropertyKey(
        string typeMetadataName,
        PropertyDeclarationSyntax propertyDeclaration)
    {
        string name = propertyDeclaration.Identifier.Text;
        if (propertyDeclaration.ExplicitInterfaceSpecifier != null)
        {
            name = propertyDeclaration.ExplicitInterfaceSpecifier.Name + "." + name;
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
        List<string> declarationDriftWarnings)
    {
        StripMethodBodiesRewriter rewriter = new StripMethodBodiesRewriter();
        SyntaxNode strippedSnapshot = rewriter.Visit(snapshotRoot);
        SyntaxNode strippedCurrent = rewriter.Visit(currentRoot);
        if (!SyntaxFactory.AreEquivalent(strippedSnapshot, strippedCurrent, topLevel: false))
        {
            declarationDriftWarnings.Add(
                string.Format(CultureInfo.InvariantCulture, OutsideMethodBodyDriftWarningFormat, fileName));
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
        Dictionary<string, IndexerDeclarationSyntax> snapshotIndexerMap)
    {
        foreach (MemberDeclarationSyntax member in typeDeclaration.Members)
        {
            if (member is PropertyDeclarationSyntax propertyDeclaration)
            {
                if (snapshotPropertyMap != null
                    && snapshotPropertyMap.TryGetValue(
                        BuildSyntaxPropertyKey(typeMetadataNameFromSyntax, propertyDeclaration),
                        out PropertyDeclarationSyntax snapshotProperty)
                    && SyntaxFactory.AreEquivalent(snapshotProperty, propertyDeclaration, topLevel: false))
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
                if (snapshotIndexerMap != null
                    && snapshotIndexerMap.TryGetValue(
                        BuildSyntaxIndexerKey(typeMetadataNameFromSyntax, indexerDeclaration),
                        out IndexerDeclarationSyntax snapshotIndexer)
                    && SyntaxFactory.AreEquivalent(snapshotIndexer, indexerDeclaration, topLevel: false))
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

        // Expression-bodied property/indexer (`=> expr`) is the getter body.
        bool hasExpressionBody =
            (propertyDeclaration is PropertyDeclarationSyntax propertyWithExpression
                && propertyWithExpression.ExpressionBody != null)
            || (propertyDeclaration is IndexerDeclarationSyntax indexerWithExpression
                && indexerWithExpression.ExpressionBody != null);
        if (hasExpressionBody)
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

        if (propertyDeclaration.AccessorList == null)
        {
            return;
        }

        foreach (AccessorDeclarationSyntax accessor in propertyDeclaration.AccessorList.Accessors)
        {
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

    private static IEnumerable<TypeDeclarationSyntax> EnumerateTypeDeclarations(CompilationUnitSyntax root)
    {
        return root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(static typeDeclaration =>
                typeDeclaration is ClassDeclarationSyntax
                || typeDeclaration is StructDeclarationSyntax
                || typeDeclaration is RecordDeclarationSyntax);
    }

    private static MethodTransformDecision DecideMethodTransform(
        TypeDeclarationSyntax typeDeclaration,
        INamedTypeSymbol typeSymbol,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
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

        SyntaxNode bodyNode = (SyntaxNode)methodDeclaration.Body ?? methodDeclaration.ExpressionBody;
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
                methodDeclaration,
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

        if (typeSymbol.IsGenericType || methodSymbol.IsGenericMethod
            || methodDeclaration.TypeParameterList != null)
        {
            return "Generic methods and methods inside generic types cannot be safely patched with Harmony.";
        }

        // Explicit interface implementations have dotted metadata names (e.g. IFoo.Bar) that are
        // not valid C# identifiers for shim method names; sanitizing would also desync the
        // matcher (Cecil MethodDefinition.Name). v1 skips them with an explicit reason.
        if (methodDeclaration.ExplicitInterfaceSpecifier != null)
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
        if (methodDeclaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.AsyncKeyword)))
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

    private static MethodDeclarationSyntax RewriteMethodBody(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol targetType,
        SemanticModel semanticModel,
        AccessorPlan accessorPlan)
    {
        // Why a single rewriter: rewriting the tree invalidates SemanticModel for new nodes.
        // Qualify + accessor rewrite both classify symbols on the original tree in one Visit pass.
        ShimBodyRewriter rewriter = new ShimBodyRewriter(semanticModel, targetType, accessorPlan);
        MethodDeclarationSyntax rewritten = (MethodDeclarationSyntax)rewriter.Visit(methodDeclaration);
        return ShimMethodFactory.ToShimMethod(rewritten, methodSymbol);
    }

    private static string FormatMethodLabel(IMethodSymbol methodSymbol)
    {
        return AccessorPlan.BuildMemberKey(methodSymbol);
    }

    private static List<UsingDirectiveSyntax> CollectUsingsForType(
        CompilationUnitSyntax root,
        TypeDeclarationSyntax typeDeclaration)
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

        return usings;
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
        if (!methodSymbol.IsStatic)
        {
            typeArguments.Add(TypeDisplay(methodSymbol.ContainingType));
        }

        foreach (IParameterSymbol parameter in methodSymbol.Parameters)
        {
            typeArguments.Add(TypeDisplay(parameter.Type));
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
        return DelegateFieldName + " = global::HarmonyLib.AccessTools.FieldRefAccess<"
            + TypeDisplay(FieldSymbol.ContainingType) + ", "
            + TypeDisplay(FieldSymbol.Type) + ">(\""
            + EscapeStringLiteral(FieldSymbol.Name) + "\");";
    }

    private string BuildMethodDelegateBindStatement(string metadataName, IMethodSymbol methodSymbol)
    {
        string declaringType = TypeDisplay(methodSymbol.ContainingType);
        string delegateType = BuildFuncOrActionType(methodSymbol);
        string typeArray = BuildTypeArrayLiteral(methodSymbol);
        // virtualCall must stay true for virtual/override/abstract instance members so a derived
        // override is dispatched; non-virtual private/internal targets keep false (exact method).
        bool virtualCall = !methodSymbol.IsStatic
            && (methodSymbol.IsVirtual || methodSymbol.IsOverride || methodSymbol.IsAbstract);
        string virtualCallLiteral = virtualCall ? "true" : "false";
        return DelegateFieldName + " = global::HarmonyLib.AccessTools.MethodDelegate<"
            + delegateType + ">(global::HarmonyLib.AccessTools.Method(typeof("
            + declaringType + "), \"" + EscapeStringLiteral(metadataName) + "\", "
            + typeArray + "), null, " + virtualCallLiteral + ", null);";
    }

    private static string BuildTypeArrayLiteral(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.Parameters.Length == 0)
        {
            return "new global::System.Type[] { }";
        }

        IEnumerable<string> typeofs = methodSymbol.Parameters.Select(
            parameter => "typeof(" + TypeDisplay(parameter.Type) + ")");
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
        MethodDeclarationSyntax methodDeclaration,
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

        if (!AreBodyTypeUsagesVisible(semanticModel, methodDeclaration, out rejectReason))
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
        MethodDeclarationSyntax methodDeclaration,
        out string rejectReason)
    {
        foreach (SyntaxNode node in methodDeclaration.DescendantNodesAndSelf())
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

            if (fieldSymbol.IsStatic)
            {
                rejectReason = "inaccessible static field access has no accessor rewrite shape (condition b).";
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

            if (fieldSymbol.IsStatic)
            {
                rejectReason = "inaccessible static field access has no accessor rewrite shape (condition b).";
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

    private static bool IsSupportedCompoundAssignmentKind(SyntaxKind kind)
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
    private static bool IsSideEffectFreeAssignmentReceiver(
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

    public ShimBodyRewriter(
        SemanticModel semanticModel,
        INamedTypeSymbol targetType,
        AccessorPlan accessorPlan)
    {
        _semanticModel = semanticModel;
        _targetType = targetType;
        _accessorPlan = accessorPlan;
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

    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (_accessorPlan == null
            || TransformWorkerProgram.NameofRules.IsNameofInvocation(node)
            || TransformWorkerProgram.NameofRules.IsInsideNameofArgument(node))
        {
            return base.VisitInvocationExpression(node);
        }

        ISymbol symbol = _semanticModel.GetSymbolInfo(node).Symbol;
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

    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        // Object/collection initializer member names must stay bare identifiers.
        if (node.Parent is InitializerExpressionSyntax)
        {
            return base.VisitAssignmentExpression(node);
        }

        if (_accessorPlan == null || TransformWorkerProgram.NameofRules.IsInsideNameofArgument(node))
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
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(fieldSymbol)
            && !fieldSymbol.IsStatic)
        {
            AccessorEntry entry = _accessorPlan.GetOrAddField(fieldSymbol);
            ExpressionSyntax receiver = ExtractReceiver(node.Left);
            ExpressionSyntax fieldRefCall = CreateDelegateInvocation(
                entry.DelegateFieldName,
                new[] { VisitReceiver(receiver) });
            return node
                .WithLeft(fieldRefCall)
                .WithRight((ExpressionSyntax)Visit(node.Right))
                .WithTriviaFrom(node);
        }

        return base.VisitAssignmentExpression(node);
    }

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (_accessorPlan == null || TransformWorkerProgram.NameofRules.IsInsideNameofArgument(node))
        {
            return base.VisitMemberAccessExpression(node);
        }

        ISymbol symbol = _semanticModel.GetSymbolInfo(node).Symbol
            ?? _semanticModel.GetSymbolInfo(node.Name).Symbol;

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
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(fieldSymbol)
            && !fieldSymbol.IsStatic)
        {
            AccessorEntry entry = _accessorPlan.GetOrAddField(fieldSymbol);
            return CreateDelegateInvocation(
                    entry.DelegateFieldName,
                    new[] { VisitReceiver(receiverSyntax) })
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
            .WithTriviaFrom(rewrittenOriginal);

        // Expression-bodied methods must keep their terminating semicolon; block bodies must not.
        return rewrittenOriginal.ExpressionBody != null
            ? shim.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            : shim.WithSemicolonToken(default);
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

        Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
        // Why assert shape: #line document names are embedded as C# string literals; backslashes
        // or quotes would break the directive or require escaping we deliberately do not support.
        Debug.Assert(
            projectRelativePath.IndexOf('\\') < 0 && projectRelativePath.IndexOf('"') < 0,
            "projectRelativePath must be forward-slash and quote-free.");

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

    private static string ToCecilFullName(ITypeSymbol typeSymbol)
    {
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

    // Verified snapshot text for edited-method detection. Null = no baseline, patch all methods.
    // Why pass text (not a path): avoids an IO race between orchestrator verification and worker
    // read that would crash the whole file under the no-try-catch policy.
    public string SnapshotSource { get; set; }

    // Project-relative forward-slash path embedded in #line document names.
    public string ProjectRelativePath { get; set; }
}

internal sealed class WorkerOutput
{
    public string ShimSource { get; set; }

    public WorkerEntry[] Entries { get; set; }

    public WorkerSkipped[] Skipped { get; set; }

    public string[] DeclarationDriftWarnings { get; set; }

    public string[] ParseErrors { get; set; }

    public WorkerUnchangedMethod[] UnchangedMethods { get; set; }
}

internal sealed class WorkerUnchangedMethod
{
    public string TypeMetadataName { get; set; }

    public string MethodName { get; set; }

    public string[] ParameterTypeFullNames { get; set; }
}

internal sealed class WorkerEntry
{
    public string TypeMetadataName { get; set; }

    public string MethodName { get; set; }

    public string[] ParameterTypeFullNames { get; set; }

    public string ShimTypeName { get; set; }

    public string ShimMethodName { get; set; }

    // "transplant" | "delegation" — see PatchKinds.
    public string PatchKind { get; set; }

    // 1-based, both ends inclusive, within the original edited source file.
    public int SourceStartLine { get; set; }

    public int SourceEndLine { get; set; }
}

internal static class PatchKinds
{
    public const string Transplant = "transplant";
    public const string Delegation = "delegation";
}

internal sealed class MethodTransformDecision
{
    public string SkipReason { get; private set; }

    public string PatchKind { get; private set; }

    public bool UsesDelegation => PatchKind == PatchKinds.Delegation;

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
        return new MethodTransformDecision { PatchKind = PatchKinds.Delegation };
    }
}

internal sealed class WorkerSkipped
{
    public string Method { get; set; }

    public string Reason { get; set; }
}
