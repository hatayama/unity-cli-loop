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

// Recovers an owned compiled method when GetSymbolInfo is unbound because an argument
// failed to bind (error-typed), so VisitName can still qualify implicit this.
internal static class UnboundOwnedCallQualifier
{
    // Why CandidateSymbols first: an error-typed argument yields OverloadResolutionFailure
    // with the real methods, which is more precise than guessing by name and argument count.
    internal static ISymbol ResolveOwnedMethodOrNull(
        SimpleNameSyntax name,
        SemanticModel semanticModel,
        INamedTypeSymbol targetType)
    {
        Debug.Assert(name != null, "name");
        Debug.Assert(semanticModel != null, "semanticModel");
        Debug.Assert(targetType != null, "targetType");

        ImmutableArray<ISymbol> candidates = CollectCandidateSymbols(name, semanticModel);
        if (!candidates.IsDefaultOrEmpty)
        {
            return AdoptOwnedCandidatesOrNull(candidates, targetType);
        }

        return ResolveByNameAndArgumentCountOrNull(name, semanticModel, targetType);
    }

    private static ImmutableArray<ISymbol> CollectCandidateSymbols(
        SimpleNameSyntax name,
        SemanticModel semanticModel)
    {
        ImmutableArray<ISymbol> fromName = semanticModel.GetSymbolInfo(name).CandidateSymbols;
        if (!fromName.IsDefaultOrEmpty)
        {
            return fromName;
        }

        if (name.Parent is InvocationExpressionSyntax invocation && invocation.Expression == name)
        {
            return semanticModel.GetSymbolInfo(invocation).CandidateSymbols;
        }

        return fromName;
    }

    private static ISymbol AdoptOwnedCandidatesOrNull(
        ImmutableArray<ISymbol> candidates,
        INamedTypeSymbol targetType)
    {
        IMethodSymbol adopted = null;
        bool? expectedStatic = null;
        foreach (ISymbol candidate in candidates)
        {
            IMethodSymbol method = AsOwnedOrdinaryMethodOrNull(candidate, targetType);
            if (method == null)
            {
                return null;
            }

            if (expectedStatic == null)
            {
                expectedStatic = method.IsStatic;
                adopted = method;
                continue;
            }

            if (method.IsStatic != expectedStatic.Value)
            {
                return null;
            }
        }

        return adopted;
    }

    private static IMethodSymbol AsOwnedOrdinaryMethodOrNull(
        ISymbol candidate,
        INamedTypeSymbol targetType)
    {
        IMethodSymbol method = candidate as IMethodSymbol;
        if (method == null
            || method.MethodKind != MethodKind.Ordinary
            || method.ContainingType == null
            || !HarmonyAccessorShimRewrite.IsInInheritanceHierarchy(targetType, method.ContainingType))
        {
            return null;
        }

        return method;
    }

    private static ISymbol ResolveByNameAndArgumentCountOrNull(
        SimpleNameSyntax name,
        SemanticModel semanticModel,
        INamedTypeSymbol targetType)
    {
        if (name.Parent is not InvocationExpressionSyntax invocation || invocation.Expression != name)
        {
            return null;
        }

        string identifier = name.Identifier.ValueText;
        if (HasLocalLikeSymbol(semanticModel, name.SpanStart, identifier))
        {
            return null;
        }

        return FindUniqueOwnedMethodOrNull(
            targetType,
            identifier,
            invocation.ArgumentList.Arguments.Count);
    }

    private static bool HasLocalLikeSymbol(SemanticModel semanticModel, int position, string identifier)
    {
        foreach (ISymbol symbol in semanticModel.LookupSymbols(position, name: identifier))
        {
            if (symbol is ILocalSymbol || symbol is IParameterSymbol)
            {
                return true;
            }

            if (symbol is IMethodSymbol method && method.MethodKind == MethodKind.LocalFunction)
            {
                return true;
            }
        }

        return false;
    }

    private static ISymbol FindUniqueOwnedMethodOrNull(
        INamedTypeSymbol targetType,
        string methodName,
        int argumentCount)
    {
        IMethodSymbol unique = null;
        int matches = 0;
        for (INamedTypeSymbol type = targetType; type != null; type = type.BaseType)
        {
            foreach (ISymbol member in type.GetMembers(methodName))
            {
                if (member is not IMethodSymbol method
                    || method.MethodKind != MethodKind.Ordinary
                    || method.Parameters.Length != argumentCount)
                {
                    continue;
                }

                matches++;
                unique = method;
                if (matches > 1)
                {
                    return null;
                }
            }
        }

        return matches == 1 ? unique : null;
    }
}
