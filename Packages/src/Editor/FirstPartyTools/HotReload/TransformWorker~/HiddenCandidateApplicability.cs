using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// What: a rough answer to whether the arguments of a call, construction or indexer access could
/// fit a candidate member, for a use the binding resolved to nothing. Its candidates are then every
/// member of the name, and one an argument cannot convert to can never be what the patch calls.
/// Anything it cannot tell (a named, ref or error-typed argument, a params parameter, an argument
/// count that differs from the parameter count) counts as fitting, so a caller that refuses fitting
/// candidates fails closed.
/// </summary>
internal static class HiddenCandidateApplicability
{
    /// <summary>
    /// What: false only when an argument of the use cannot convert to the candidate's parameter in
    /// its position.
    /// </summary>
    internal static bool MayApply(SemanticModel enforcingModel, SyntaxNode node, ISymbol candidate)
    {
        BaseArgumentListSyntax argumentList = FindArgumentListOrNull(node);
        ImmutableArray<IParameterSymbol> parameters = ParametersOf(candidate);
        if (argumentList == null || parameters.IsDefault)
        {
            return true;
        }

        List<ExpressionSyntax> arguments = ArgumentExpressionsOrNull(argumentList);
        if (arguments == null)
        {
            return true;
        }

        int comparedCount = System.Math.Min(arguments.Count, parameters.Length);
        for (int index = 0; index < comparedCount; index++)
        {
            // Why a params parameter ends the check: one argument may be its element, which does not
            // convert to the array type the parameter has.
            IParameterSymbol parameter = parameters[index];
            if (parameter.IsParams)
            {
                return true;
            }

            // Why the expression rather than its type: a constant converts to a narrower integral type,
            // null to any reference type, and a lambda to a delegate type, none of which the type says.
            ExpressionSyntax argument = arguments[index];
            ITypeSymbol argumentType = enforcingModel.GetTypeInfo(argument).Type;
            if (argumentType != null && argumentType.TypeKind == TypeKind.Error)
            {
                continue;
            }

            if (!enforcingModel.ClassifyConversion(argument, parameter.Type).IsImplicit)
            {
                return false;
            }
        }

        return true;
    }

    // Why the reduced form as given: a candidate of a call written as 'value.Name(...)' comes already
    // reduced, so its parameters start after the receiver, as the argument list does.
    private static ImmutableArray<IParameterSymbol> ParametersOf(ISymbol candidate)
    {
        if (candidate is IMethodSymbol method)
        {
            return method.Parameters;
        }

        if (candidate is IPropertySymbol property && property.IsIndexer)
        {
            return property.Parameters;
        }

        return default;
    }

    // Why the parent invocation for a name or member access: every node of 'value.Name(...)' reports
    // the same candidates, and only the invocation holds the arguments.
    // Why not for other callees: an indexer access or a call that returns a delegate is invoked by
    // its parent, but its own candidates take the arguments it holds itself.
    private static BaseArgumentListSyntax FindArgumentListOrNull(SyntaxNode node)
    {
        SyntaxNode callee = node;
        if (callee is SimpleNameSyntax name
            && ((name.Parent is MemberAccessExpressionSyntax nameAccess && nameAccess.Name == name)
                || (name.Parent is MemberBindingExpressionSyntax nameBinding && nameBinding.Name == name)))
        {
            callee = name.Parent;
        }

        if ((callee is SimpleNameSyntax || callee is MemberAccessExpressionSyntax || callee is MemberBindingExpressionSyntax)
            && callee.Parent is InvocationExpressionSyntax parentInvocation
            && parentInvocation.Expression == callee)
        {
            return parentInvocation.ArgumentList;
        }

        switch (callee)
        {
            case InvocationExpressionSyntax invocation:
                return invocation.ArgumentList;
            case BaseObjectCreationExpressionSyntax creation:
                return creation.ArgumentList;
            case ElementAccessExpressionSyntax elementAccess:
                return elementAccess.ArgumentList;
            default:
                return null;
        }
    }

    // Why the caller classifies with the enforcing binding: with accessibility ignored, an argument of
    // a BCL type a referenced assembly also declares internally (Memory<T>) has no type, while Unity's
    // binding gives it one.
    private static List<ExpressionSyntax> ArgumentExpressionsOrNull(BaseArgumentListSyntax argumentList)
    {
        List<ExpressionSyntax> expressions = new List<ExpressionSyntax>();
        foreach (ArgumentSyntax argument in argumentList.Arguments)
        {
            if (argument.NameColon != null || !argument.RefKindKeyword.IsKind(SyntaxKind.None))
            {
                return null;
            }

            expressions.Add(argument.Expression);
        }

        return expressions;
    }
}
