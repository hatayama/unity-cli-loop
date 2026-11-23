using System;
using Microsoft.CodeAnalysis;

/// <summary>
/// Tells the accessor plan which members reached from one edited type this reload adds (its own
/// private members, and extension methods added to a static class the edit declares), so a shape
/// the accessor rewrite cannot express does not refuse a member the added-member rewrite reaches
/// directly.
/// </summary>
internal sealed class AddedMemberAccessLookup
{
    private readonly INamedTypeSymbol _typeSymbol;
    private readonly INamedTypeSymbol _compiledType;
    private readonly AddedPropertyCatalog _addedPropertyCatalog;
    private readonly AddedEventLookup _addedEvents;

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

    /// <summary>
    /// Whether an extension method is one this reload adds to a static class the edit declares, so a
    /// call to it is rewritten to the added method directly, whatever its arguments, as a call to a
    /// method added to the edited type is.
    /// </summary>
    // Why the compiled counterpart of the method's own class: an extension method is declared
    // outside the edited type, and the catalog cannot answer for a class classified later in the
    // run. A class new to the Editor has no counterpart and is left to the introduced-type paths;
    // a generic method is left to the generic-method reason, which every call form meets.
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
        // Why NotFound and not "anything but Matched": a method whose return type changed is a
        // signature change, which keeps its own refusal; it is not added, and no shim method holds it.
        return compiledType != null
            && CompiledMemberMatcher.MatchCompiledOrdinaryMethod(compiledType, methodSymbol)
                == CompiledMethodMatch.NotFound;
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

    // Why any declaring type, unlike the property question: the event answer comes from the
    // compiled declaring type alone, which every type can ask regardless of classification order.
    public bool IsStoreBackedEvent(IEventSymbol eventSymbol)
    {
        return eventSymbol != null && _addedEvents.IsStoreBacked(eventSymbol);
    }
}
