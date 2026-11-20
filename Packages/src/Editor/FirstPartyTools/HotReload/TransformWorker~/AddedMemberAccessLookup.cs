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
    private readonly AddedEventLookup _addedEvents;
    private readonly bool _answersAddedExtensionsOnly;

    public AddedMemberAccessLookup(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol compiledType,
        AddedPropertyCatalog addedPropertyCatalog,
        AddedEventLookup addedEvents)
    {
        if (typeSymbol == null)
        {
            throw new ArgumentNullException(nameof(typeSymbol));
        }

        if (addedPropertyCatalog == null)
        {
            throw new ArgumentNullException(nameof(addedPropertyCatalog));
        }

        if (addedEvents == null)
        {
            throw new ArgumentNullException(nameof(addedEvents));
        }

        _typeSymbol = typeSymbol;
        _addedEvents = addedEvents;
        _compiledType = compiledType;
        _addedPropertyCatalog = addedPropertyCatalog;
    }

    private AddedMemberAccessLookup(AddedEventLookup addedEvents)
    {
        _addedEvents = addedEvents;
        _answersAddedExtensionsOnly = true;
    }

    /// <summary>
    /// A lookup for a property or indexer accessor body that answers only whether an extension
    /// method is added in this reload; every other question answers false.
    /// </summary>
    // Why accessor bodies get the extension answer and nothing else: they reach no added member
    // directly, so the other answers stay as without a lookup. A receiver call to an added
    // extension fails at accessor binding in any body, so that refusal must reach them too.
    internal static AddedMemberAccessLookup ForAccessorBody(AddedEventLookup addedEvents)
    {
        if (addedEvents == null)
        {
            throw new ArgumentNullException(nameof(addedEvents));
        }

        return new AddedMemberAccessLookup(addedEvents);
    }

    // Why the compiled match and not the added-method catalog: the catalog fills in declaration
    // order, so a caller declared above its added callee would not find the callee yet.
    public bool IsAddedMethod(IMethodSymbol methodSymbol)
    {
        if (_answersAddedExtensionsOnly
            || _compiledType == null
            || methodSymbol == null
            || methodSymbol.MethodKind != MethodKind.Ordinary
            || !SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType, _typeSymbol))
        {
            return false;
        }

        return CompiledMemberMatcher.MatchCompiledOrdinaryMethod(_compiledType, methodSymbol)
            != CompiledMethodMatch.Matched;
    }

    // Why any declaring type, unlike IsAddedMethod: an extension method lives in its own static
    // class, and whether that class's compiled counterpart holds it is answered from that class
    // alone. A class new to the Editor is left to the introduced-type paths. A generic one is
    // left to the generic-method reason, which a static-form call would meet as well.
    public bool IsAddedExtensionMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol == null
            || !methodSymbol.IsExtensionMethod
            || methodSymbol.IsGenericMethod
            || methodSymbol.MethodKind != MethodKind.Ordinary
            || methodSymbol.DeclaringSyntaxReferences.IsEmpty)
        {
            return false;
        }

        INamedTypeSymbol compiledType =
            _addedEvents.FindCompiledDeclaringTypeOrNull(methodSymbol.ContainingType.OriginalDefinition);
        return compiledType != null
            && CompiledMemberMatcher.MatchCompiledOrdinaryMethod(compiledType, methodSymbol)
                != CompiledMethodMatch.Matched;
    }

    // Why the catalog is enough here: a type's properties are classified before any of its
    // method bodies is decided. Another type's may not be yet, so only this type is asked.
    public bool IsAddedProperty(IPropertySymbol propertySymbol)
    {
        if (_answersAddedExtensionsOnly
            || propertySymbol == null
            || !SymbolEqualityComparer.Default.Equals(propertySymbol.ContainingType, _typeSymbol))
        {
            return false;
        }

        return _addedPropertyCatalog.FindBySymbolOrNull(propertySymbol) != null;
    }

    // Why any declaring type, unlike the property question: the event answer comes from the
    // compiled declaring type alone, which every type can ask regardless of classification order.
    public bool IsStoreBackedEvent(IEventSymbol eventSymbol)
    {
        return !_answersAddedExtensionsOnly && eventSymbol != null && _addedEvents.IsStoreBacked(eventSymbol);
    }
}
