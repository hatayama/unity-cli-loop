using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// What: the query methods a query expression calls that the worker's binding found no method
/// for, each with the receiver type it is called on, for the unpassed implicit-call lookup.
/// </summary>
internal static class UnpassedQueryCallLookup
{
    // Why the receiver is carried: each clause calls its query method on what the previous call
    // returned. After a clause that binds to nothing, the last type known stands in, which is right
    // for a query whose methods all return the same sequence type.
    internal static IEnumerable<(ITypeSymbol Receiver, string MethodName)> MissingCalls(
        SemanticModel semanticModel,
        QueryExpressionSyntax query)
    {
        ITypeSymbol receiver = TypeOf(semanticModel, query.FromClause.Expression);
        List<(ITypeSymbol, string)> missing = new List<(ITypeSymbol, string)>();
        if (query.FromClause.Type != null)
        {
            receiver = CallOrRecordMissing(
                semanticModel.GetQueryClauseInfo(query.FromClause).CastInfo.Symbol,
                receiver,
                "Cast",
                missing);
        }

        foreach (QueryClauseSyntax clause in query.Body.Clauses)
        {
            receiver = AddMissingClauseCalls(semanticModel, clause, receiver, missing);
        }

        CallOrRecordMissing(
            semanticModel.GetSymbolInfo(query.Body.SelectOrGroup).Symbol,
            receiver,
            query.Body.SelectOrGroup is GroupClauseSyntax ? "GroupBy" : "Select",
            missing);
        return missing;
    }

    // The type the query goes on with: the bound method's return type, or the receiver unchanged
    // after recording the method as missing.
    private static ITypeSymbol CallOrRecordMissing(
        ISymbol bound,
        ITypeSymbol receiver,
        string methodName,
        List<(ITypeSymbol, string)> missing)
    {
        if (bound is IMethodSymbol method)
        {
            return method.ReturnType;
        }

        missing.Add((receiver, methodName));
        return receiver;
    }

    private static ITypeSymbol AddMissingClauseCalls(
        SemanticModel semanticModel,
        QueryClauseSyntax clause,
        ITypeSymbol receiver,
        List<(ITypeSymbol, string)> missing)
    {
        if (clause is OrderByClauseSyntax orderBy)
        {
            return AddMissingOrderingCalls(semanticModel, orderBy, receiver, missing);
        }

        QueryClauseInfo info = semanticModel.GetQueryClauseInfo(clause);
        if (clause is JoinClauseSyntax join && join.Type != null && info.CastInfo.Symbol == null)
        {
            missing.Add((TypeOf(semanticModel, join.InExpression), "Cast"));
        }

        if (clause is FromClauseSyntax from && from.Type != null && info.CastInfo.Symbol == null)
        {
            missing.Add((TypeOf(semanticModel, from.Expression), "Cast"));
        }

        return CallOrRecordMissing(info.OperationInfo.Symbol, receiver, OperationName(clause), missing);
    }

    private static ITypeSymbol AddMissingOrderingCalls(
        SemanticModel semanticModel,
        OrderByClauseSyntax orderBy,
        ITypeSymbol receiver,
        List<(ITypeSymbol, string)> missing)
    {
        for (int index = 0; index < orderBy.Orderings.Count; index++)
        {
            OrderingSyntax ordering = orderBy.Orderings[index];
            bool descending = ordering.AscendingOrDescendingKeyword.IsKind(SyntaxKind.DescendingKeyword);
            string name = index == 0
                ? (descending ? "OrderByDescending" : "OrderBy")
                : (descending ? "ThenByDescending" : "ThenBy");
            receiver = CallOrRecordMissing(semanticModel.GetSymbolInfo(ordering).Symbol, receiver, name, missing);
        }

        return receiver;
    }

    private static string OperationName(QueryClauseSyntax clause)
    {
        switch (clause)
        {
            case WhereClauseSyntax:
                return "Where";
            case FromClauseSyntax:
                return "SelectMany";
            case JoinClauseSyntax join:
                return join.Into != null ? "GroupJoin" : "Join";
            default:
                return "Select";
        }
    }

    private static ITypeSymbol TypeOf(SemanticModel semanticModel, SyntaxNode node)
    {
        return node == null ? null : semanticModel.GetTypeInfo(node).Type;
    }
}
