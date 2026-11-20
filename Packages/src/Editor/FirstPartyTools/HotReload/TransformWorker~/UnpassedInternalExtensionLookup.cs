using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

// What: finds the internal extension method of the target assembly that an implicit call on a
// receiver binds to, which the receiver's own type cannot name because the extension is declared
// in a file the run was not given.
internal static class UnpassedInternalExtensionLookup
{
    /// <summary>
    /// What: the first internal extension method of the target assembly named 'methodName', with
    /// 'outParameterCount' out parameters when given, whose 'this' parameter takes the receiver;
    /// null when none does.
    /// </summary>
    // Why names and not symbols: the target assembly comes from a separate compilation, so its
    // types are other symbols than the receiver the body binds to.
    internal static IMethodSymbol FindInternalExtensionOrNull(
        ITypeSymbol receiver,
        string methodName,
        int? outParameterCount,
        IAssemblySymbol targetAssembly)
    {
        HashSet<string> acceptedThisTypes = AcceptedThisTypeNames(receiver);
        foreach (INamedTypeSymbol type in EnumerateTypes(targetAssembly.GlobalNamespace))
        {
            if (!type.MightContainExtensionMethods)
            {
                continue;
            }

            foreach (IMethodSymbol method in type.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (method.IsExtensionMethod
                    && UnpassedInternalMemberUse.HasOutParameterCount(method, outParameterCount)
                    && AcceptsReceiver(method.Parameters[0].Type, receiver, acceptedThisTypes)
                    && AccessibilityRules.IsInaccessibleFromExternalAssembly(method))
                {
                    return method;
                }
            }
        }

        return null;
    }

    // Why a type parameter is compared by its constraints: `this T` accepts every receiver its
    // constraints allow, and an unconstrained one accepts any receiver.
    private static bool AcceptsReceiver(ITypeSymbol thisType, ITypeSymbol receiver, HashSet<string> acceptedThisTypes)
    {
        switch (thisType)
        {
            case INamedTypeSymbol named:
                return acceptedThisTypes.Contains(ConstDriftCollector.ToReflectionMetadataName(named.OriginalDefinition));
            // Why the element is compared as the receiver: `this T[]` takes an array of the same
            // rank whose element its element type accepts.
            case IArrayTypeSymbol arrayThisType:
                return receiver is IArrayTypeSymbol arrayReceiver
                    && arrayReceiver.Rank == arrayThisType.Rank
                    && AcceptsReceiver(
                        arrayThisType.ElementType,
                        arrayReceiver.ElementType,
                        AcceptedThisTypeNames(arrayReceiver.ElementType));
            case ITypeParameterSymbol typeParameter:
                return SatisfiesKindConstraints(typeParameter, receiver)
                    && typeParameter.ConstraintTypes.All(constraint => AcceptsConstraint(constraint, receiver, acceptedThisTypes));
            default:
                return false;
        }
    }

    // Whether the receiver satisfies one type constraint of a 'this T' parameter.
    // Why the constructed type only for a named receiver and a closed, invariant constraint: then
    // the compiler needs the receiver to have exactly that type among its own, base and interface
    // types, so 'IMarker<A>' rejects a receiver marked by 'IMarker<B>'. Every other case keeps the
    // match by the generic definition, which never rejects a receiver the compiler accepts:
    // - a constraint that names a type parameter ('IEquatable<T>') is constructed from the receiver;
    // - a variant one ('IProducer<Base>', 'IComparable<T>') is met by other type arguments;
    // - an array converts to 'IList<Base>' of a base element type (array covariance), which is
    //   among none of its interfaces, and a type parameter meets a constraint through its own.
    private static bool AcceptsConstraint(ITypeSymbol constraint, ITypeSymbol receiver, HashSet<string> acceptedThisTypes)
    {
        if (constraint is not INamedTypeSymbol namedConstraint
            || !acceptedThisTypes.Contains(ConstDriftCollector.ToReflectionMetadataName(namedConstraint.OriginalDefinition)))
        {
            return false;
        }

        string constraintKey = receiver is INamedTypeSymbol && !HasVariantTypeParameter(namedConstraint)
            ? IdentityKeyOrNull(namedConstraint)
            : null;
        return constraintKey == null || ReceiverAndSupertypes(receiver).Any(type => IdentityKeyOrNull(type) == constraintKey);
    }

