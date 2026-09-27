using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Recognizes a write that reaches an added field through a value-type member, which the shim
// cannot rewrite. Split from the rest of the body scan because it has to walk back along a
// receiver chain, while every other check reads a single node.
internal static class AddedFieldValueTypeWriteScan
{
    internal static bool BodyHasValueTypeAddedFieldMemberWrite(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (ExpressionSyntax target in AssignedTargets(bodyNode))
        {
            if (WritesThroughValueTypeAddedField(semanticModel, target, addedFieldCatalog))
            {
                return true;
            }
        }

        foreach (PrefixUnaryExpressionSyntax prefix in bodyNode.DescendantNodesAndSelf()
            .OfType<PrefixUnaryExpressionSyntax>())
        {
            if (AddedFieldBodyScan.IsIncrementOrDecrement(prefix.Kind())
                && WritesThroughValueTypeAddedField(semanticModel, prefix.Operand, addedFieldCatalog))
            {
                return true;
            }
        }

        foreach (PostfixUnaryExpressionSyntax postfix in bodyNode.DescendantNodesAndSelf()
            .OfType<PostfixUnaryExpressionSyntax>())
        {
            if (AddedFieldBodyScan.IsIncrementOrDecrement(postfix.Kind())
                && WritesThroughValueTypeAddedField(semanticModel, postfix.Operand, addedFieldCatalog))
            {
                return true;
            }
        }

        foreach (InvocationExpressionSyntax invocation in bodyNode.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>())
        {
            if (NameofRules.IsNameofInvocation(invocation)
                || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                continue;
            }

            ISymbol invoked = semanticModel.GetSymbolInfo(invocation).Symbol;
            if (invoked is IMethodSymbol methodSymbol
                && !methodSymbol.IsStatic
                && ReachesValueTypeAddedFieldRoot(semanticModel, memberAccess.Expression, addedFieldCatalog))
            {
                return true;
            }
        }

        return false;
    }

    // Every expression an assignment writes: its left side, and each element of a tuple it
    // deconstructs into, which it writes just as it writes a left side.
    private static IEnumerable<ExpressionSyntax> AssignedTargets(SyntaxNode bodyNode)
    {
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (node is AssignmentExpressionSyntax assignment)
            {
                yield return assignment.Left;
            }
            else if (node is ExpressionSyntax expression && AssignmentTargetRules.IsDeconstructionTarget(expression))
            {
                yield return expression;
            }
        }
    }

    // A whole-value reassignment of the field itself stays supported, so only a target reached
    // through at least one member or element step counts as a write into the copy. Parentheses
    // around the target are no such step: `(a.F) = v` reassigns F exactly as `a.F = v` does.
    private static bool WritesThroughValueTypeAddedField(
        SemanticModel semanticModel,
        ExpressionSyntax target,
        AddedFieldCatalog addedFieldCatalog)
    {
        ExpressionSyntax receiver = TryGetReceiver(AssignmentTargetRules.Unparenthesized(target));
        return receiver != null
            && ReachesValueTypeAddedFieldRoot(semanticModel, receiver, addedFieldCatalog);
    }

    // Why walk the whole chain: the store hands back a copy of the field, so a write reached
    // through any number of further steps is lost, no matter how deep the member or element
    // access that names it sits.
    private static bool ReachesValueTypeAddedFieldRoot(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        AddedFieldCatalog addedFieldCatalog)
    {
        ExpressionSyntax current = expression;
        while (current != null)
        {
            if (AddedFieldBodyScan.IsStoreAddedValueTypeField(semanticModel, current, addedFieldCatalog))
            {
                return true;
            }

            current = TryGetReceiver(current);
        }

        return false;
    }

    private static ExpressionSyntax TryGetReceiver(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess.Expression;
            case ElementAccessExpressionSyntax elementAccess:
                return elementAccess.Expression;
            case ParenthesizedExpressionSyntax parenthesized:
                return parenthesized.Expression;
            default:
                return null;
        }
    }
}
