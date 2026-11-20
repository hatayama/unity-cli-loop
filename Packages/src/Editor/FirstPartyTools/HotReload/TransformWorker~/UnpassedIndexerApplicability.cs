using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

/// <summary>
/// Whether an indexer of an all-members compiled copy accepts a single Index or Range argument,
/// as the shim compile's overload resolution would decide it.
/// </summary>
/// <remarks>
/// Why by display name and not by conversion: the compiled copy lives in a separate compilation
/// that imports every member, so its symbols never equal the worker's, and a conversion cannot be
/// classified across the two. An Index or Range converts only to itself, its nullable form, object,
/// ValueType, dynamic and the interfaces it implements, so those names are the whole set.
/// </remarks>
internal static class UnpassedIndexerApplicability
{
    /// <summary>
    /// Whether <paramref name="indexer"/>, declared by the compiled copy of
    /// <paramref name="constructed"/>, accepts <paramref name="argumentType"/> as its only
    /// argument: its first parameter takes that type, its other parameters are optional, or its
    /// only parameter is a params array of it. A parameter whose type still holds a type parameter
    /// after substitution counts as accepting it, so a skip rather than a run-time failure follows.
    /// </summary>
    internal static bool Accepts(IPropertySymbol indexer, INamedTypeSymbol constructed, ITypeSymbol argumentType)
    {
        if (indexer.Parameters.Length == 0
            || !indexer.Parameters.Skip(1).All(parameter => parameter.IsOptional || parameter.IsParams))
        {
            return false;
        }

        IParameterSymbol first = indexer.Parameters[0];
        HashSet<string> acceptedNames = AcceptedParameterTypeNames(argumentType);
        if (TakesTheArgument(first.Type, constructed, acceptedNames))
        {
            return true;
        }

        return first.IsParams
            && first.Type is IArrayTypeSymbol array
            && TakesTheArgument(array.ElementType, constructed, acceptedNames);
    }

    /// <summary>
    /// Whether two named types from different compilations are the same type definition.
    /// </summary>
    internal static bool HaveTheSameMetadataName(INamedTypeSymbol left, INamedTypeSymbol right)
    {
        return ConstDriftCollector.ToReflectionMetadataName(left.OriginalDefinition)
            == ConstDriftCollector.ToReflectionMetadataName(right.OriginalDefinition);
    }

    private static bool TakesTheArgument(ITypeSymbol parameterType, INamedTypeSymbol constructed, HashSet<string> acceptedNames)
    {
        ITypeSymbol substituted = SubstituteOrNull(parameterType, constructed);
        if (substituted == null || ContainsTypeParameter(substituted))
        {
            return true;
        }

        return acceptedNames.Contains(substituted.ToDisplayString());
    }

    // A type parameter of the declaring type, or of a type containing it, becomes the matching type
    // argument of the worker's constructed type; null when no such argument is found.
    private static ITypeSymbol SubstituteOrNull(ITypeSymbol parameterType, INamedTypeSymbol constructed)
    {
        if (parameterType is not ITypeParameterSymbol typeParameter)
        {
            return parameterType;
        }

        if (typeParameter.TypeParameterKind != TypeParameterKind.Type || typeParameter.DeclaringType == null)
        {
            return null;
        }

        for (INamedTypeSymbol type = constructed; type != null; type = type.ContainingType)
        {
            if (typeParameter.Ordinal < type.TypeArguments.Length
                && HaveTheSameMetadataName(type, typeParameter.DeclaringType))
            {
                return type.TypeArguments[typeParameter.Ordinal];
            }
        }

        return null;
    }

    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        switch (type)
        {
            case ITypeParameterSymbol _:
                return true;
            case IArrayTypeSymbol array:
                return ContainsTypeParameter(array.ElementType);
            case INamedTypeSymbol named:
                return named.TypeArguments.Any(ContainsTypeParameter);
            default:
                return false;
        }
    }

    private static HashSet<string> AcceptedParameterTypeNames(ITypeSymbol argumentType)
    {
        string name = argumentType.ToDisplayString();
        HashSet<string> names = new HashSet<string>(System.StringComparer.Ordinal)
        {
            name,
            name + "?",
            "object",
            "System.ValueType",
            "dynamic",
        };
        foreach (INamedTypeSymbol implemented in argumentType.AllInterfaces)
        {
            names.Add(implemented.ToDisplayString());
        }

        return names;
    }
}