    private static bool HasVariantTypeParameter(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol current = type; current != null; current = current.ContainingType)
        {
            if (current.OriginalDefinition.TypeParameters.Any(parameter => parameter.Variance != VarianceKind.None))
            {
                return true;
            }
        }

        return false;
    }

    // What: a name of the type that the two compilations agree on; null when the type holds a type
    // parameter or a shape the name does not cover, which leaves the definition match in place.
    // Why metadata names and not the display: a tuple's element names are not part of its type, so
    // '(int First, int Second)' and '(int Left, int Right)' must get one name. Why no case for
    // dynamic: C# allows it neither in a constraint nor in a base or interface type.
    private static string IdentityKeyOrNull(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                string elementKey = IdentityKeyOrNull(array.ElementType);
                return elementKey == null ? null : elementKey + "[" + array.Rank + "]";
            case INamedTypeSymbol named:
                List<string> argumentKeys = AllTypeArguments(named).Select(IdentityKeyOrNull).ToList();
                return argumentKeys.Contains(null)
                    ? null
                    : ConstDriftCollector.ToReflectionMetadataName(named.OriginalDefinition)
                        + "<" + string.Join(",", argumentKeys) + ">";
            default:
                return null;
        }
    }

    // Why the containing types' arguments too: 'Outer<A>.Inner' and 'Outer<B>.Inner' share the
    // definition and differ only there.
    private static IEnumerable<ITypeSymbol> AllTypeArguments(INamedTypeSymbol type)
    {
        return type.ContainingType == null
            ? type.TypeArguments
            : AllTypeArguments(type.ContainingType).Concat(type.TypeArguments);
    }

    // The receiver, its base types and its interfaces, as constructed types.
    private static IEnumerable<INamedTypeSymbol> ReceiverAndSupertypes(ITypeSymbol receiver)
    {
        for (INamedTypeSymbol type = receiver as INamedTypeSymbol ?? receiver.BaseType; type != null; type = type.BaseType)
        {
            yield return type;
        }

        foreach (INamedTypeSymbol implemented in receiver.AllInterfaces)
        {
            yield return implemented;
        }
    }

    // The constraints on what kind of type the receiver is: class, struct, unmanaged, and new().
    // Why only a public parameterless constructor counts for new(): the worker sees the receiver's
    // assembly through its public surface, as the body that calls the extension does.
    private static bool SatisfiesKindConstraints(ITypeParameterSymbol typeParameter, ITypeSymbol receiver)
    {
        // Why Nullable<T> fails the struct constraint: C# requires a non-nullable value type there.
        if (typeParameter.HasValueTypeConstraint
            && (!receiver.IsValueType || receiver.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T))
        {
            return false;
        }

        if (typeParameter.HasReferenceTypeConstraint && !receiver.IsReferenceType)
        {
            return false;
        }

        if (typeParameter.HasUnmanagedTypeConstraint && !receiver.IsUnmanagedType)
        {
            return false;
        }

        // Why a type-parameter receiver passes new() by its own constraint: it has no constructors
        // to inspect, and the compiler accepts it when it is itself constrained to new().
        return !typeParameter.HasConstructorConstraint
            || receiver.IsValueType
            || receiver is ITypeParameterSymbol { HasConstructorConstraint: true }
            || (receiver is INamedTypeSymbol { IsAbstract: false } named && named.InstanceConstructors.Any(constructor =>
                constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public));
    }

    // The receiver, its base types and its interfaces, any of which an extension's 'this' may name.
    private static HashSet<string> AcceptedThisTypeNames(ITypeSymbol receiver)
    {
        return new HashSet<string>(
            ReceiverAndSupertypes(receiver)
                .Select(type => ConstDriftCollector.ToReflectionMetadataName(type.OriginalDefinition)),
            StringComparer.Ordinal);
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (INamedTypeSymbol type in namespaceSymbol.GetTypeMembers())
        {
            yield return type;
        }

        foreach (INamespaceSymbol nested in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (INamedTypeSymbol type in EnumerateTypes(nested))
            {
                yield return type;
            }
        }
    }
}
