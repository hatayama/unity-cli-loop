using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Where an assignment writes rather than reads, and which accessor a write calls. Parentheses
// around a target do not change what it is: `(a.P) = 1` sets P exactly as `a.P = 1` does, and so
// does `(a.P, b) = t`. A check that judges the access inside the parentheses by its direct parent
// reads it as a read, and a rewrite that takes the receiver from the direct left side aims the
// write at the running instance, so a check that asks whether a node is assigned, or which
// instance an assignment writes, should ask here. So should a check that asks which accessor a
// write needs: a ref-returning property has no setter, and a check that looks for one finds none.
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

    /// <summary>
    /// Whether the node is a deconstruction element that sets the property through its setter. A
    /// ref-returning property has no setter to call: the deconstruction writes through the
    /// reference its getter returns, the accessor a read of it calls too.
    /// </summary>
    internal static bool IsDeconstructedThroughSetter(IPropertySymbol property, SyntaxNode node)
    {
        return !IsWrittenThroughGetter(property) && IsDeconstructionTarget(node);
    }

    /// <summary>
    /// The accessor through which a write to the property stores its value: the setter, or the
    /// getter of a ref-returning property, whose returned reference the write stores through. A
    /// compound assignment or an increment also reads through the getter; this names only the
    /// store. Null when the property has no such accessor.
    /// </summary>
    internal static IMethodSymbol AccessorCalledByWrite(IPropertySymbol property)
    {
        return IsWrittenThroughGetter(property) ? property.GetMethod : property.SetMethod;
    }

    private static bool IsWrittenThroughGetter(IPropertySymbol property)
    {
        return property.ReturnsByRef || property.ReturnsByRefReadonly;
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
