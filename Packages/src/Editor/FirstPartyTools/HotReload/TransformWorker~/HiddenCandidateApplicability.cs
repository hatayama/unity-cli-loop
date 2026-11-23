using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// What: a rough answer to whether the arguments of a call, construction or indexer access could
/// fit a candidate member, for a use the binding resolved to nothing (its candidates are then every
/// member of the name) or a private member of a compiled type the run was not given. A candidate an
/// argument cannot convert to can never be what the patch calls.
/// Anything it cannot tell (a named, ref or error-typed argument, a params parameter, a parameter
/// type it cannot map) counts as fitting, so a caller that refuses fitting candidates fails closed.
/// </summary>
internal static class HiddenCandidateApplicability
{
    /// <summary>
    /// What: false only when the use's positional arguments are too few or too many for the
    /// candidate's parameters, or an argument cannot convert to the candidate's parameter in its
    /// position.
    /// </summary>
    internal static bool MayApply(SemanticModel enforcingModel, SyntaxNode node, ISymbol candidate)
    {
        ImmutableArray<IParameterSymbol> parameters = ParametersOf(candidate);
        if (parameters.IsDefault)
        {
            return true;
        }

        List<CandidateParameter> shapes = new List<CandidateParameter>(parameters.Length);
        foreach (IParameterSymbol parameter in parameters)
        {
            shapes.Add(new CandidateParameter(parameter.Type, parameter.IsOptional, parameter.IsParams));
        }

        return MayApplyToParameters(enforcingModel, node, shapes);
    }

    /// <summary>
    /// What: MayApply for a candidate given by its parameters, for a candidate whose parameter types
    /// come from another compilation and had to be mapped into the use's compilation first. A
    /// parameter without a type takes any argument.
    /// </summary>
    internal static bool MayApplyToParameters(
        SemanticModel enforcingModel,
        SyntaxNode node,
        IReadOnlyList<CandidateParameter> parameters)
    {
        BaseArgumentListSyntax argumentList = FindArgumentListOrNull(node);
        if (argumentList == null)
        {
            return true;
        }

        List<ExpressionSyntax> arguments = ArgumentExpressionsOrNull(argumentList);
        if (arguments == null)
        {
            return true;
        }

        if (!FitsTheArgumentCount(arguments.Count, parameters))
        {
            return false;
        }

        int comparedCount = System.Math.Min(arguments.Count, parameters.Count);
        for (int index = 0; index < comparedCount; index++)
        {
            // Why a params parameter ends the check: one argument may be its element, which does not
            // convert to the array type the parameter has.
            CandidateParameter parameter = parameters[index];
            if (parameter.IsParams)
            {
                return true;
            }

            if (parameter.TypeOrNull == null)
            {
                continue;
            }

            // Why the expression rather than its type: a constant converts to a narrower integral type,
            // null to any reference type, and a lambda to a delegate type, none of which the type says.
            ExpressionSyntax argument = arguments[index];
            ITypeSymbol argumentType = enforcingModel.GetTypeInfo(argument).Type;
            if (argumentType != null && argumentType.TypeKind == TypeKind.Error)
            {
                continue;
            }

            if (!enforcingModel.ClassifyConversion(argument, parameter.TypeOrNull).IsImplicit)
            {
                return false;
            }
        }

        return true;
    }

    // Why a params parameter takes any count from the required ones up: in its expanded form it
    // takes zero or more elements, and in its normal form one array.
    // Why optional parameters only lower the minimum: an argument left out takes the default value,
    // while every argument still needs a parameter of its own.
    private static bool FitsTheArgumentCount(int argumentCount, IReadOnlyList<CandidateParameter> parameters)
    {
        int requiredCount = 0;
        bool endsWithParams = false;
        foreach (CandidateParameter parameter in parameters)
        {
            if (parameter.IsParams)
            {
                endsWithParams = true;
                continue;
            }

            if (!parameter.IsOptional)
            {
                requiredCount++;
            }
        }

        if (argumentCount < requiredCount)
        {
            return false;
        }

        return endsWithParams || argumentCount <= parameters.Count;
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

/// <summary>
/// What: one parameter of a candidate as the applicability check reads it. A null type stands for a
/// parameter type that could not be expressed in the use's compilation, which takes any argument.
/// </summary>
internal readonly struct CandidateParameter
{
    internal CandidateParameter(ITypeSymbol typeOrNull, bool isOptional, bool isParams)
    {
        TypeOrNull = typeOrNull;
        IsOptional = isOptional;
        IsParams = isParams;
    }

    internal ITypeSymbol TypeOrNull { get; }

    internal bool IsOptional { get; }

    internal bool IsParams { get; }
}
