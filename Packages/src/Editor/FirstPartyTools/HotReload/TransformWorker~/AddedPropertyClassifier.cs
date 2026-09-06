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

/// <summary>
/// Classifies properties missing from a compiled type before accessor skips and body emission.
/// </summary>
internal static class AddedPropertyClassifier
{
    internal static (int ShimTypeCounter, int GlobalShimMethodCounter) ClassifyAddedProperties(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        IAssemblySymbol targetTypesAssemblySymbol,
        WorkerInput input,
        BaselineSnapshotState baseline,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedPropertyCatalog addedPropertyCatalog,
        AddedMethodCatalog addedMethodCatalog,
        List<WorkerSkipped> skipped,
        int shimTypeCounter,
        int globalShimMethodCounter)
    {
        INamedTypeSymbol compiledType = CompiledMemberMatcher.FindCompiledType(
            typeState.TypeSymbol,
            targetTypesAssemblySymbol);
        if (compiledType == null)
        {
            return (shimTypeCounter, globalShimMethodCounter);
        }

        typeState.CompiledType = compiledType;
        foreach (PropertyDeclarationSyntax declaration in typeState.TypeDeclaration.Members
            .OfType<PropertyDeclarationSyntax>())
        {
            (shimTypeCounter, globalShimMethodCounter) = ClassifyProperty(
                declaration,
                typeState,
                compiledType,
                semanticModel,
                input,
                baseline,
                root,
                assemblyGlobalUsings,
                shimTypes,
                addedPropertyCatalog,
                addedMethodCatalog,
                skipped,
                shimTypeCounter,
                globalShimMethodCounter);
        }

        return (shimTypeCounter, globalShimMethodCounter);
    }

    private static (int ShimTypeCounter, int GlobalShimMethodCounter) ClassifyProperty(
        PropertyDeclarationSyntax declaration,
        TypeEmitState typeState,
        INamedTypeSymbol compiledType,
        SemanticModel semanticModel,
        WorkerInput input,
        BaselineSnapshotState baseline,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedPropertyCatalog addedPropertyCatalog,
        AddedMethodCatalog addedMethodCatalog,
        List<WorkerSkipped> skipped,
        int shimTypeCounter,
        int globalShimMethodCounter)
    {
        AddedPropertyCandidate candidate = CreateCandidateOrNull(
            declaration,
            typeState,
            compiledType,
            semanticModel,
            baseline,
            addedMethodCatalog);
        if (candidate == null)
        {
            return (shimTypeCounter, globalShimMethodCounter);
        }

        MarkClassifiedAccessors(candidate, addedMethodCatalog);
        if (candidate.Reason != null || IsExcluded(input, candidate.GetterKey, candidate.SetterKey))
        {
            candidate.Binding.UnavailableReason = candidate.Reason
                ?? AddedPropertySkipReasons.UnavailableAddedProperty;
            addedPropertyCatalog.Register(candidate.Binding);
            AppendSkippedAccessors(candidate.Binding, skipped);
            return (shimTypeCounter, globalShimMethodCounter);
        }

        (ShimTypeBuilder shimType, int nextShimTypeCounter) = OrdinaryMethodQueue.EnsureShimType(
            typeState,
            root,
            assemblyGlobalUsings,
            shimTypes,
            shimTypeCounter);
        candidate.Binding.Getter = CreateBinding(
            candidate.GetterKey,
            candidate.Binding.Symbol.GetMethod,
            shimType,
            globalShimMethodCounter);
        globalShimMethodCounter++;
        if (candidate.Binding.Symbol.SetMethod != null)
        {
            candidate.Binding.Setter = CreateBinding(
                candidate.SetterKey,
                candidate.Binding.Symbol.SetMethod,
                shimType,
                globalShimMethodCounter);
            globalShimMethodCounter++;
        }

        addedMethodCatalog.Register(candidate.Binding.Getter);
        if (candidate.Binding.Setter != null)
        {
            addedMethodCatalog.Register(candidate.Binding.Setter);
        }

        addedPropertyCatalog.Register(candidate.Binding);
        return (nextShimTypeCounter, globalShimMethodCounter);
    }

