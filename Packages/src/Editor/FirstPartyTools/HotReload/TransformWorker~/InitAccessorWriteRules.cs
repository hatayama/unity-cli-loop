using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// What: finds writes in an init accessor body that only the declaring type's own constructors and
// init accessors may make: a readonly field or an init-only property as the target of an
// assignment, a compound assignment, ++/--, a deconstruction, a ref/out argument, or a ref
// expression, or as a value-type receiver a write goes through (Position.x = 1 writes Position).
// Why these refuse the accessor: the shim that replaces the body is compiled outside the type, so
// such a write does not compile there (CS0191 / CS1648 / CS0192 for a readonly field, CS8852 for an
// init-only property) and would fail the whole file instead of skipping one accessor.
internal static class InitAccessorWriteRules
{
    internal static bool WritesInitOnlyMember(SyntaxNode bodyNode, SemanticModel semanticModel)
    {
        foreach (ExpressionSyntax target in CollectWriteTargets(bodyNode))
        {
            if (WritesThroughInitOnlyMember(target, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<ExpressionSyntax> CollectWriteTargets(SyntaxNode bodyNode)
    {
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            switch (node)
            {
                // Why not a member set inside an object or with initializer: an initializer may set
                // init-only members of the object it builds from anywhere, so the shim compiles it.
                case AssignmentExpressionSyntax assignment when !IsInitializerMemberSet(assignment):
                    foreach (ExpressionSyntax target in ExpandDeconstruction(assignment.Left))
                    {
                        yield return target;
                    }

                    break;
                case PrefixUnaryExpressionSyntax prefix when IsIncrementOrDecrement(prefix.Kind()):
                    yield return prefix.Operand;
                    break;
                case PostfixUnaryExpressionSyntax postfix when IsIncrementOrDecrement(postfix.Kind()):
                    yield return postfix.Operand;
                    break;
                case ArgumentSyntax argument when argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)
                    || argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword):
                    yield return argument.Expression;
                    break;
                // Why every ref expression, ref readonly locals included: refusing a readable ref
                // only skips the accessor, while missing a writable one fails the whole file.
                case RefExpressionSyntax reference:
                    yield return reference.Expression;
                    break;
            }
        }
    }

    // What: whether the write target, or a value-type receiver the write goes through, is a readonly
    // field or an init-only property. Why the receivers: writing a member of a struct stored in a
    // readonly field writes the field itself.
    private static bool WritesThroughInitOnlyMember(ExpressionSyntax writeTarget, SemanticModel semanticModel)
    {
        // Why unwrap: parentheses do not change which storage a write reaches.
        ExpressionSyntax target = AssignmentTargetRules.Unparenthesized(writeTarget);
        if (IsInitOnlyMember(semanticModel.GetSymbolInfo(target).Symbol))
        {
            return true;
        }

        // Why stop at the first receiver that is not a value-type member: past a class reference the
        // write changes the referenced object, not the storage that holds the reference.
        ExpressionSyntax receiver = Unparenthesized((target as MemberAccessExpressionSyntax)?.Expression);
        while (receiver is MemberAccessExpressionSyntax or IdentifierNameSyntax)
        {
            ISymbol symbol = semanticModel.GetSymbolInfo(receiver).Symbol;
            if (!IsValueTypeMember(symbol))
            {
                return false;
            }

            if (IsInitOnlyMember(symbol))
            {
                return true;
            }

            receiver = Unparenthesized((receiver as MemberAccessExpressionSyntax)?.Expression);
        }

        return false;
    }

    private static ExpressionSyntax Unparenthesized(ExpressionSyntax expression)
    {
        return expression == null ? null : AssignmentTargetRules.Unparenthesized(expression);
    }

    private static bool IsValueTypeMember(ISymbol symbol)
    {
        return symbol switch
        {
            IFieldSymbol field => field.Type.IsValueType,
            IPropertySymbol property => property.Type.IsValueType,
            _ => false
        };
    }

    // What: the assigned expressions of a deconstruction target such as (a, (b, c)), or the target
    // itself when it is not a tuple.
    private static IEnumerable<ExpressionSyntax> ExpandDeconstruction(ExpressionSyntax target)
    {
        if (target is not TupleExpressionSyntax tuple)
        {
            yield return target;
            yield break;
        }

        foreach (ArgumentSyntax argument in tuple.Arguments)
        {
            foreach (ExpressionSyntax inner in ExpandDeconstruction(argument.Expression))
            {
                yield return inner;
            }
        }
    }

    private static bool IsInitializerMemberSet(AssignmentExpressionSyntax assignment)
    {
        return assignment.Parent is InitializerExpressionSyntax initializer
            && (initializer.IsKind(SyntaxKind.ObjectInitializerExpression)
                || initializer.IsKind(SyntaxKind.WithInitializerExpression));
    }

    private static bool IsIncrementOrDecrement(SyntaxKind kind)
    {
        return kind == SyntaxKind.PreIncrementExpression
            || kind == SyntaxKind.PreDecrementExpression
            || kind == SyntaxKind.PostIncrementExpression
            || kind == SyntaxKind.PostDecrementExpression;
    }

    private static bool IsInitOnlyMember(ISymbol symbol)
    {
        return symbol switch
        {
            IFieldSymbol field => field.IsReadOnly,
            IPropertySymbol property => property.SetMethod != null && property.SetMethod.IsInitOnly,
            _ => false
        };
    }
}
