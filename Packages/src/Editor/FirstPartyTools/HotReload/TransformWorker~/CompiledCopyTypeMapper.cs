using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

/// <summary>
/// What: maps a type that a member of the target assembly's compiled metadata names into the form
/// the edited source binds to. A type the run declares from source exists twice in the worker's
/// compilations, as the run's declaration and as its compiled copy, and the edited source binds to
/// the declaration, so a parameter naming the copy takes no argument of it, while the patch, which
/// knows one type, does. Only an exact match maps: the same assembly identity, metadata name and
/// generic arity; anything else maps to null, which the caller treats as a type any argument may
/// convert to.
/// </summary>
internal static class CompiledCopyTypeMapper
{
    /// <summary>
    /// What: <paramref name="compilation"/>'s form of <paramref name="type"/>, or null when it has
    /// none that matches exactly. A type of the target assembly that the run declares from source
    /// maps to the run's declaration, since the arguments of the edited source bind to that one.
    /// </summary>
    internal static ITypeSymbol MapOrNull(
        ITypeSymbol type,
        Compilation compilation,
        IAssemblySymbol targetAssembly)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                ITypeSymbol element = MapOrNull(array.ElementType, compilation, targetAssembly);
                return element == null ? null : compilation.CreateArrayTypeSymbol(element, array.Rank);
            case INamedTypeSymbol named:
                return MapNamedOrNull(named, compilation, targetAssembly);
            default:
                // Why a type parameter, pointer or dynamic maps to nothing: what it stands for at
                // the use is not known here, so the caller lets any argument fit it.
                return null;
        }
    }

    /// <summary>
    /// What: whether a parameter of <paramref name="member"/> names, in itself, an array element,
    /// a type argument or a containing type, a type of the target assembly that the run declares
    /// from source.
    /// </summary>
    internal static bool HasParameterTypeTheRunDeclares(
        ISymbol member,
        Compilation compilation,
        IAssemblySymbol targetAssembly)
    {
        ImmutableArray<IParameterSymbol> parameters = member switch
        {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol property => property.Parameters,
            _ => ImmutableArray<IParameterSymbol>.Empty
        };
        foreach (IParameterSymbol parameter in parameters)
        {
            if (MentionsTypeTheRunDeclares(parameter.Type, compilation, targetAssembly))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MentionsTypeTheRunDeclares(
        ITypeSymbol type,
        Compilation compilation,
        IAssemblySymbol targetAssembly)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return MentionsTypeTheRunDeclares(array.ElementType, compilation, targetAssembly);
            case INamedTypeSymbol named:
                if (IsDeclaredByTheRun(named.OriginalDefinition, compilation, targetAssembly))
                {
                    return true;
                }

                foreach (ITypeSymbol argument in named.TypeArguments)
                {
                    if (MentionsTypeTheRunDeclares(argument, compilation, targetAssembly))
                    {
                        return true;
                    }
                }

                // Why the containing type too: a type nested in a generic type carries the
                // containing type's type arguments there, not in its own.
                return named.ContainingType != null
                    && MentionsTypeTheRunDeclares(named.ContainingType, compilation, targetAssembly);
            default:
                return false;
        }
    }

    // Why the compilation's own assembly: it holds only the types the run declares from source.
    private static bool IsDeclaredByTheRun(
        INamedTypeSymbol definition,
        Compilation compilation,
        IAssemblySymbol targetAssembly)
    {
        return targetAssembly != null
            && definition.ContainingAssembly != null
            && definition.ContainingAssembly.Identity.Equals(targetAssembly.Identity)
            && compilation.Assembly.GetTypeByMetadataName(ConstDriftCollector.ToReflectionMetadataName(definition)) != null;
    }

    // Why a type nested in a generic type maps to nothing: its own type arguments leave out the
    // containing type's, so the constructed form could not be rebuilt exactly.
    private static INamedTypeSymbol MapNamedOrNull(
        INamedTypeSymbol named,
        Compilation compilation,
        IAssemblySymbol targetAssembly)
    {
        if (named.TypeKind == TypeKind.Error || HasGenericContainingType(named))
        {
            return null;
        }

        INamedTypeSymbol definition = named.OriginalDefinition;
        INamedTypeSymbol mappedDefinition = FindDefinitionOrNull(definition, compilation, targetAssembly);
        if (mappedDefinition == null || mappedDefinition.Arity != definition.Arity)
        {
            return null;
        }

        if (definition.Arity == 0)
        {
            return mappedDefinition;
        }

        List<ITypeSymbol> mappedArguments = new List<ITypeSymbol>(named.TypeArguments.Length);
        foreach (ITypeSymbol argument in named.TypeArguments)
        {
            ITypeSymbol mappedArgument = MapOrNull(argument, compilation, targetAssembly);
            if (mappedArgument == null)
            {
                return null;
            }

            mappedArguments.Add(mappedArgument);
        }

        return SymbolEqualityComparer.Default.Equals(named, definition)
            ? mappedDefinition
            : mappedDefinition.Construct(mappedArguments.ToArray());
    }

    // Why the reflection form of the name: GetTypeByMetadataName nests a type with '+', not with the
    // '/' of metadata names or the '.' of source.
    private static INamedTypeSymbol FindDefinitionOrNull(
        INamedTypeSymbol definition,
        Compilation compilation,
        IAssemblySymbol targetAssembly)
    {
        AssemblyIdentity identity = definition.ContainingAssembly?.Identity;
        if (identity == null)
        {
            return null;
        }

        string metadataName = ConstDriftCollector.ToReflectionMetadataName(definition);
        if (targetAssembly != null && identity.Equals(targetAssembly.Identity))
        {
            INamedTypeSymbol declaredByTheRun = compilation.Assembly.GetTypeByMetadataName(metadataName);
            if (declaredByTheRun != null)
            {
                return declaredByTheRun;
            }
        }

        ImmutableArray<IAssemblySymbol> referenced = compilation.SourceModule.ReferencedAssemblySymbols;
        foreach (IAssemblySymbol assembly in referenced)
        {
            if (assembly.Identity.Equals(identity))
            {
                return assembly.GetTypeByMetadataName(metadataName);
            }
        }

        return null;
    }

    private static bool HasGenericContainingType(INamedTypeSymbol named)
    {
        for (INamedTypeSymbol current = named.ContainingType; current != null; current = current.ContainingType)
        {
            if (current.IsGenericType)
            {
                return true;
            }
        }

        return false;
    }
}
