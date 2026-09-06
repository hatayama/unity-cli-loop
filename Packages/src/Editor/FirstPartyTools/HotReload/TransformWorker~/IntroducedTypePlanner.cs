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

// Identifies newly declared top-level type definitions without mixing parse-failed files into
// semantic analysis. Binding/rewriting callers intentionally remains a later-stage concern.
internal static class IntroducedTypePlanner
{
    internal static void Plan(
        WorkerSourceUnit unit,
        IAssemblySymbol targetAssembly,
        string targetAssemblyName,
        string targetAssemblyMvid,
        IntroducedTypeArtifactMap artifactMap,
        IReadOnlyList<string> defineSymbols,
        IReadOnlyList<UsingDirectiveSyntax> assemblyGlobalUsings)
    {
        foreach (BaseTypeDeclarationSyntax declaration in unit.Root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            INamedTypeSymbol typeSymbol = unit.SemanticModel.GetDeclaredSymbol(declaration);
            if (typeSymbol == null)
            {
                unit.IntroducedTypeDiagnostics.Add("Could not resolve a declared type symbol.");
                continue;
            }

            if (CompiledMemberMatcher.FindCompiledType(typeSymbol, targetAssembly) != null)
            {
                continue;
            }

            if (typeSymbol.ContainingType != null)
            {
                unit.IntroducedTypeDiagnostics.Add(
                    "Nested type requires a compile: " + CecilTypeNames.ToMetadataName(typeSymbol));
                continue;
            }

            // Nested declarations are excluded unconditionally, so an outer type that contains one
            // has to be refused as well. Emitting it would either drop the nested implementation
            // its members rely on or retain a type this stage cannot manage the lifetime of.
            if (TryFindNestedDeclaration(declaration, out string nestedName))
            {
                unit.IntroducedTypeDiagnostics.Add(
                    "Nested declaration inside an introduced type requires a compile: "
                    + CecilTypeNames.ToMetadataName(typeSymbol) + "/" + nestedName);
                continue;
            }

            if (!IsSupported(typeSymbol, declaration, unit.SemanticModel, out string reason))
            {
                unit.IntroducedTypeDiagnostics.Add(reason + ": " + CecilTypeNames.ToMetadataName(typeSymbol));
                continue;
            }

            string changedConst = IntroducedTypeConstDriftDetector.FindChangedReferencedConst(
                declaration, unit.SemanticModel, targetAssembly);
            if (changedConst != null)
            {
                unit.IntroducedTypeDiagnostics.Add(
                    "Changed const requires a compile: " + changedConst
                    + " referenced by " + CecilTypeNames.ToMetadataName(typeSymbol));
                continue;
            }

            unit.IntroducedTypes.Add(
                new WorkerIntroducedType
                {
                    OriginalAssemblyName = targetAssemblyName ?? string.Empty,
                    OriginalAssemblyMvid = targetAssemblyMvid ?? string.Empty,
                    MetadataName = CecilTypeNames.ToMetadataName(typeSymbol),
                    OwnerProjectRelativePath = unit.Input.ProjectRelativePath,
                    DeclarationFingerprint = ComputeFingerprint(
                        unit.Root,
                        declaration,
                        defineSymbols,
                        typeSymbol,
                        unit.SemanticModel,
                        targetAssembly,
                        targetAssemblyName,
                        targetAssemblyMvid,
                        artifactMap),
                    Source = BuildTypeSource(unit.Root, typeSymbol, declaration, assemblyGlobalUsings)
                });
        }

        foreach (DelegateDeclarationSyntax declaration in unit.Root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
        {
            INamedTypeSymbol delegateSymbol = unit.SemanticModel.GetDeclaredSymbol(declaration);
            if (delegateSymbol == null || CompiledMemberMatcher.FindCompiledType(delegateSymbol, targetAssembly) != null)
            {
                continue;
            }

            unit.IntroducedTypeDiagnostics.Add(
                "Delegate introduced type requires a compile: " + CecilTypeNames.ToMetadataName(delegateSymbol));
        }
    }

    private static bool TryFindNestedDeclaration(BaseTypeDeclarationSyntax declaration, out string nestedName)
    {
        foreach (SyntaxNode node in declaration.DescendantNodes())
        {
            if (node is BaseTypeDeclarationSyntax nestedType)
            {
                nestedName = nestedType.Identifier.Text;
                return true;
            }

            if (node is DelegateDeclarationSyntax nestedDelegate)
            {
                nestedName = nestedDelegate.Identifier.Text;
                return true;
            }
        }

        nestedName = string.Empty;
        return false;
    }

    private static bool IsSupported(
        INamedTypeSymbol typeSymbol,
        BaseTypeDeclarationSyntax declaration,
        SemanticModel semanticModel,
        out string reason)
    {
        if (typeSymbol.Arity != 0)
        {
            reason = "Generic introduced type requires a compile";
            return false;
        }

        if (declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            reason = "Partial introduced type requires a compile";
            return false;
        }

        if (declaration is RecordDeclarationSyntax)
        {
            reason = "Record introduced type requires a compile";
            return false;
        }

        if (typeSymbol.DeclaredAccessibility != Accessibility.Public)
        {
            reason = "Non-public introduced type requires a compile";
            return false;
        }

        if (typeSymbol.IsRefLikeType)
        {
            reason = "Ref-like introduced type requires a compile";
            return false;
        }

        if (ContainsUnsafeCode(declaration))
        {
            reason = "Unsafe introduced type requires a compile";
            return false;
        }

        if (InheritsUnityObject(typeSymbol))
        {
            reason = "Unity object introduced type requires a compile";
            return false;
        }

        if (HasSerializableAttribute(typeSymbol))
        {
            reason = "Serializable introduced type requires a compile";
            return false;
        }

        if (HasModuleInitializer(declaration, semanticModel))
        {
            reason = "Module initializer introduced type requires a compile";
            return false;
        }

        if (typeSymbol.TypeKind != TypeKind.Class
            && typeSymbol.TypeKind != TypeKind.Struct
            && typeSymbol.TypeKind != TypeKind.Enum
            && typeSymbol.TypeKind != TypeKind.Interface)
        {
            reason = "Unsupported introduced type requires a compile";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static string BuildTypeSource(
        CompilationUnitSyntax root,
        INamedTypeSymbol typeSymbol,
        BaseTypeDeclarationSyntax declaration,
        IReadOnlyList<UsingDirectiveSyntax> assemblyGlobalUsings)
    {
        StringBuilder builder = new StringBuilder();
        foreach (ExternAliasDirectiveSyntax externAlias in root.Externs)
        {
            builder.Append(externAlias.ToFullString());
        }

        List<BaseNamespaceDeclarationSyntax> namespaceDeclarations = declaration.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .ToList();
        if (namespaceDeclarations.Count > 0)
        {
            AppendRootUsings(builder, root, assemblyGlobalUsings);
            foreach (BaseNamespaceDeclarationSyntax namespaceDeclaration in namespaceDeclarations)
            {
                builder.Append("namespace ");
                builder.Append(namespaceDeclaration.Name.ToString());
                builder.AppendLine();
                builder.AppendLine("{");
                foreach (UsingDirectiveSyntax usingDirective in namespaceDeclaration.Usings)
                {
                    builder.Append(usingDirective.ToFullString());
                }
            }

            builder.Append(declaration.ToFullString());
            for (int index = 0; index < namespaceDeclarations.Count; index++)
            {
                builder.AppendLine("}");
            }
            return builder.ToString();
        }

        AppendRootUsings(builder, root, assemblyGlobalUsings);
        builder.Append(declaration.ToFullString());
        return builder.ToString();
    }

    private static void AppendRootUsings(
        StringBuilder builder,
        CompilationUnitSyntax root,
        IReadOnlyList<UsingDirectiveSyntax> assemblyGlobalUsings)
    {
        foreach (UsingDirectiveSyntax usingDirective in root.Usings)
        {
            builder.Append(usingDirective.WithGlobalKeyword(default).ToFullString());
        }

        foreach (UsingDirectiveSyntax assemblyGlobalUsing in assemblyGlobalUsings)
        {
            if (WorkerUsingCollector.ContainsEquivalentUsing(root.Usings.ToList(), assemblyGlobalUsing))
            {
                continue;
            }

            builder.Append(assemblyGlobalUsing.ToFullString());
        }
    }

    private static bool ContainsUnsafeCode(BaseTypeDeclarationSyntax declaration)
    {
        return declaration.DescendantTokens().Any(token => token.IsKind(SyntaxKind.UnsafeKeyword));
    }

    private static bool InheritsUnityObject(INamedTypeSymbol typeSymbol)
    {
        INamedTypeSymbol current = typeSymbol;
        while (current != null)
        {
            if (current.ToDisplayString() == "UnityEngine.Object")
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool HasSerializableAttribute(INamedTypeSymbol typeSymbol)
    {
        foreach (AttributeData attribute in typeSymbol.GetAttributes())
        {
            if (attribute.AttributeClass != null
                && attribute.AttributeClass.ToDisplayString() == "System.SerializableAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasModuleInitializer(
        BaseTypeDeclarationSyntax declaration,
        SemanticModel semanticModel)
    {
        if (declaration is not TypeDeclarationSyntax typeDeclaration)
        {
            return false;
        }

        foreach (MethodDeclarationSyntax method in typeDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(method);
            if (methodSymbol == null)
            {
                continue;
            }

            foreach (AttributeData attribute in methodSymbol.GetAttributes())
            {
                if (attribute.AttributeClass != null
                    && attribute.AttributeClass.ToDisplayString()
                        == "System.Runtime.CompilerServices.ModuleInitializerAttribute")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string ComputeFingerprint(
        CompilationUnitSyntax root,
        BaseTypeDeclarationSyntax declaration,
        IReadOnlyList<string> defineSymbols,
        INamedTypeSymbol typeSymbol,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly,
        string targetAssemblyName,
        string targetAssemblyMvid,
        IntroducedTypeArtifactMap artifactMap)
    {
        StringBuilder input = new StringBuilder();
        AppendTokens(input, declaration.DescendantTokens());

        foreach (string defineSymbol in defineSymbols.OrderBy(symbol => symbol, StringComparer.Ordinal))
        {
            AppendValue(input, defineSymbol);
        }

        foreach (string dependencyIdentity in CollectDependencyIdentities(
            declaration,
            typeSymbol,
            semanticModel,
            targetAssembly,
            targetAssemblyName,
            targetAssemblyMvid,
            artifactMap))
        {
            AppendValue(input, dependencyIdentity);
        }

        byte[] sourceBytes = Encoding.UTF8.GetBytes(input.ToString());
        using SHA256 hash = SHA256.Create();
        byte[] bytes = hash.ComputeHash(sourceBytes);
        StringBuilder builder = new StringBuilder(bytes.Length * 2);
        for (int index = 0; index < bytes.Length; index++)
        {
            builder.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void AppendTokens(StringBuilder builder, IEnumerable<SyntaxToken> tokens)
    {
        foreach (SyntaxToken token in tokens)
        {
            builder.Append(token.RawKind.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            AppendValue(builder, token.Text);
        }
    }

    private static void AppendValue(StringBuilder builder, string value)
    {
        string safeValue = value ?? string.Empty;
        builder.Append(safeValue.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(safeValue);
        builder.Append('\n');
    }

    // Records each dependency against the ordinal position of the declaration node that binds it.
    // An unordered set cannot tell two aliases apart when they exchange the types they bind to:
    // the tokens are identical and the set of referenced types is the same, so the fingerprint
    // would stay equal while the definition changed. The position is a traversal ordinal rather
    // than an absolute span, so it survives trivia edits and unrelated using directives.
    private static IReadOnlyList<string> CollectDependencyIdentities(
        BaseTypeDeclarationSyntax declaration,
        INamedTypeSymbol typeSymbol,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly,
        string targetAssemblyName,
        string targetAssemblyMvid,
        IntroducedTypeArtifactMap artifactMap)
    {
        List<string> positionedDependencies = new List<string>();
        HashSet<string> declaringDependency = new HashSet<string>(StringComparer.Ordinal);
        AddDependency(typeSymbol, semanticModel, targetAssembly, targetAssemblyName, targetAssemblyMvid, artifactMap, declaringDependency);
        foreach (string identity in declaringDependency.OrderBy(identity => identity, StringComparer.Ordinal))
        {
            positionedDependencies.Add("self|" + identity);
        }

        int position = 0;
        foreach (SyntaxNode node in declaration.DescendantNodesAndSelf())
        {
            HashSet<string> nodeDependencies = new HashSet<string>(StringComparer.Ordinal);
            SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(node);
            AddDependency(symbolInfo.Symbol, semanticModel, targetAssembly, targetAssemblyName, targetAssemblyMvid, artifactMap, nodeDependencies);
            foreach (ISymbol candidate in symbolInfo.CandidateSymbols)
            {
                AddDependency(candidate, semanticModel, targetAssembly, targetAssemblyName, targetAssemblyMvid, artifactMap, nodeDependencies);
            }

            TypeInfo typeInfo = semanticModel.GetTypeInfo(node);
            AddDependency(typeInfo.Type, semanticModel, targetAssembly, targetAssemblyName, targetAssemblyMvid, artifactMap, nodeDependencies);
            AddDependency(typeInfo.ConvertedType, semanticModel, targetAssembly, targetAssemblyName, targetAssemblyMvid, artifactMap, nodeDependencies);
            foreach (string identity in nodeDependencies.OrderBy(identity => identity, StringComparer.Ordinal))
            {
                positionedDependencies.Add(position.ToString(CultureInfo.InvariantCulture) + "|" + identity);
            }

            position++;
        }

        return positionedDependencies;
    }

    private static void AddDependency(
        ISymbol symbol,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly,
        string targetAssemblyName,
        string targetAssemblyMvid,
        IntroducedTypeArtifactMap artifactMap,
        HashSet<string> dependencies)
    {
        if (symbol == null)
        {
            return;
        }

        INamedTypeSymbol namedType = symbol as INamedTypeSymbol;
        if (symbol is IMethodSymbol methodSymbol)
        {
            namedType = methodSymbol.ContainingType;
        }
        else if (symbol is IPropertySymbol propertySymbol)
        {
            namedType = propertySymbol.ContainingType;
        }
        else if (symbol is IFieldSymbol fieldSymbol)
        {
            namedType = fieldSymbol.ContainingType;
        }

        if (namedType == null)
        {
            return;
        }

        IAssemblySymbol containingAssembly = namedType.ContainingAssembly;
        if (containingAssembly == null)
        {
            return;
        }

        string metadataName = CecilTypeNames.ToMetadataName(namedType.OriginalDefinition);

        // A type that already lives in a retained artifact is the same definition it was when it
        // was still a source declaration, so it has to fingerprint as the assembly its source
        // belongs to. Binding through the artifact assembly identity instead would invalidate
        // every dependent declaration the moment the type it depends on is introduced.
        string normalizedIdentity = artifactMap.FindNormalizedIdentity(containingAssembly, metadataName);
        if (normalizedIdentity != null)
        {
            dependencies.Add(normalizedIdentity);
            return;
        }

        if (SymbolEqualityComparer.Default.Equals(containingAssembly, semanticModel.Compilation.Assembly)
            || SymbolEqualityComparer.Default.Equals(containingAssembly, targetAssembly))
        {
            dependencies.Add((targetAssemblyName ?? string.Empty)
                + "|" + (targetAssemblyMvid ?? string.Empty)
                + "|" + metadataName);
            return;
        }

        dependencies.Add(containingAssembly.Identity.GetDisplayName() + "|" + metadataName);
    }
}
