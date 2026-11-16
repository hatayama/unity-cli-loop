using io.github.hatayama.UnityCliLoop.FirstPartyTools;
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
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// What: methods the compiler calls for a syntax node with no invocation of them in the source
/// (await, foreach, deconstruction, collection initializer, query expression, ref struct using).
/// A closure or state machine emitted into the shim assembly makes those calls too, so an
/// inaccessible one fails at run time exactly like a written call. None has an accessor rewrite,
/// so such a body is refused.
/// </summary>
internal static class ImplicitCallTargets
{
    private const string AwaitConstruct = "await";
    private const string ForEachConstruct = "foreach";
    private const string DeconstructionConstruct = "a deconstruction";
    private const string CollectionInitializerConstruct = "a collection initializer";
    private const string QueryConstruct = "a query expression";
    private const string UsingConstruct = "using";
    private const string AwaitUsingConstruct = "await using";
    private const string PositionalPatternConstruct = "a positional pattern";

    /// <summary>
    /// What: the refusal for the first inaccessible method the compiler calls for this node, or
    /// null when the node makes no such call.
    /// </summary>
    internal static WorkerReason FindInaccessibleCallReasonOrNull(SemanticModel semanticModel, SyntaxNode node)
    {
        foreach ((IMethodSymbol method, string construct) in EnumerateTargets(semanticModel, node))
        {
            if (method == null)
            {
                continue;
            }

            IMethodSymbol target = AccessibilityRules.UnreducedMethod(method);
            if (AccessibilityRules.IsInaccessibleFromExternalAssembly(target))
            {
                return WorkerReason.Of(
                    HotReloadWorkerReasonCode.AccessorImplicitCallNoShape,
                    DisplayName(target),
                    construct);
            }
        }

        return null;
    }

    private static IEnumerable<(IMethodSymbol Method, string Construct)> EnumerateTargets(
        SemanticModel semanticModel,
        SyntaxNode node)
    {
        switch (node)
        {
            case AwaitExpressionSyntax awaitExpression:
                return AwaitTargets(semanticModel.GetAwaitExpressionInfo(awaitExpression));
            case CommonForEachStatementSyntax forEach:
                return ForEachTargets(semanticModel, forEach);
            case AssignmentExpressionSyntax assignment when IsDeconstruction(assignment):
                return DeconstructionTargets(semanticModel.GetDeconstructionInfo(assignment));
            case InitializerExpressionSyntax initializer
                when initializer.IsKind(SyntaxKind.CollectionInitializerExpression):
                return CollectionInitializerTargets(semanticModel, initializer);
            case SelectOrGroupClauseSyntax selectOrGroup:
                return Single(semanticModel.GetSymbolInfo(selectOrGroup).Symbol, QueryConstruct);
            case OrderingSyntax ordering:
                return Single(semanticModel.GetSymbolInfo(ordering).Symbol, QueryConstruct);
            case QueryClauseSyntax queryClause:
                return QueryClauseTargets(semanticModel.GetQueryClauseInfo(queryClause));
            case RecursivePatternSyntax pattern when pattern.PositionalPatternClause != null:
                return Single(
                    (semanticModel.GetOperation(pattern) as IRecursivePatternOperation)?.DeconstructSymbol,
                    PositionalPatternConstruct);
            case UsingStatementSyntax usingStatement:
                return UsingTargets(
                    semanticModel,
                    usingStatement.Declaration?.Type,
                    usingStatement.Expression,
                    usingStatement.AwaitKeyword != default);
            case LocalDeclarationStatementSyntax localDeclaration
                when localDeclaration.UsingKeyword != default:
                return UsingTargets(
                    semanticModel,
                    localDeclaration.Declaration.Type,
                    null,
                    localDeclaration.AwaitKeyword != default);
            default:
                return Array.Empty<(IMethodSymbol, string)>();
        }
    }

    private static IEnumerable<(IMethodSymbol, string)> AwaitTargets(AwaitExpressionInfo info)
    {
        yield return (info.GetAwaiterMethod, AwaitConstruct);
        yield return (info.IsCompletedProperty?.GetMethod, AwaitConstruct);
        yield return (info.GetResultMethod, AwaitConstruct);
    }

    private static IEnumerable<(IMethodSymbol, string)> ForEachTargets(
        SemanticModel semanticModel,
        CommonForEachStatementSyntax forEach)
    {
        ForEachStatementInfo info = semanticModel.GetForEachStatementInfo(forEach);
        yield return (info.GetEnumeratorMethod, ForEachConstruct);
        yield return (info.MoveNextMethod, ForEachConstruct);
        yield return (info.CurrentProperty?.GetMethod, ForEachConstruct);
        yield return (info.DisposeMethod, ForEachConstruct);
        if (forEach is ForEachVariableStatementSyntax forEachVariable)
        {
            foreach ((IMethodSymbol, string) target in DeconstructionTargets(
                         semanticModel.GetDeconstructionInfo(forEachVariable)))
            {
                yield return target;
            }
        }
    }

