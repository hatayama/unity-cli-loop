using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Tells whether an inaccessible field is used by reference while a later await in the same
/// expression runs. The accessor rewrite turns the field into a call that returns a reference, and C#
/// cannot keep that reference across an await (CS8178), so such a use has no accessor rewrite shape.
/// </summary>
internal static class FieldReferenceAwaitRules
{
    /// <summary>
    /// Whether the field named at the site is used by reference (a ref, out or in argument written
    /// with its keyword, the receiver of a call that takes it by reference, or an assignment target,
    /// directly or through members and indexers of a struct or a deconstruction tuple) in an
    /// expression with an await that runs after that use.
    /// </summary>
    internal static bool IsHeldAcrossLaterAwait(SemanticModel semanticModel, ExpressionSyntax site)
    {
        ExpressionSyntax held = ClimbToTheUseByReference(semanticModel, site);
        SyntaxNode holder = FindHolderOrNull(semanticModel, held);
        return holder != null && HasAwaitAfter(holder, held.Span.End);
    }

    // Why climb through a struct's members and indexers: they reach the struct through the field's
    // reference, so '_point.X += await t' and '_indexed[0] = await t' keep it as the field itself would.
    // Why climb through a tuple element without a keyword: a deconstruction assigns through it.
    private static ExpressionSyntax ClimbToTheUseByReference(SemanticModel semanticModel, ExpressionSyntax site)
    {
        ExpressionSyntax held = site;
        while (true)
        {
            SyntaxNode parent = held.Parent;
            if (parent is MemberAccessExpressionSyntax member
                && member.Expression == held
                && !IsInvocationCallee(member)
                && IsValueType(semanticModel, held))
            {
                held = member;
                continue;
            }

            if (parent is ElementAccessExpressionSyntax element
                && element.Expression == held
                && IsValueType(semanticModel, held))
            {
                held = element;
                continue;
            }

            if (parent is ArgumentSyntax tupleElement
                && tupleElement.RefKindKeyword.IsKind(SyntaxKind.None)
                && tupleElement.Parent is TupleExpressionSyntax tuple)
            {
                held = tuple;
                continue;
            }

            return held;
        }
    }

    private static SyntaxNode FindHolderOrNull(SemanticModel semanticModel, ExpressionSyntax held)
    {
        SyntaxNode parent = held.Parent;
        // Why only a written keyword: an 'in' argument without it is copied to a temporary first
        // (measured: such a call applies).
        if (parent is ArgumentSyntax argument && !argument.RefKindKeyword.IsKind(SyntaxKind.None))
        {
            return argument.Parent?.Parent;
        }

        if (parent is AssignmentExpressionSyntax assignment && assignment.Left == held)
        {
            return assignment;
        }

        if (parent is MemberAccessExpressionSyntax callee
            && callee.Expression == held
            && callee.Parent is InvocationExpressionSyntax invocation
            && invocation.Expression == callee
            && TakesReceiverByReference(semanticModel, invocation, held))
        {
            return invocation;
        }

        return null;
    }

    // Why a struct's instance method counts: its 'this' is the receiver by reference. Why a reduced
    // extension call counts only with a ref or in 'this': otherwise the receiver is passed by value.
    private static bool TakesReceiverByReference(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        ExpressionSyntax receiver)
    {
        if (AccessibilityRules.ResolvedSymbolOrNull(semanticModel, invocation) is not IMethodSymbol method)
        {
            return false;
        }

        if (method.MethodKind == MethodKind.ReducedExtension)
        {
            IMethodSymbol staticForm = AccessibilityRules.UnreducedMethod(method);
            return staticForm.Parameters.Length > 0 && staticForm.Parameters[0].RefKind != RefKind.None;
        }

        return !method.IsStatic && IsValueType(semanticModel, receiver);
    }

    // Why not into a nested function: its await runs in another body, after this expression is done.
    private static bool HasAwaitAfter(SyntaxNode holder, int position)
    {
        return holder
            .DescendantNodes(node => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .OfType<AwaitExpressionSyntax>()
            .Any(awaitExpression => awaitExpression.SpanStart >= position);
    }

    private static bool IsInvocationCallee(MemberAccessExpressionSyntax member)
    {
        return member.Parent is InvocationExpressionSyntax invocation && invocation.Expression == member;
    }

    private static bool IsValueType(SemanticModel semanticModel, ExpressionSyntax expression)
    {
        return semanticModel.GetTypeInfo(expression).Type?.IsValueType == true;
    }
}
