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

            if (IsRefusedForNesting(unit, typeSymbol, declaration, targetAssembly))
            {
                continue;
            }

            if (!IsSupported(typeSymbol, declaration, unit.SemanticModel, out string reason))
            {
                unit.IntroducedTypeDiagnostics.Add(reason + ": " + CecilTypeNames.ToMetadataName(typeSymbol));
                continue;
            }

            if (IntroducedTypeConstDriftDetector.TryFindUnusableReferencedConst(
                    declaration,
                    unit.ConstDriftSemanticModel ?? unit.SemanticModel,
                    targetAssembly,
                    out string unusableConst,
                    out string unusableConstReason))
            {
                unit.IntroducedTypeDiagnostics.Add(
                    unusableConstReason + ": " + unusableConst
                    + " referenced by " + CecilTypeNames.ToMetadataName(typeSymbol));
                continue;
            }

            string metadataName = CecilTypeNames.ToMetadataName(typeSymbol);
            string declarationFingerprint = IntroducedTypeFingerprint.Compute(
                unit.Root,
                declaration,
                defineSymbols,
                typeSymbol,
                unit.SemanticModel,
                targetAssembly,
                targetAssemblyName,
                targetAssemblyMvid,
                artifactMap);
            if (IsAlreadyIntroduced(
                    unit,
                    artifactMap,
                    targetAssemblyName,
                    targetAssemblyMvid,
                    metadataName,
                    declarationFingerprint))
            {
                continue;
            }

            unit.IntroducedTypes.Add(
                new WorkerIntroducedType
                {
                    OriginalAssemblyName = targetAssemblyName ?? string.Empty,
                    OriginalAssemblyMvid = targetAssemblyMvid ?? string.Empty,
                    MetadataName = metadataName,
                    OwnerProjectRelativePath = unit.Input.ProjectRelativePath,
                    DeclarationFingerprint = declarationFingerprint,
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

    // Whether nesting alone settles this declaration, either because it is nested in a type the
    // assembly already holds or because it declares a nested type of its own. Both cases end the
    // declaration's planning, so the caller only has to know that it was handled.
    private static bool IsRefusedForNesting(
        WorkerSourceUnit unit,
        INamedTypeSymbol typeSymbol,
        BaseTypeDeclarationSyntax declaration,
        IAssemblySymbol targetAssembly)
    {
        if (typeSymbol.ContainingType != null)
        {
            // Why silent when the outer type is not compiled either: that outer declaration
            // is refused on its own, and its diagnostic already names this nested one as the
            // reason. Reporting both turns a single refusal into two lines about one type.
            if (CompiledMemberMatcher.FindCompiledType(typeSymbol.ContainingType, targetAssembly) == null)
            {
                return true;
            }

            unit.IntroducedTypeDiagnostics.Add(
                "Nested type requires a compile: " + CecilTypeNames.ToMetadataName(typeSymbol));
            return true;
        }

        // Nested declarations are excluded unconditionally, so an outer type that contains one
        // has to be refused as well. Emitting it would either drop the nested implementation
        // its members rely on or retain a type this stage cannot manage the lifetime of.
        if (TryFindNestedDeclaration(declaration, out string nestedName))
        {
            unit.IntroducedTypeDiagnostics.Add(
                "Nested declaration inside an introduced type requires a compile: "
                + CecilTypeNames.ToMetadataName(typeSymbol) + "/" + nestedName);
            return true;
        }

        return false;
    }

    // Whether this domain already retains an assembly for the declaration. Introducing it again
    // would compile a second artifact for the same original type, and the transform run would
    // then be offered two records normalizing to it, which it refuses. The unchanged declaration
    // binds from the active record instead, so this run introduces nothing.
    private static bool IsAlreadyIntroduced(
        WorkerSourceUnit unit,
        IntroducedTypeArtifactMap artifactMap,
        string targetAssemblyName,
        string targetAssemblyMvid,
        string metadataName,
        string declarationFingerprint)
    {
        string activeFingerprint = artifactMap.FindActiveDeclarationFingerprint(
            targetAssemblyName,
            targetAssemblyMvid,
            metadataName);
        if (activeFingerprint == null)
        {
            return false;
        }

        // Replacing the retained assembly a live type was loaded from is outside what a reload
        // can do, so a redefined introduced type is reported instead of being introduced again.
        if (!string.Equals(activeFingerprint, declarationFingerprint, StringComparison.Ordinal))
        {
            // The editor side recognises this text by its prefix and reads the type name back out
            // of the tail (HotReloadConstants.ChangedIntroducedTypeDiagnosticPrefix), so changing
            // the wording here means changing that constant in the same edit.
            unit.IntroducedTypeDiagnostics.Add(
                "Changed introduced type requires a compile: " + metadataName);
            return true;
        }

        // Why recorded: the run binds this declaration from the active artifact, and without a
        // record the reload could not tell that from a run that never saw the declaration.
        unit.IntroducedTypeReuses.Add(
            new WorkerIntroducedTypeReuse
            {
                MetadataName = metadataName,
                OriginalAssemblyName = targetAssemblyName ?? string.Empty,
                OriginalAssemblyMvid = targetAssemblyMvid ?? string.Empty
            });
        return true;
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
}