    // Why the left side decides: `(a, b) = x` and `var (a, b) = x` deconstruct, and a tuple
    // assignment whose right side is a tuple literal reports no Deconstruct method, which is fine.
    private static bool IsDeconstruction(AssignmentExpressionSyntax assignment)
    {
        return assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && assignment.Left is TupleExpressionSyntax or DeclarationExpressionSyntax;
    }

    private static IEnumerable<(IMethodSymbol, string)> DeconstructionTargets(DeconstructionInfo info)
    {
        yield return (info.Method, DeconstructionConstruct);
        foreach (DeconstructionInfo nested in info.Nested)
        {
            foreach ((IMethodSymbol, string) target in DeconstructionTargets(nested))
            {
                yield return target;
            }
        }
    }

    private static IEnumerable<(IMethodSymbol, string)> CollectionInitializerTargets(
        SemanticModel semanticModel,
        InitializerExpressionSyntax initializer)
    {
        foreach (ExpressionSyntax element in initializer.Expressions)
        {
            yield return (
                semanticModel.GetCollectionInitializerSymbolInfo(element).Symbol as IMethodSymbol,
                CollectionInitializerConstruct);
        }
    }

    private static IEnumerable<(IMethodSymbol, string)> QueryClauseTargets(QueryClauseInfo info)
    {
        yield return (info.CastInfo.Symbol as IMethodSymbol, QueryConstruct);
        yield return (info.OperationInfo.Symbol as IMethodSymbol, QueryConstruct);
    }

    // Why only these resource types: the compiler that builds the patch (the Unity installation's)
    // calls IDisposable.Dispose or IAsyncDisposable.DisposeAsync through the interface, which is
    // public, whenever the resource implements it, even when a pattern method also exists. A ref
    // struct without IDisposable may offer a pattern Dispose, and an await using on any type without
    // IAsyncDisposable a pattern DisposeAsync; no semantic API reports which method was bound.
    private static IEnumerable<(IMethodSymbol, string)> UsingTargets(
        SemanticModel semanticModel,
        TypeSyntax declaredType,
        ExpressionSyntax resource,
        bool isAwaitUsing)
    {
        SyntaxNode typedNode = (SyntaxNode)declaredType ?? resource;
        ITypeSymbol resourceType = typedNode == null ? null : semanticModel.GetTypeInfo(typedNode).Type;
        if (resourceType == null)
        {
            return Array.Empty<(IMethodSymbol, string)>();
        }

        if (isAwaitUsing)
        {
            return Implements(resourceType, "System.IAsyncDisposable")
                ? Array.Empty<(IMethodSymbol, string)>()
                : Single(FindInstanceMethodCallableWithoutArgumentsOrNull(resourceType, "DisposeAsync"), AwaitUsingConstruct);
        }

        if (!resourceType.IsRefLikeType || Implements(resourceType, "System.IDisposable"))
        {
            return Array.Empty<(IMethodSymbol, string)>();
        }

        return Single(FindInstanceMethodCallableWithoutArgumentsOrNull(resourceType, "Dispose"), UsingConstruct);
    }

    private static bool Implements(ITypeSymbol type, string interfaceFullName)
    {
        return type.AllInterfaces.Any(candidate => candidate.ToDisplayString() == interfaceFullName);
    }

    // Why base types, optional parameters, and the order: the compiler finds an inherited pattern
    // method, binds one whose parameters all have defaults or are params, and prefers the overload
    // that needs the fewest omitted arguments, then one callable without expanding a params array.
    private static IMethodSymbol FindInstanceMethodCallableWithoutArgumentsOrNull(ITypeSymbol type, string name)
    {
        for (ITypeSymbol current = type; current != null; current = current.BaseType)
        {
            IMethodSymbol method = current.GetMembers(name)
                .OfType<IMethodSymbol>()
                .Where(candidate => !candidate.IsStatic
                    && candidate.Parameters.All(parameter => parameter.IsOptional || parameter.IsParams))
                .OrderBy(candidate => candidate.Parameters.Length)
                .ThenBy(candidate => candidate.Parameters.Any(parameter => parameter.IsParams) ? 1 : 0)
                .FirstOrDefault();
            if (method != null)
            {
                return method;
            }
        }

        return null;
    }

    private static IEnumerable<(IMethodSymbol, string)> Single(ISymbol symbol, string construct)
    {
        return new[] { (symbol as IMethodSymbol, construct) };
    }

    // A property accessor is named by its property, which is what the source shows.
    private static string DisplayName(IMethodSymbol method)
    {
        return method.AssociatedSymbol is IPropertySymbol property ? property.Name : method.Name;
    }
}
