using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

// Turns a bound symbol into the set of type identities a declaration depends on. Collapsing a
// symbol to a single type would lose the types that only appear inside it: the element type of an
// array, the type arguments of a constructed generic, and the signature types of a referenced
// member. A definition that changes only there has to change the fingerprint too.
internal sealed class IntroducedTypeDependencyWalker
{
    private readonly IAssemblySymbol compilationAssembly;
    private readonly IAssemblySymbol targetAssembly;
    private readonly string targetAssemblyName;
    private readonly string targetAssemblyMvid;
    private readonly IntroducedTypeArtifactMap artifactMap;

    internal IntroducedTypeDependencyWalker(
        IAssemblySymbol compilationAssembly,
        IAssemblySymbol targetAssembly,
        string targetAssemblyName,
        string targetAssemblyMvid,
        IntroducedTypeArtifactMap artifactMap)
    {
        this.compilationAssembly = compilationAssembly;
        this.targetAssembly = targetAssembly;
        this.targetAssemblyName = targetAssemblyName;
        this.targetAssemblyMvid = targetAssemblyMvid;
        this.artifactMap = artifactMap;
    }

    // Adds every type identity reachable from the symbol. The visited set is per call, so the same
    // type reached through two different symbols is still recorded once per dependency set.
    internal void AddDependencies(ISymbol symbol, HashSet<string> dependencies)
    {
        HashSet<ISymbol> visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        Expand(symbol, dependencies, visited);
    }

    private void Expand(ISymbol symbol, HashSet<string> dependencies, HashSet<ISymbol> visited)
    {
        if (symbol == null || !visited.Add(symbol))
        {
            return;
        }

        if (symbol is IArrayTypeSymbol arrayType)
        {
            Expand(arrayType.ElementType, dependencies, visited);
            return;
        }

        if (symbol is IPointerTypeSymbol pointerType)
        {
            Expand(pointerType.PointedAtType, dependencies, visited);
            return;
        }

        if (symbol is ITypeParameterSymbol typeParameter)
        {
            foreach (ITypeSymbol constraintType in typeParameter.ConstraintTypes)
            {
                Expand(constraintType, dependencies, visited);
            }

            return;
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            ExpandNamedType(namedType, dependencies, visited);
            return;
        }

        ExpandMember(symbol, dependencies, visited);
    }

    private void ExpandNamedType(
        INamedTypeSymbol namedType,
        HashSet<string> dependencies,
        HashSet<ISymbol> visited)
    {
        AddIdentity(namedType, dependencies);
        foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
        {
            Expand(typeArgument, dependencies, visited);
        }
    }

    private void ExpandMember(ISymbol symbol, HashSet<string> dependencies, HashSet<ISymbol> visited)
    {
        Expand(symbol.ContainingType, dependencies, visited);
        if (symbol is IMethodSymbol method)
        {
            ExpandMethod(method, dependencies, visited);
            return;
        }

        if (symbol is IPropertySymbol property)
        {
            Expand(property.Type, dependencies, visited);
            foreach (IParameterSymbol parameter in property.Parameters)
            {
                Expand(parameter.Type, dependencies, visited);
            }

            return;
        }

        if (symbol is IFieldSymbol field)
        {
            Expand(field.Type, dependencies, visited);
            return;
        }

        if (symbol is IEventSymbol eventSymbol)
        {
            Expand(eventSymbol.Type, dependencies, visited);
        }
    }

    private void ExpandMethod(IMethodSymbol method, HashSet<string> dependencies, HashSet<ISymbol> visited)
    {
        Expand(method.ReturnType, dependencies, visited);
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            Expand(parameter.Type, dependencies, visited);
        }

        foreach (ITypeSymbol typeArgument in method.TypeArguments)
        {
            Expand(typeArgument, dependencies, visited);
        }
    }

    private void AddIdentity(INamedTypeSymbol namedType, HashSet<string> dependencies)
    {
        IAssemblySymbol containingAssembly = namedType.ContainingAssembly;
        if (containingAssembly == null)
        {
            return;
        }

        string metadataName = CecilTypeNames.ToMetadataName(namedType.OriginalDefinition);

        // A type that already lives in a retained artifact is the same definition it was when it
        // was still a source declaration, so it has to fingerprint as the assembly its source
        // belongs to. Binding through the artifact assembly identity instead would invalidate
        // every dependent declaration the moment the type it depends on is introduced.
        string normalizedIdentity = artifactMap.FindNormalizedIdentity(containingAssembly, metadataName);
        if (normalizedIdentity != null)
        {
            dependencies.Add(normalizedIdentity);
            return;
        }

        if (SymbolEqualityComparer.Default.Equals(containingAssembly, compilationAssembly)
            || SymbolEqualityComparer.Default.Equals(containingAssembly, targetAssembly))
        {
            dependencies.Add((targetAssemblyName ?? string.Empty)
                + "|" + (targetAssemblyMvid ?? string.Empty)
                + "|" + metadataName);
            return;
        }

        dependencies.Add(containingAssembly.Identity.GetDisplayName() + "|" + metadataName);
    }
}
