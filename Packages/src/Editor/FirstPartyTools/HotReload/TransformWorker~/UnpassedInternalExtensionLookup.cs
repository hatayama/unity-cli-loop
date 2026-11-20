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
                    && typeParameter.ConstraintTypes.All(constraint => constraint is INamedTypeSymbol namedConstraint
                        && acceptedThisTypes.Contains(
                            ConstDriftCollector.ToReflectionMetadataName(namedConstraint.OriginalDefinition)));
            default:
                return false;
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
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        for (INamedTypeSymbol type = receiver as INamedTypeSymbol ?? receiver.BaseType; type != null; type = type.BaseType)
        {
            names.Add(ConstDriftCollector.ToReflectionMetadataName(type.OriginalDefinition));
        }

        foreach (INamedTypeSymbol implemented in receiver.AllInterfaces)
        {
            names.Add(ConstDriftCollector.ToReflectionMetadataName(implemented.OriginalDefinition));
        }

        return names;
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
