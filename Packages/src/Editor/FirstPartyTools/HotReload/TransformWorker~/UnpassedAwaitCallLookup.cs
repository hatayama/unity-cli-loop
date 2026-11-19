using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// What: the awaiter methods an await, an await using, or an await foreach calls that the worker's
/// binding found no method for, each with the receiver type it is called on, for the unpassed
/// implicit-call lookup. An await using and an await foreach await the value a public pattern
/// method returns, so a missing awaiter member there leaves no await in the source to inspect.
/// </summary>
internal static class UnpassedAwaitCallLookup
{
    internal static IEnumerable<(ITypeSymbol Receiver, string MethodName)> MissingAwaitCalls(
        SemanticModel semanticModel,
        AwaitExpressionSyntax awaitExpression)
    {
        ITypeSymbol awaited = awaitExpression.Expression == null
            ? null
            : semanticModel.GetTypeInfo(awaitExpression.Expression).Type;
        return MissingAwaiterCalls(semanticModel.GetAwaitExpressionInfo(awaitExpression), awaited);
    }

    /// <summary>
    /// What: for an await using on a resource without IAsyncDisposable, the pattern DisposeAsync
    /// when it binds to nothing, or else the awaiter members of its result that bind to nothing.
    /// </summary>
    internal static IEnumerable<(ITypeSymbol Receiver, string MethodName)> MissingAwaitUsingCalls(
        SemanticModel semanticModel,
        int position,
        ITypeSymbol resourceType)
    {
        return MissingCallsOfAwaitedCall(semanticModel, position, resourceType, "DisposeAsync", isRequired: true);
    }

    /// <summary>
    /// What: for an await foreach the worker could not bind, the first method of the chain
    /// GetAsyncEnumerator, MoveNextAsync and the awaiter of its result that binds to nothing,
    /// followed by the awaiter members of the enumerator's DisposeAsync result when it has one.
    /// </summary>
    internal static IEnumerable<(ITypeSymbol Receiver, string MethodName)> MissingAwaitForEachCalls(
        SemanticModel semanticModel,
        int position,
        ITypeSymbol collectionType)
    {
        IMethodSymbol getEnumerator = BindCallOrNull(semanticModel, position, collectionType, "GetAsyncEnumerator");
        if (getEnumerator == null)
        {
            return new[] { (collectionType, "GetAsyncEnumerator") };
        }

        List<(ITypeSymbol, string)> missing = new List<(ITypeSymbol, string)>();
        missing.AddRange(MissingCallsOfAwaitedCall(
            semanticModel, position, getEnumerator.ReturnType, "MoveNextAsync", isRequired: true));
        missing.AddRange(MissingCallsOfAwaitedCall(
            semanticModel, position, getEnumerator.ReturnType, "DisposeAsync", isRequired: false));
        return missing;
    }

    // Why a speculative `await default(R).M()`: it binds the call and the awaiter of its result by
    // the compiler's own rules, while the statement in the source failed as a whole.
    private static IEnumerable<(ITypeSymbol, string)> MissingCallsOfAwaitedCall(
        SemanticModel semanticModel,
        int position,
        ITypeSymbol receiver,
        string methodName,
        bool isRequired)
    {
        AwaitExpressionSyntax awaitExpression = ParseAwaitOrNull(
            "await " + DefaultOf(receiver) + "." + methodName + "();");
        if (receiver == null
            || awaitExpression == null
            || !semanticModel.TryGetSpeculativeSemanticModel(
                position,
                (StatementSyntax)awaitExpression.Parent,
                out SemanticModel speculative))
        {
            return Array.Empty<(ITypeSymbol, string)>();
        }

        // Why an extension counts as no method: await using and await foreach call only an instance
        // DisposeAsync or MoveNextAsync, so an extension the speculative call binds to instead
        // stands in for an instance method the worker cannot see.
        if (speculative.GetSymbolInfo(awaitExpression.Expression).Symbol is not IMethodSymbol call
            || call.MethodKind == MethodKind.ReducedExtension)
        {
            return isRequired ? new[] { (receiver, methodName) } : Array.Empty<(ITypeSymbol, string)>();
        }

        return MissingAwaiterCalls(speculative.GetAwaitExpressionInfo(awaitExpression), call.ReturnType);
    }

    private static IMethodSymbol BindCallOrNull(
        SemanticModel semanticModel,
        int position,
        ITypeSymbol receiver,
        string methodName)
    {
        if (receiver == null)
        {
            return null;
        }

        ExpressionSyntax call = SyntaxFactory.ParseExpression(DefaultOf(receiver) + "." + methodName + "()");
        return semanticModel.GetSpeculativeSymbolInfo(position, call, SpeculativeBindingOption.BindAsExpression)
            .Symbol as IMethodSymbol;
    }

    private static IEnumerable<(ITypeSymbol, string)> MissingAwaiterCalls(AwaitExpressionInfo info, ITypeSymbol awaited)
    {
        if (info.GetAwaiterMethod == null)
        {
            return new[] { (awaited, "GetAwaiter") };
        }

        List<(ITypeSymbol, string)> missing = new List<(ITypeSymbol, string)>();
        ITypeSymbol awaiter = info.GetAwaiterMethod.ReturnType;
        if (info.IsCompletedProperty == null)
        {
            missing.Add((awaiter, "IsCompleted"));
        }

        if (info.GetResultMethod == null)
        {
            missing.Add((awaiter, "GetResult"));
        }

        return missing;
    }

    private static AwaitExpressionSyntax ParseAwaitOrNull(string statementText)
    {
        return SyntaxFactory.ParseStatement(statementText) is ExpressionStatementSyntax
        {
            Expression: AwaitExpressionSyntax awaitExpression,
        }
            ? awaitExpression
            : null;
    }

    private static string DefaultOf(ITypeSymbol type)
    {
        return type == null
            ? "default"
            : "default(" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")";
    }
}
