using System.Collections.Generic;
using Microsoft.CodeAnalysis;

// Assembly-constrained virtual slots do not become overrideable merely by exposing reference
// metadata. Only a base introduced in this same artifact shares the implementation's assembly.
internal static class IntroducedTypeInternalOverrideGuard
{
    internal static bool TryFindUnsupportedOverride(
        INamedTypeSymbol type,
        WorkerTypeHome home,
        string targetAssemblyMvid,
        IntroducedTypeArtifactMap artifactMap,
        out string memberName)
    {
        foreach (IMethodSymbol method in EnumerateMethods(type))
        {
            if (!HasAssemblyConstrainedOverride(method))
            {
                continue;
            }

            INamedTypeSymbol baseType = method.OverriddenMethod?.ContainingType;
            if (baseType != null
                && baseType.DeclaringSyntaxReferences.Length > 0
                && home.FindCompiledType(baseType) == null
                && artifactMap.FindActiveDeclarationFingerprint(
                    home.AssemblyName, targetAssemblyMvid, CecilTypeNames.ToMetadataName(baseType)) == null)
            {
                continue;
            }

            memberName = method.AssociatedSymbol?.Name ?? method.Name;
            return true;
        }

        memberName = string.Empty;
        return false;
    }

    private static bool HasAssemblyConstrainedOverride(IMethodSymbol method)
    {
        if (method == null || !method.IsOverride)
        {
            return false;
        }

        Accessibility accessibility = method.DeclaredAccessibility;
        return accessibility == Accessibility.Internal
            || accessibility == Accessibility.ProtectedOrInternal
            || accessibility == Accessibility.ProtectedAndInternal;
    }

    // Accessors travel through their associated member once, not again as standalone methods,
    // so a property or event produces one refusal with its source-level name.
    private static IEnumerable<IMethodSymbol> EnumerateMethods(INamedTypeSymbol type)
    {
        foreach (ISymbol member in type.GetMembers())
        {
            if (member is IMethodSymbol method && method.AssociatedSymbol == null)
            {
                yield return method;
            }
            else if (member is IPropertySymbol property)
            {
                yield return property.GetMethod;
                yield return property.SetMethod;
            }
            else if (member is IEventSymbol eventSymbol)
            {
                yield return eventSymbol.AddMethod;
                yield return eventSymbol.RemoveMethod;
            }
        }
    }
}
