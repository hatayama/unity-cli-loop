using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// What: finds the internal extension method of the target assembly that an implicit call on a
// receiver binds to, which the receiver's own type cannot name because the extension is declared
// in a file the run was not given.
internal static class UnpassedInternalExtensionLookup
{
    /// <summary>
    /// What: the first internal extension method of the target assembly named 'methodName', with
    /// 'outParameterCount' out parameters when given, whose 'this' parameter takes the receiver,
    /// preferring one whose class the run does not declare; null when none does.
    /// </summary>
    // Why names and not symbols: the target assembly comes from a separate compilation, so its
    // types are other symbols than the receiver the body binds to.
    internal static IMethodSymbol FindInternalExtensionOrNull(
        ITypeSymbol receiver,
        string methodName,
        int? outParameterCount,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly)
    {
        HashSet<string> acceptedThisTypes = AcceptedThisTypeNames(receiver);
        IMethodSymbol firstOfARunDeclaredClass = null;
        foreach (INamedTypeSymbol type in EnumerateTypes(targetAssembly.GlobalNamespace))
        {
            if (!type.MightContainExtensionMethods)
            {
                continue;
            }

            foreach (IMethodSymbol method in type.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (!method.IsExtensionMethod
                    || !UnpassedInternalMemberUse.HasOutParameterCount(method, outParameterCount)
                    || !AcceptsReceiver(method.Parameters[0].Type, receiver, acceptedThisTypes)
                    || !AccessibilityRules.IsInaccessibleFromExternalAssembly(method))
                {
                    continue;
                }

                // Why a class the run declares comes last: the worker binds its edited source, so
                // a method missing there is missing from the edit, while its compiled copy still
                // lists it, and a class the run was not given may declare the method the call
                // means. Why it is still returned when nothing else matches: the shim compile binds
                // that compiled copy, so a closure would call it and throw.
                if (!UnpassedInternalMemberUse.IsDeclaredByTheRun(type, semanticModel))
                {
                    return method;
                }

                firstOfARunDeclaredClass ??= method;
            }
        }

        return firstOfARunDeclaredClass;
    }

    /// <summary>
    /// What: an internal extension method of the target assembly that the reduced call
    /// 'receiver.methodName(...)' may run, found as <see cref="FindInternalExtensionOrNull"/> finds
    /// one; null when the receiver names a type or has no type, or no such extension takes it.
    /// The lookup reads neither using directives nor overloads, so with several candidates it may
    /// name another one than the compiler calls.
    /// </summary>
    internal static IMethodSymbol FindReducedCallTargetOrNull(
        ExpressionSyntax receiver,
        string methodName,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly)
    {
        // Why not a type: 'Type.Name(...)' calls a static member of the type, never an extension.
        if (receiver == null || semanticModel.GetSymbolInfo(receiver).Symbol is ITypeSymbol)
        {
            return null;
        }

        ITypeSymbol receiverType = semanticModel.GetTypeInfo(receiver).Type;
        if (receiverType == null)
        {
            return null;
        }

        // Why Nullable<T> is unwrapped after '?.': the call then runs on the underlying value, so
        // the extension that takes it is the one the compiler calls.
        if (receiver.Parent is ConditionalAccessExpressionSyntax conditional
            && conditional.Expression == receiver
            && receiverType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            receiverType = nullable.TypeArguments[0];
        }

        return FindInternalExtensionOrNull(receiverType, methodName, outParameterCount: null, semanticModel, targetAssembly);
    }

    // Why a type parameter is compared by its constraints: `this T` accepts every receiver its
    // constraints allow, and an unconstrained one accepts any receiver.
    private static bool AcceptsReceiver(ITypeSymbol thisType, ITypeSymbol receiver, HashSet<string> acceptedThisTypes)
    {
        switch (thisType)
        {
            case INamedTypeSymbol named:
                return ReceiverConvertsTo(named, receiver, acceptedThisTypes);
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
                    && typeParameter.ConstraintTypes.All(constraint => ReceiverConvertsTo(constraint, receiver, acceptedThisTypes));
            default:
                return false;
        }
    }

    // Whether the receiver converts to 'target' by identity, reference or boxing, which is what the
    // compiler asks of the receiver for a named 'this' parameter type and for each type constraint
    // of a 'this T' parameter.
    // Why the constructed type only for a named receiver and a closed, invariant target: then the
    // compiler needs the receiver to have exactly that type among its own, base and interface
    // types, so 'IMarker<A>' rejects a receiver marked by 'IMarker<B>'. Every other case keeps the
    // match by the generic definition, which never rejects a receiver the compiler accepts:
    // - a target that names a type parameter ('IEquatable<T>') is constructed from the receiver;
    // - a variant one ('IProducer<Base>', 'IComparable<T>') is met by other type arguments;
    // - an array converts to 'IList<Base>' of a base element type (array covariance), which is
    //   among none of its interfaces, and a type parameter meets a target through its constraints.
    private static bool ReceiverConvertsTo(ITypeSymbol target, ITypeSymbol receiver, HashSet<string> acceptedThisTypes)
    {
        if (target is not INamedTypeSymbol namedTarget
            || !acceptedThisTypes.Contains(ConstDriftCollector.ToReflectionMetadataName(namedTarget.OriginalDefinition)))
        {
            return false;
        }

        string targetKey = receiver is INamedTypeSymbol && !HasVariantTypeParameter(namedTarget)
            ? IdentityKeyOrNull(namedTarget)
            : null;
        return targetKey == null || ReceiverAndSupertypes(receiver).Any(type => IdentityKeyOrNull(type) == targetKey);
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

    // What: a name of the type that the two compilations agree on and that two types related by an
    // identity conversion share; null when the type holds a type parameter or a shape the name does
    // not cover, which leaves the definition match in place.
    // Why metadata names and not the display: a tuple's element names are not part of its type, so
    // '(int First, int Second)' and '(int Left, int Right)' must get one name. Why dynamic is named
    // as object: a receiver typed 'IMarker<dynamic>' meets 'IMarker<object>'.
    private static string IdentityKeyOrNull(ITypeSymbol type)
    {
        switch (type)
        {
            case IDynamicTypeSymbol _:
                return "System.Object";
            case IArrayTypeSymbol array:
                string elementKey = IdentityKeyOrNull(array.ElementType);
                return elementKey == null ? null : elementKey + "[" + array.Rank + "]";
            case INamedTypeSymbol named:
                List<string> argumentKeys = AllTypeArguments(named).Select(IdentityKeyOrNull).ToList();
                string definitionName = ConstDriftCollector.ToReflectionMetadataName(named.OriginalDefinition);
                if (argumentKeys.Contains(null))
                {
                    return null;
                }

                return argumentKeys.Count == 0 ? definitionName : definitionName + "<" + string.Join(",", argumentKeys) + ">";
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
