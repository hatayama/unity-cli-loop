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
/// Removes an added property's accessor pair when either accessor body cannot be emitted safely.
/// </summary>
internal static class AddedPropertyAccessorGuard
{
    internal static bool SkipUnavailableAccessors(
        List<TypeEmitState> typeEmitStates,
        AddedPropertyCatalog addedPropertyCatalog,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerSkipped> skipped)
    {
        bool skippedAny = false;
        foreach (AddedPropertyBinding binding in addedPropertyCatalog.Bindings)
        {
            if (binding.UnavailableReason != null || binding.IsAuto)
            {
                continue;
            }

            TypeEmitState typeState = FindTypeState(typeEmitStates, binding);
            if (typeState == null)
            {
                continue;
            }

            string reason = FindAccessorSkipReason(
                binding,
                typeState,
                addedMethodCatalog,
                addedFieldCatalog,
                addedPropertyCatalog);
            if (reason == null)
            {
                continue;
            }

            addedPropertyCatalog.MarkUnavailable(binding.PropertyKey, reason);
            UnregisterAccessor(addedMethodCatalog, binding.Getter);
            UnregisterAccessor(addedMethodCatalog, binding.Setter);
            AppendSkippedAccessor(binding, binding.Symbol.GetMethod, skipped);
            AppendSkippedAccessor(binding, binding.Symbol.SetMethod, skipped);
            skippedAny = true;
        }

        return skippedAny;
    }

    private static TypeEmitState FindTypeState(
        List<TypeEmitState> typeEmitStates,
        AddedPropertyBinding binding)
    {
        foreach (TypeEmitState typeState in typeEmitStates)
        {
            if (typeState.SourceUnit.SyntaxTree == binding.Declaration.SyntaxTree
                && SymbolEqualityComparer.Default.Equals(typeState.TypeSymbol, binding.HostType))
            {
                return typeState;
            }
        }

        return null;
    }

    private static string FindAccessorSkipReason(
        AddedPropertyBinding binding,
        TypeEmitState typeState,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        AddedPropertyCatalog addedPropertyCatalog)
    {
        SemanticModel semanticModel = typeState.SourceUnit.SemanticModel;
        if (binding.Declaration.ExpressionBody != null)
        {
            return EvaluateAccessor(
                binding,
                binding.Getter,
                binding.Symbol.GetMethod,
                binding.Declaration.ExpressionBody,
                typeState,
                addedMethodCatalog,
                addedFieldCatalog,
                addedPropertyCatalog);
        }

        foreach (AccessorDeclarationSyntax accessor in GetAccessors(binding.Declaration))
        {
            SyntaxNode bodyNode = (SyntaxNode)accessor.Body ?? accessor.ExpressionBody;
            bool isGetter = accessor.IsKind(SyntaxKind.GetAccessorDeclaration);
            string reason = EvaluateAccessor(
                binding,
                isGetter ? binding.Getter : binding.Setter,
                isGetter ? binding.Symbol.GetMethod : binding.Symbol.SetMethod,
                bodyNode,
                typeState,
                addedMethodCatalog,
                addedFieldCatalog,
                addedPropertyCatalog);
            if (reason != null)
            {
                return reason;
            }
        }

        return null;
    }

    private static string EvaluateAccessor(
        AddedPropertyBinding binding,
        AddedMethodBinding accessorBinding,
        IMethodSymbol accessorSymbol,
        SyntaxNode bodyNode,
        TypeEmitState typeState,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        AddedPropertyCatalog addedPropertyCatalog)
    {
        if (accessorBinding == null || accessorSymbol == null || bodyNode == null)
        {
            return null;
        }

        MethodTransformDecision decision = MethodTransformDecider.DecideMethodTransform(
            typeState.TypeDeclaration,
            typeState.TypeSymbol,
            methodDeclaration: null,
            accessorSymbol,
            bodyNode,
            typeState.SourceUnit.SemanticModel,
            typeState.CompiledType);
        if (decision.SkipReason != null)
        {
            return decision.SkipReason;
        }

        decision = MethodTransformDecider.DecideAddedMethodAccessors(
            accessorSymbol,
            typeState.TypeSymbol,
            bodyNode,
            typeState.SourceUnit.SemanticModel,
            decision);
        if (decision.SkipReason != null)
        {
            return decision.SkipReason;
        }

        SetAccessorDecision(binding, accessorBinding, decision);
        (string callSiteReason, _) = AddedCallSiteGuard.EvaluateAddedCallSiteSkipReason(
            bodyNode,
            typeState.SourceUnit.SemanticModel,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog,
            binding.HostType);
        return callSiteReason;
    }

    private static void SetAccessorDecision(
        AddedPropertyBinding binding,
        AddedMethodBinding accessorBinding,
        MethodTransformDecision decision)
    {
        if (accessorBinding == binding.Getter)
        {
            binding.GetterDecision = decision;
            return;
        }

        binding.SetterDecision = decision;
    }

    private static IEnumerable<AccessorDeclarationSyntax> GetAccessors(PropertyDeclarationSyntax declaration)
    {
        return declaration.AccessorList == null
            ? Array.Empty<AccessorDeclarationSyntax>()
            : declaration.AccessorList.Accessors;
    }

    private static void UnregisterAccessor(AddedMethodCatalog addedMethodCatalog, AddedMethodBinding accessor)
    {
        if (accessor != null)
        {
            addedMethodCatalog.Unregister(accessor.MethodKey);
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
}
