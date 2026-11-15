using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// What: decides which property set accessors the setter emitter reports, so the skip collector
// and the emitter split every bodied setter between them without overlap.
internal static class PropertySetterClassifier
{
    // What: whether hot reload patches this property's set accessor itself (as opposed to
    // reporting it Skipped). compiledType is null when the compiled assembly has no such type.
    // Why a matching compiled setter is required: an entry for a setter the compiled property does
    // not have (or takes another type) fails the Editor's signature lookup and turns the run into
    // Failed, while Skipped keeps pointing the reader at 'uloop compile'.
    internal static bool IsPatchableCompiledSetter(
        PropertyDeclarationSyntax propertyDeclaration,
        IPropertySymbol propertySymbol,
        INamedTypeSymbol compiledType,
        bool servedByRetainedArtifact)
    {
        if (propertySymbol == null || propertySymbol.SetMethod == null)
        {
            return false;
        }

        if (FindBodiedSetAccessorOrNull(propertyDeclaration) == null)
        {
            return false;
        }

        // Why not a retained artifact's type: the fingerprint comparison that serves those types
        // reports their accessor edits, and the baseline this emitter reads is not theirs.
        if (compiledType == null || servedByRetainedArtifact)
        {
            return false;
        }

        IPropertySymbol compiledProperty = compiledType.GetMembers(propertySymbol.Name)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(candidate => !candidate.IsIndexer);
        return compiledProperty != null
            && HasMatchingOrdinarySetter(compiledProperty, propertySymbol);
    }

    // What: the set accessor with a block or expression body, or null when the property has no
    // set accessor, only an auto-implemented one, or only init.
    internal static AccessorDeclarationSyntax FindBodiedSetAccessorOrNull(PropertyDeclarationSyntax propertyDeclaration)
    {
        if (propertyDeclaration.AccessorList == null)
        {
            return null;
        }

        foreach (AccessorDeclarationSyntax accessor in propertyDeclaration.AccessorList.Accessors)
        {
            if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration)
                && (accessor.Body != null || accessor.ExpressionBody != null))
            {
                return accessor;
            }
        }

        return null;
    }

    // What: whether the property has a set or init accessor with a block or expression body.
    internal static bool HasBodiedSetOrInitAccessor(PropertyDeclarationSyntax propertyDeclaration)
    {
        if (propertyDeclaration.AccessorList == null)
        {
            return false;
        }

        foreach (AccessorDeclarationSyntax accessor in propertyDeclaration.AccessorList.Accessors)
        {
            bool isSetOrInit = accessor.IsKind(SyntaxKind.SetAccessorDeclaration)
                || accessor.IsKind(SyntaxKind.InitAccessorDeclaration);
            if (isSetOrInit && (accessor.Body != null || accessor.ExpressionBody != null))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMatchingOrdinarySetter(IPropertySymbol compiledProperty, IPropertySymbol propertySymbol)
    {
        // Why each check: a setter added since the last compile, one that was init at the last
        // compile, and a property re-typed or made (non-)static since then all lack a compiled
        // set_X with this signature.
        return compiledProperty.SetMethod != null
            && !compiledProperty.SetMethod.IsInitOnly
            && compiledProperty.IsStatic == propertySymbol.IsStatic
            && CecilTypeNames.ToCecilFullName(compiledProperty.Type) == CecilTypeNames.ToCecilFullName(propertySymbol.Type);
    }
}
