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
    /// with its keyword, the receiver of a call or an indexer that takes it by reference, or an
    /// assignment target, directly or through a field of a struct or a deconstruction tuple) in an
    /// expression with an await that runs after the field is read.
    /// </summary>
    internal static bool IsHeldAcrossLaterAwait(SemanticModel semanticModel, ExpressionSyntax site)
    {
        ExpressionSyntax held = ClimbThroughTheSameVariable(semanticModel, site);
        // Why the climbed expression on its own: an access that reaches into the field by reference
        // ('_buffer[await t]' on an indexer returning by reference) holds it even with no outer holder.
        if (held != site && HasAwaitAfter(held, site.Span.End))
        {
            return true;
        }

        SyntaxNode holder = FindHolderOrNull(semanticModel, held);
        // Why the site and not the climbed expression: the reference is taken where the field is
        // read, so an await in an index after it ('_indexed[await t] = 1') still runs while it is held.
        return holder != null && HasAwaitAfter(holder, site.Span.End);
    }

    // Why only parentheses, fields and by-reference members: they name a part of the same variable,
    // so '(_point)' and '_point.X' are used by reference when the field is. A property or indexer that
    // returns by value yields a copy, and the use of that copy ends with the call.
    private static ExpressionSyntax ClimbThroughTheSameVariable(SemanticModel semanticModel, ExpressionSyntax site)
    {
        ExpressionSyntax held = site;
        while (true)
        {
            SyntaxNode parent = held.Parent;
            if (parent is ParenthesizedExpressionSyntax parenthesized)
            {
                held = parenthesized;
                continue;
            }

            if (parent is MemberAccessExpressionSyntax member
                && member.Expression == held
                && !IsInvocationCallee(member)
                && IsValueType(semanticModel, held)
                && NamesAPartOfTheVariable(semanticModel, member))
            {
                held = member;
                continue;
            }

            if (parent is ElementAccessExpressionSyntax element
                && element.Expression == held
                && IsValueType(semanticModel, held)
                && NamesAPartOfTheVariable(semanticModel, element))
            {
                held = element;
                continue;
            }

            return held;
        }
    }

    private static SyntaxNode FindHolderOrNull(SemanticModel semanticModel, ExpressionSyntax held)
    {
        AssignmentExpressionSyntax assignment = AssignmentTargetingOrNull(held);
        if (assignment != null)
        {
            return assignment;
        }

        SyntaxNode parent = held.Parent;
        // Why only a written keyword: an 'in' argument without it is copied to a temporary first
        // (measured: such a call applies).
        if (parent is ArgumentSyntax argument && !argument.RefKindKeyword.IsKind(SyntaxKind.None))
        {
            return argument.Parent?.Parent;
        }

        if (parent is MemberAccessExpressionSyntax callee
            && callee.Expression == held
            && callee.Parent is InvocationExpressionSyntax invocation
            && invocation.Expression == callee)
        {
            return TakesReceiverByReference(semanticModel, invocation, held) ? invocation : null;
        }

        return IsValueType(semanticModel, held) ? FindStructAccessorHolderOrNull(held) : null;
    }

    // Why a struct's indexer and setter hold it: their accessor takes the struct by reference, and
    // runs only after its index arguments and the assigned value are evaluated.
    private static SyntaxNode FindStructAccessorHolderOrNull(ExpressionSyntax held)
    {
        SyntaxNode parent = held.Parent;
        if (parent is ElementAccessExpressionSyntax element && element.Expression == held)
        {
            return (SyntaxNode)AssignmentTargetingOrNull(element) ?? element;
        }

        if (parent is MemberAccessExpressionSyntax property && property.Expression == held)
        {
            return AssignmentTargetingOrNull(property);
        }

        return null;
    }

    // Why through tuple elements without a keyword: a deconstruction assigns through each of them.
    private static AssignmentExpressionSyntax AssignmentTargetingOrNull(ExpressionSyntax expression)
    {
        ExpressionSyntax target = expression;
        while (target.Parent is ArgumentSyntax tupleElement
            && tupleElement.RefKindKeyword.IsKind(SyntaxKind.None)
            && tupleElement.Parent is TupleExpressionSyntax tuple)
        {
            target = tuple;
        }

        return target.Parent is AssignmentExpressionSyntax assignment && assignment.Left == target
            ? assignment
            : null;
    }

    private static bool NamesAPartOfTheVariable(SemanticModel semanticModel, ExpressionSyntax access)
    {
        ISymbol symbol = semanticModel.GetSymbolInfo(access).Symbol;
        return symbol is IFieldSymbol
            || symbol is IPropertySymbol { ReturnsByRef: true }
            || symbol is IPropertySymbol { ReturnsByRefReadonly: true };
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