    private static AddedPropertyCandidate CreateCandidateOrNull(
        PropertyDeclarationSyntax declaration,
        TypeEmitState typeState,
        INamedTypeSymbol compiledType,
        SemanticModel semanticModel,
        BaselineSnapshotState baseline,
        AddedMethodCatalog addedMethodCatalog)
    {
        IPropertySymbol symbol = semanticModel.GetDeclaredSymbol(declaration);
        if (symbol == null || HasCompiledProperty(compiledType, symbol.Name))
        {
            return null;
        }

        string syntaxKey = WorkerSyntaxIndex.BuildSyntaxPropertyKey(
            typeState.TypeMetadataNameFromSyntax,
            declaration);
        if (IsUnchangedBaselineProperty(baseline, syntaxKey))
        {
            return null;
        }

        if (baseline.HasBaseline)
        {
            addedMethodCatalog.AddAddedPropertySyntaxKey(syntaxKey);
        }

        bool isAuto = IsAutoProperty(declaration);
        string getterKey = BuildGetterKey(typeState.TypeSymbol, symbol);
        string setterKey = BuildSetterKey(typeState.TypeSymbol, symbol);
        string reason = symbol.GetMethod == null
            ? AddedPropertySkipReasons.SetOnly
            : EvaluateDeclarationSkipReason(symbol, declaration, typeState.TypeSymbol);
        if (isAuto && reason == null)
        {
            reason = AddedMethodSkipReasons.AddedProperty;
        }

        AddedPropertyBinding binding = new AddedPropertyBinding
        {
            SourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath,
            PropertyKey = AddedPropertyCatalog.FormatPropertyKey(
                CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
                symbol.Name),
            SyntaxKey = syntaxKey,
            Name = symbol.Name,
            HostType = typeState.TypeSymbol,
            ValueType = symbol.Type,
            IsStatic = symbol.IsStatic,
            IsAuto = isAuto,
            Declaration = declaration,
            Symbol = symbol
        };
        return new AddedPropertyCandidate
        {
            Binding = binding,
            GetterKey = getterKey,
            SetterKey = setterKey,
            Reason = reason
        };
    }

