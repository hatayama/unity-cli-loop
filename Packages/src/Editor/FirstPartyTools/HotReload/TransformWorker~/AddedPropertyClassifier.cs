using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// Classifies properties missing from a compiled type before accessor skips and body emission.
/// </summary>
internal static class AddedPropertyClassifier
{
    internal static void ClassifyAddedProperties(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        WorkerTypeHome home,
        WorkerInput input,
        BaselineSnapshotState baseline,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedPropertyCatalog addedPropertyCatalog,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerSkipped> skipped,
        ShimNameAllocator shimNames)
    {
        // The counterpart the caller resolved: the patch target's own type, or the artifact of
        // an earlier reload that serves it. A type neither holds is one this edit introduces,
        // and nothing of it is an addition to something already running.
        INamedTypeSymbol compiledType = typeState.CompiledType;
        if (compiledType == null)
        {
            return;
        }

        foreach (PropertyDeclarationSyntax declaration in typeState.TypeDeclaration.Members
            .OfType<PropertyDeclarationSyntax>())
        {
            ClassifyProperty(
                declaration,
                typeState,
                compiledType,
                semanticModel,
                home,
                input,
                baseline,
                root,
                assemblyGlobalUsings,
                shimTypes,
                addedPropertyCatalog,
                addedMethodCatalog,
                addedFieldCatalog,
                skipped,
                shimNames);
        }
    }

    private static void ClassifyProperty(
        PropertyDeclarationSyntax declaration,
        TypeEmitState typeState,
        INamedTypeSymbol compiledType,
        SemanticModel semanticModel,
        WorkerTypeHome home,
        WorkerInput input,
        BaselineSnapshotState baseline,
        CompilationUnitSyntax root,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        List<ShimTypeBuilder> shimTypes,
        AddedPropertyCatalog addedPropertyCatalog,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerSkipped> skipped,
        ShimNameAllocator shimNames)
    {
        AddedPropertyCandidate candidate = CreateCandidateOrNull(
            declaration,
            typeState,
            compiledType,
            semanticModel,
            home,
            baseline,
            addedMethodCatalog);
        if (candidate == null)
        {
            return;
        }

        MarkClassifiedAccessors(candidate, addedMethodCatalog);
        if (candidate.Reason != null || IsExcluded(input, candidate.GetterKey, candidate.SetterKey))
        {
            candidate.Binding.UnavailableReason = candidate.Reason
                ?? WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyUnavailableAddedProperty);
            addedPropertyCatalog.Register(candidate.Binding);
            AppendSkippedAccessors(candidate.Binding, skipped);
            return;
        }

        if (candidate.Binding.IsAuto)
        {
            AddedPropertySkipEvaluator.RegisterAutoPropertyStore(candidate.Binding, addedFieldCatalog);
        }

        ShimTypeBuilder shimType = OrdinaryMethodShimTypes.EnsureShimType(
            typeState,
            root,
            assemblyGlobalUsings,
            shimTypes,
            shimNames);
        // Why the getter name is taken first: the accessors are numbered getter before setter,
        // and the numbers appear in the emitted shim names.
        candidate.Binding.Getter = CreateBinding(
            candidate.GetterKey,
            candidate.Binding.Symbol.GetMethod,
            shimType,
            shimNames.NextShimMethodName(candidate.Binding.Symbol.GetMethod.Name));
        if (candidate.Binding.Symbol.SetMethod != null)
        {
            candidate.Binding.Setter = CreateBinding(
                candidate.SetterKey,
                candidate.Binding.Symbol.SetMethod,
                shimType,
                shimNames.NextShimMethodName(candidate.Binding.Symbol.SetMethod.Name));
        }

        addedMethodCatalog.Register(candidate.Binding.Getter);
        if (candidate.Binding.Setter != null)
        {
            addedMethodCatalog.Register(candidate.Binding.Setter);
        }

        addedPropertyCatalog.Register(candidate.Binding);
    }

    private static AddedPropertyCandidate CreateCandidateOrNull(
        PropertyDeclarationSyntax declaration,
        TypeEmitState typeState,
        INamedTypeSymbol compiledType,
        SemanticModel semanticModel,
        WorkerTypeHome home,
        BaselineSnapshotState baseline,
        AddedMethodCatalog addedMethodCatalog)
    {
        IPropertySymbol symbol = semanticModel.GetDeclaredSymbol(declaration);
        if (symbol == null || AddedPropertySkipEvaluator.HasCompiledProperty(compiledType, symbol.Name))
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
        WorkerReason reason = AddedPropertySkipEvaluator.EvaluateCompiledMemberKindChangeReason(compiledType, symbol.Name);
        if (reason == null)
        {
            reason = symbol.GetMethod == null
                ? WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertySetOnly)
                : AddedPropertySkipEvaluator.EvaluateDeclarationSkipReason(symbol, declaration, typeState.TypeSymbol);
        }

        if (isAuto && reason == null)
        {
            reason = AddedPropertySkipEvaluator.EvaluateAutoPropertyStoreSkipReason(
                declaration,
                symbol,
                typeState.TypeSymbol,
                semanticModel,
                home,
                typeState.SourceUnit);
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
        string shimMethodName)
    {
        return new AddedMethodBinding
        {
            MethodKey = methodKey,
            ShimTypeName = shimType.ShimTypeName,
            ShimMethodName = shimMethodName,
            NamespaceName = shimType.NamespaceName,
            IsStatic = accessorSymbol.IsStatic,
            ParameterCount = accessorSymbol.Parameters.Length
        };
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

        public WorkerReason Reason { get; set; }
    }
}
