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
        foreach (AssignmentExpressionSyntax assignment in bodyNode.DescendantNodesAndSelf()
            .OfType<AssignmentExpressionSyntax>())
        {
            if (WritesThroughValueTypeAddedField(semanticModel, assignment.Left, addedFieldCatalog))
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

    // A whole-value reassignment of the field itself stays supported, so only a target reached
    // through at least one member or element step counts as a write into the copy.
    private static bool WritesThroughValueTypeAddedField(
        SemanticModel semanticModel,
        ExpressionSyntax target,
        AddedFieldCatalog addedFieldCatalog)
    {
        ExpressionSyntax receiver = TryGetReceiver(target);
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
