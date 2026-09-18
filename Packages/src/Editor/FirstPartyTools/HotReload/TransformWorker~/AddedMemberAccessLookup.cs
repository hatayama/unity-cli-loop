using System;
using Microsoft.CodeAnalysis;

/// <summary>
/// Tells the accessor plan which private members of one edited type this reload adds, so a
/// shape the accessor rewrite cannot express does not refuse a member the added-member
/// rewrite reaches directly.
/// </summary>
internal sealed class AddedMemberAccessLookup
{
    private readonly INamedTypeSymbol _typeSymbol;
    private readonly INamedTypeSymbol _compiledType;
    private readonly AddedPropertyCatalog _addedPropertyCatalog;

    public AddedMemberAccessLookup(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol compiledType,
        AddedPropertyCatalog addedPropertyCatalog)
    {
        if (typeSymbol == null)
        {
            throw new ArgumentNullException(nameof(typeSymbol));
        }

        if (addedPropertyCatalog == null)
        {
            throw new ArgumentNullException(nameof(addedPropertyCatalog));
        }

        _typeSymbol = typeSymbol;
        _compiledType = compiledType;
        _addedPropertyCatalog = addedPropertyCatalog;
    }

    // Why the compiled match and not the added-method catalog: the catalog fills in declaration
    // order, so a caller declared above its added callee would not find the callee yet.
    public bool IsAddedMethod(IMethodSymbol methodSymbol)
    {
        if (_compiledType == null
            || methodSymbol == null
            || methodSymbol.MethodKind != MethodKind.Ordinary
            || !SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType, _typeSymbol))
        {
            return false;
        }

        return CompiledMemberMatcher.MatchCompiledOrdinaryMethod(_compiledType, methodSymbol)
            != CompiledMethodMatch.Matched;
    }

    // Why the catalog is enough here: a type's properties are classified before any of its
    // method bodies is decided. Another type's may not be yet, so only this type is asked.
    public bool IsAddedProperty(IPropertySymbol propertySymbol)
    {
        if (propertySymbol == null
            || !SymbolEqualityComparer.Default.Equals(propertySymbol.ContainingType, _typeSymbol))
        {
            return false;
        }

        return _addedPropertyCatalog.FindBySymbolOrNull(propertySymbol) != null;
    }
}
