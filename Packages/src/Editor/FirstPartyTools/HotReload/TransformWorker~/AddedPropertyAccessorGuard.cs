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
                typeState.SourceUnit.SemanticModel,
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
            if (typeState.SourceUnit.SyntaxTree == binding.Declaration.SyntaxTree)
            {
                return typeState;
            }
        }

        return null;
    }

    private static string FindAccessorSkipReason(
        AddedPropertyBinding binding,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        AddedPropertyCatalog addedPropertyCatalog)
    {
        foreach (AccessorDeclarationSyntax accessor in GetAccessors(binding.Declaration))
        {
            SyntaxNode bodyNode = (SyntaxNode)accessor.Body ?? accessor.ExpressionBody;
            (string reason, _) = AddedCallSiteGuard.EvaluateAddedCallSiteSkipReason(
                bodyNode,
                semanticModel,
                addedMethodCatalog,
                addedFieldCatalog,
                addedPropertyCatalog,
                binding.HostType);
            if (reason != null)
            {
                return reason;
            }
        }

        if (binding.Declaration.ExpressionBody == null)
        {
            return null;
        }

        (string expressionReason, _) = AddedCallSiteGuard.EvaluateAddedCallSiteSkipReason(
            binding.Declaration.ExpressionBody,
            semanticModel,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog,
            binding.HostType);
        return expressionReason;
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
