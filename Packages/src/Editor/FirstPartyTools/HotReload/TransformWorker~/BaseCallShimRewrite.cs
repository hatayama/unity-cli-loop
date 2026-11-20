using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Rewrites a <c>base.M(args)</c> call in a shim body into a call to its stand-in, which the
/// Editor redirects to the base method when it transplants the body.
/// </summary>
internal static class BaseCallShimRewrite
{
    /// <summary>True when <paramref name="node"/> is a <c>base.M(args)</c> call.</summary>
    internal static bool IsBaseCall(InvocationExpressionSyntax node)
    {
        return node.Expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Expression is BaseExpressionSyntax;
    }

    /// <summary>Rewrites <c>base.M(args)</c> into a call to its stand-in, which the Editor redirects to the base method.</summary>
    internal static ExpressionSyntax Rewrite(
        ShimBodyRewriter rewriter,
        InvocationExpressionSyntax node,
        IMethodSymbol target,
        BaseCallStandInPlan standIns,
        INamedTypeSymbol targetType)
    {
        if (standIns == null)
        {
            throw new InvalidOperationException("A base call reached the shim rewriter in a body that cannot host one.");
        }

        int depth = BaseCallRules.FindBaseDepth(targetType, target.ContainingType);
        string name = standIns.GetOrAdd(depth, target, ShimMethodFactory.BuildInstanceParameterType(targetType));
        // Why visit the arguments: a nested base call or a this inside them needs the same rewrite.
        ArgumentListSyntax visitedArguments = (ArgumentListSyntax)rewriter.VisitArgumentList(node.ArgumentList);
        List<ArgumentSyntax> arguments = new List<ArgumentSyntax>
        {
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName))
        };
        arguments.AddRange(visitedArguments.Arguments);
        return SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(name),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)))
            .WithTriviaFrom(node);
    }
}
