using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Where an expression is written rather than read. Parentheses around a target do not change what
// it is: `(a.P) = 1` sets P exactly as `a.P = 1` does, and so does `(a.P, b) = t`. Every check that
// asks whether an expression is written, or which instance a write reaches, looks through them
// here, because a check that stops at the parentheses reads a write as a read and aims the
// rewritten write at the wrong receiver.
internal static class AssignmentTargetRules
{
    internal static ExpressionSyntax Unparenthesized(ExpressionSyntax expression)
    {
        ExpressionSyntax current = expression;
        while (current is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized.Expression;
        }

        return current;
    }

    /// <summary>
    /// The assignment whose left side is the node, through any parentheses around it, or null.
    /// </summary>
    internal static AssignmentExpressionSyntax AssignmentTargetedBy(SyntaxNode node)
    {
        SyntaxNode target = OutermostParentheses(node);
        return target.Parent is AssignmentExpressionSyntax assignment && assignment.Left == target
            ? assignment
            : null;
    }

    /// <summary>
    /// Whether the node is an element, at any depth and through any parentheses, of a tuple that an
    /// assignment deconstructs into.
    /// </summary>
    internal static bool IsDeconstructionTarget(SyntaxNode node)
    {
        SyntaxNode target = OutermostParentheses(node);
        bool isElement = false;
        while (target.Parent is ArgumentSyntax argument && argument.Parent is TupleExpressionSyntax tuple)
        {
            target = tuple;
            isElement = true;
        }

        return isElement && AssignmentTargetedBy(target) != null;
    }

    private static SyntaxNode OutermostParentheses(SyntaxNode node)
    {
        SyntaxNode current = node;
        while (current.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized;
        }

        return current;
    }
}
