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
/// ValueType, dynamic, the interfaces it implements, and a type that declares its own implicit
/// conversion from it, so those are the whole set.
/// </remarks>
internal static class UnpassedIndexerApplicability
{
    /// <summary>
    /// Whether <paramref name="indexer"/>, declared by the compiled copy of
    /// <paramref name="constructed"/>, accepts <paramref name="argumentType"/> as its only
    /// argument: its first parameter takes that type, its other parameters are optional, or its
    /// only parameter is a params array of it. A parameter that is still a type parameter after
    /// substitution, or a nullable form or an interface built on one, counts as accepting it, so a
    /// skip rather than a run-time failure follows.
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
        if (TakesTheArgument(first.Type, constructed, argumentType, acceptedNames))
        {
            return true;
        }

        return first.IsParams
            && first.Type is IArrayTypeSymbol array
            && TakesTheArgument(array.ElementType, constructed, argumentType, acceptedNames);
    }

    /// <summary>
    /// Whether two named types from different compilations are the same type definition.
    /// </summary>
    internal static bool HaveTheSameMetadataName(INamedTypeSymbol left, INamedTypeSymbol right)
    {
        return ConstDriftCollector.ToReflectionMetadataName(left.OriginalDefinition)
            == ConstDriftCollector.ToReflectionMetadataName(right.OriginalDefinition);
    }

    private static bool TakesTheArgument(
        ITypeSymbol parameterType,
        INamedTypeSymbol constructed,
        ITypeSymbol argumentType,
        HashSet<string> acceptedNames)
    {
        ITypeSymbol substituted = SubstituteOrNull(parameterType, constructed);
        if (substituted == null || DeclaresImplicitConversionFrom(substituted, constructed, argumentType))
        {
            return true;
        }

        if (ContainsTypeParameter(substituted))
        {
            return MayStillTakeTheArgument(substituted);
        }

        return acceptedNames.Contains(substituted.ToDisplayString());
    }

    // Why only these shapes: an Index or Range is a struct, which converts to a type parameter it may
    // stand for, its nullable form or an interface, but never to an array or another generic class,
    // whatever the type arguments are.
    private static bool MayStillTakeTheArgument(ITypeSymbol substituted)
    {
        if (substituted is ITypeParameterSymbol)
        {
            return true;
        }

        return substituted is INamedTypeSymbol named
            && (named.TypeKind == TypeKind.Interface
                || named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);
    }

    // Why only conversions the target type itself declares, returning it: a user-defined conversion
    // converts to or from the type that declares it, so one inherited from a base type yields the
    // base type, which converts to the parameter type only explicitly. Why the nullable form's
    // underlying type: a conversion to T also converts to T?, and one to T? only to T?. Why the
    // argument's nullable form too: a conversion from Index? also takes an Index. Why the
    // conversion's own parameter is substituted as well: a generic key converting from its type
    // argument takes an Index only once the receiver's type argument is known.
    private static bool DeclaresImplicitConversionFrom(
        ITypeSymbol parameterType,
        INamedTypeSymbol constructed,
        ITypeSymbol argumentType)
    {
        bool isNullable = parameterType is INamedTypeSymbol nullable
            && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        ITypeSymbol target = isNullable ? ((INamedTypeSymbol)parameterType).TypeArguments[0] : parameterType;
        string targetName = target.ToDisplayString();
        string argumentName = argumentType.ToDisplayString();
        return target.GetMembers(WellKnownMemberNames.ImplicitConversionName)
            .OfType<IMethodSymbol>()
            .Any(conversion => conversion.Parameters.Length == 1
                && MayConvertFrom(SubstituteOrNull(conversion.Parameters[0].Type, constructed), argumentName)
                && (conversion.ReturnType.ToDisplayString() == targetName
                    || isNullable && conversion.ReturnType.ToDisplayString() == targetName + "?"));
    }

    // Why a type still holding a type parameter counts: what it stands for is unknown here, so a
    // skip rather than a run-time failure follows.
    private static bool MayConvertFrom(ITypeSymbol conversionSource, string argumentName)
    {
        return conversionSource == null
            || ContainsTypeParameter(conversionSource)
            || IsTypeOrItsNullableForm(conversionSource, argumentName);
    }

    private static bool IsTypeOrItsNullableForm(ITypeSymbol type, string name)
    {
        string displayed = type.ToDisplayString();
        return displayed == name || displayed == name + "?";
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
