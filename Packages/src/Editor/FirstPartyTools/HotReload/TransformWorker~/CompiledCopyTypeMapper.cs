using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

/// <summary>
/// What: the worker compilation's symbol for a type read from the all-members compiled copy of the
/// target assembly, so a conversion to it can be classified in the worker compilation. The copy is a
/// separate compilation, and its symbols never equal the worker's. Only an exact match maps: the
/// same assembly identity, metadata name and generic arity; anything else maps to null, which the
/// caller treats as a type any argument may convert to.
/// </summary>
internal static class CompiledCopyTypeMapper
{
    /// <summary>
    /// What: the worker compilation's form of <paramref name="type"/>, or null when it has none
    /// that matches exactly. A type of the target assembly that the run declares from source maps
    /// to the run's declaration, since the arguments of the edited source bind to that one.
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