    private static bool HasCompiledProperty(INamedTypeSymbol compiledType, string propertyName)
    {
        foreach (ISymbol member in compiledType.GetMembers(propertyName))
        {
            if (member is IPropertySymbol)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnchangedBaselineProperty(BaselineSnapshotState baseline, string syntaxKey)
    {
        if (!baseline.HasBaseline
            || baseline.SnapshotPropertyMap == null
            || baseline.PlainCurrentPropertyMap == null)
        {
            return false;
        }

        if (!baseline.SnapshotPropertyMap.TryGetValue(syntaxKey, out PropertyDeclarationSyntax snapshot)
            || !baseline.PlainCurrentPropertyMap.TryGetValue(syntaxKey, out PropertyDeclarationSyntax current))
        {
            return false;
        }

        return SyntaxFactory.AreEquivalent(snapshot, current, topLevel: false);
    }

    private static bool IsAutoProperty(PropertyDeclarationSyntax declaration)
    {
        if (declaration.ExpressionBody != null || declaration.AccessorList == null)
        {
            return false;
        }

        foreach (AccessorDeclarationSyntax accessor in declaration.AccessorList.Accessors)
        {
            if (accessor.Body != null || accessor.ExpressionBody != null)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsExcluded(WorkerInput input, string getterKey, string setterKey)
    {
        return input.ExcludedAddedMethodKeys.Contains(getterKey)
            || (setterKey != null && input.ExcludedAddedMethodKeys.Contains(setterKey));
    }

    private static void MarkClassifiedAccessors(
        AddedPropertyCandidate candidate,
        AddedMethodCatalog addedMethodCatalog)
    {
        if (candidate.GetterKey != null)
        {
            addedMethodCatalog.MarkClassifiedAdded(candidate.GetterKey);
        }

        if (candidate.SetterKey != null)
        {
            addedMethodCatalog.MarkClassifiedAdded(candidate.SetterKey);
        }
    }

    private static string BuildGetterKey(INamedTypeSymbol hostType, IPropertySymbol symbol)
    {
        if (symbol.GetMethod == null)
        {
            return null;
        }

        return WorkerMethodKeys.BuildMethodKey(
            CecilTypeNames.ToMetadataName(hostType),
            symbol.GetMethod.Name,
            Array.Empty<string>(),
            symbol.GetMethod.Arity);
    }

    private static string BuildSetterKey(INamedTypeSymbol hostType, IPropertySymbol symbol)
    {
        if (symbol.SetMethod == null)
        {
            return null;
        }

        return WorkerMethodKeys.BuildMethodKey(
            CecilTypeNames.ToMetadataName(hostType),
            symbol.SetMethod.Name,
            new[] { CecilTypeNames.ToCecilFullName(symbol.Type) },
            symbol.SetMethod.Arity);
    }

    private static AddedMethodBinding CreateBinding(
        string methodKey,
        IMethodSymbol accessorSymbol,
        ShimTypeBuilder shimType,
        int shimMethodCounter)
    {
        return new AddedMethodBinding
        {
            MethodKey = methodKey,
            ShimTypeName = shimType.ShimTypeName,
            ShimMethodName = accessorSymbol.Name + "__shim" + shimMethodCounter,
            NamespaceName = shimType.NamespaceName,
            IsStatic = accessorSymbol.IsStatic,
            ParameterCount = accessorSymbol.Parameters.Length
        };
    }

    private static string EvaluateDeclarationSkipReason(
        IPropertySymbol symbol,
        PropertyDeclarationSyntax declaration,
        INamedTypeSymbol hostType)
    {
        if (hostType.TypeKind == TypeKind.Struct)
        {
            return AddedPropertySkipReasons.StructHost;
        }

        if (hostType.TypeKind == TypeKind.Interface
            || symbol.IsVirtual
            || symbol.IsOverride
            || symbol.IsAbstract)
        {
            return AddedPropertySkipReasons.VirtualOrAbstract;
        }

        if (declaration.ExplicitInterfaceSpecifier != null)
        {
            return AddedPropertySkipReasons.ExplicitInterface;
        }

        if (HasInitAccessor(declaration))
        {
            return AddedPropertySkipReasons.InitAccessor;
        }

        if (symbol.ReturnsByRef || symbol.ReturnsByRefReadonly)
        {
            return AddedPropertySkipReasons.RefOutIn;
        }

        if (!AccessibilityRules.IsExternallyVisibleType(symbol.Type))
        {
            return AddedPropertySkipReasons.ValueTypeNotExternallyVisible;
        }

        return null;
    }

    private static bool HasInitAccessor(PropertyDeclarationSyntax declaration)
    {
        if (declaration.AccessorList == null)
        {
            return false;
        }

        foreach (AccessorDeclarationSyntax accessor in declaration.AccessorList.Accessors)
        {
            if (accessor.IsKind(SyntaxKind.InitAccessorDeclaration))
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendSkippedAccessors(AddedPropertyBinding binding, List<WorkerSkipped> skipped)
    {
        AppendSkippedAccessor(binding, binding.Symbol.GetMethod, skipped);
        if (binding.Symbol.SetMethod != null)
        {
            AppendSkippedAccessor(binding, binding.Symbol.SetMethod, skipped);
        }
    }

    private static void AppendSkippedAccessor(
        AddedPropertyBinding binding,
        IMethodSymbol accessorSymbol,
        List<WorkerSkipped> skipped)
    {
        if (accessorSymbol == null)
        {
            return;
        }

        skipped.Add(new WorkerSkipped
        {
            SourceProjectRelativePath = binding.SourceProjectRelativePath,
            Method = WorkerMethodKeys.FormatMethodLabel(accessorSymbol),
            Reason = binding.UnavailableReason
        });
    }

    private sealed class AddedPropertyCandidate
    {
        public AddedPropertyBinding Binding { get; set; }

        public string GetterKey { get; set; }

        public string SetterKey { get; set; }

        public string Reason { get; set; }
    }
}
