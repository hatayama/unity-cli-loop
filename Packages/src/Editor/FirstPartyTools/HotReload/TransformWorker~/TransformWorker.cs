// Hot-reload transform worker: parse + semantic analysis of one edited C# file, emit static
// shim method sources (no Prefix wrappers) plus a per-method manifest / skip list.
// Runs out-of-process on the Unity-bundled .NET host against the Unity-bundled Roslyn.
// Generated shims mirror user method signatures verbatim; repo style rules apply to
// hand-written code only.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        return input;
    }

    private static void WriteOutput(string outputJsonPath, WorkerOutput output)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(output, JsonOptions);
        File.WriteAllBytes(outputJsonPath, bytes);
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

        List<MetadataReference> references = new List<MetadataReference>();
        foreach (string referencePath in input.ReferencePaths)
        {
            if (File.Exists(referencePath))
            {
                references.Add(MetadataReference.CreateFromFile(referencePath));
            }
            else
            {
                parseErrors.Add("Reference not found: " + referencePath);
            }
        }

        if (!string.IsNullOrEmpty(input.TargetTypesAssemblyPath) && File.Exists(input.TargetTypesAssemblyPath))
        {
            bool alreadyListed = false;
            foreach (string referencePath in input.ReferencePaths)
            {
                if (string.Equals(
                        Path.GetFullPath(referencePath),
                        Path.GetFullPath(input.TargetTypesAssemblyPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    alreadyListed = true;
                    break;
                }
            }

            if (!alreadyListed)
            {
                references.Add(MetadataReference.CreateFromFile(input.TargetTypesAssemblyPath));
            }
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "UloopHotReloadTransformWorkerCompilation",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
        CompilationUnitSyntax root = syntaxTree.GetCompilationUnitRoot();

        List<WorkerEntry> entries = new List<WorkerEntry>();
        List<WorkerSkipped> skipped = new List<WorkerSkipped>();
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

                string skipReason = EvaluateSkipReason(
                    typeDeclaration,
                    typeSymbol,
                    methodDeclaration,
                    methodSymbol,
                    semanticModel);
                if (skipReason != null)
                {
                    skipped.Add(new WorkerSkipped
                    {
                        Method = FormatMethodLabel(typeSymbol, methodSymbol),
                        Reason = skipReason
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

                MethodDeclarationSyntax rewrittenMethod = RewriteMethodBody(
                    methodDeclaration,
                    methodSymbol,
                    typeSymbol,
                    semanticModel);
                currentShimType.AddMethod(rewrittenMethod, shimMethodName);

                entries.Add(new WorkerEntry
                {
                    TypeMetadataName = CecilTypeNames.ToMetadataName(typeSymbol),
                    MethodName = methodSymbol.Name,
                    ParameterTypeFullNames = methodSymbol.Parameters
                        .Select(CecilTypeNames.ToParameterTypeFullName)
                        .ToArray(),
                    ShimTypeName = currentShimType.ShimTypeName,
                    ShimMethodName = shimMethodName
                });
            }
        }

        string shimSource = ShimSourceEmitter.Emit(root, shimTypes);
        return new WorkerOutput
        {
            ShimSource = shimSource,
            Entries = entries.ToArray(),
            Skipped = skipped.ToArray(),
            ParseErrors = parseErrors.ToArray()
        };
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

    private static string EvaluateSkipReason(
        TypeDeclarationSyntax typeDeclaration,
        INamedTypeSymbol typeSymbol,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel)
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

        SyntaxNode bodyNode = (SyntaxNode)methodDeclaration.Body ?? methodDeclaration.ExpressionBody;
        if (bodyNode == null)
        {
            return "Methods without a body (abstract/extern) are skipped.";
        }

        if (ContainsBaseExpression(bodyNode))
        {
            return "Methods that call base. members are skipped; C# cannot express base calls outside the type.";
        }

        if (SubtreeHasInaccessibleMemberAccess(semanticModel, FindLambdaAndLocalFunctionBodies(bodyNode)))
        {
            return "Lambda or local-function bodies that access private/internal members are skipped in v1 "
                + "(closure methods JIT-compile normally and fail accessibility checks).";
        }

        if (IsAsyncOrIterator(methodDeclaration, bodyNode)
            && SubtreeHasInaccessibleMemberAccess(semanticModel, new[] { bodyNode }))
        {
            return "Async or iterator methods whose bodies access private/internal members are skipped in v1 "
                + "(state-machine MoveNext JIT-compiles normally and fails accessibility checks).";
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

        return bodyNode.DescendantNodes().OfType<YieldStatementSyntax>().Any();
    }

    private static List<SyntaxNode> FindLambdaAndLocalFunctionBodies(SyntaxNode bodyNode)
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
                if (node is not IdentifierNameSyntax && node is not GenericNameSyntax)
                {
                    continue;
                }

                ISymbol symbol = semanticModel.GetSymbolInfo(node).Symbol;
                if (symbol != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static MethodDeclarationSyntax RewriteMethodBody(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol targetType,
        SemanticModel semanticModel)
    {
        InstanceMemberQualifier qualifier = new InstanceMemberQualifier(semanticModel, targetType);
        MethodDeclarationSyntax rewritten = (MethodDeclarationSyntax)qualifier.Visit(methodDeclaration);
        return ShimMethodFactory.ToShimMethod(rewritten, methodSymbol);
    }

    private static string FormatMethodLabel(INamedTypeSymbol typeSymbol, IMethodSymbol methodSymbol)
    {
        return typeSymbol.ToDisplayString(FullyQualifiedTypeFormat) + "." + methodSymbol.Name;
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

            if (symbol.ContainingType != null
                && HasInaccessibleAccessibility(symbol.ContainingType.DeclaredAccessibility))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasInaccessibleAccessibility(Accessibility accessibility)
    {
        return accessibility == Accessibility.Private
            || accessibility == Accessibility.Internal
            || accessibility == Accessibility.Protected
            || accessibility == Accessibility.ProtectedAndInternal
            || accessibility == Accessibility.ProtectedOrInternal;
    }
}

/// <summary>
/// Qualifies bare instance/static member references so a static shim can host the original body.
/// </summary>
internal sealed class InstanceMemberQualifier : CSharpSyntaxRewriter
{
    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol _targetType;

    public InstanceMemberQualifier(SemanticModel semanticModel, INamedTypeSymbol targetType)
    {
        _semanticModel = semanticModel;
        _targetType = targetType;
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

        if (!IsOwnedMember(symbol, out bool isStatic, out INamedTypeSymbol containingType))
        {
            return original;
        }

        if (isStatic)
        {
            TypeSyntax typeSyntax = SyntaxFactory.ParseTypeName(
                containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
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

    private bool IsOwnedMember(ISymbol symbol, out bool isStatic, out INamedTypeSymbol containingType)
    {
        isStatic = false;
        containingType = null;

        if (symbol is IMethodSymbol methodSymbol)
        {
            // Extension methods live on unrelated static classes; leave them alone.
            if (methodSymbol.IsExtensionMethod)
            {
                return false;
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
            return false;
        }

        if (containingType == null)
        {
            return false;
        }

        return IsInInheritanceHierarchy(_targetType, containingType);
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
    public const string InstanceParameterName = "instance";
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
    }

    public string ShimTypeName { get; }

    public string NamespaceName { get; }

    public List<UsingDirectiveSyntax> Usings { get; }

    public void AddMethod(MethodDeclarationSyntax shimMethod, string shimMethodName)
    {
        MethodDeclarationSyntax named = shimMethod.WithIdentifier(SyntaxFactory.Identifier(shimMethodName));
        _methods.Add(named);
    }

    public IReadOnlyList<MethodDeclarationSyntax> Methods => _methods;
}

internal static class ShimSourceEmitter
{
    public static string Emit(CompilationUnitSyntax originalRoot, List<ShimTypeBuilder> shimTypes)
    {
        if (shimTypes.Count == 0)
        {
            return string.Empty;
        }

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
                .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(shimType.Methods));

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

        unit = unit.NormalizeWhitespace();

        StringBuilder builder = new StringBuilder();
        builder.AppendLine(
            "// Generated shims mirror user method signatures verbatim; repo style rules apply to hand-written code only.");
        builder.Append(unit.ToFullString());
        return builder.ToString();
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

            return elementName + "[" + new string(',', arrayType.Rank - 1) + "]";
        }

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            if (namedType.IsGenericType && !namedType.IsUnboundGenericType)
            {
                string head = ToMetadataName(namedType.OriginalDefinition);
                string args = string.Join(",", namedType.TypeArguments.Select(ToCecilFullName));
                return head + "<" + args + ">";
            }

            return ToMetadataName(namedType);
        }

        return typeSymbol.ToDisplayString(
            new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));
    }
}

internal sealed class WorkerInput
{
    public string SourcePath { get; set; }

    public string[] Defines { get; set; }

    public string[] ReferencePaths { get; set; }

    public string TargetTypesAssemblyPath { get; set; }
}

internal sealed class WorkerOutput
{
    public string ShimSource { get; set; }

    public WorkerEntry[] Entries { get; set; }

    public WorkerSkipped[] Skipped { get; set; }

    public string[] ParseErrors { get; set; }
}

internal sealed class WorkerEntry
{
    public string TypeMetadataName { get; set; }

    public string MethodName { get; set; }

    public string[] ParameterTypeFullNames { get; set; }

    public string ShimTypeName { get; set; }

    public string ShimMethodName { get; set; }
}

internal sealed class WorkerSkipped
{
    public string Method { get; set; }

    public string Reason { get; set; }
}
