using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Decides whether an event this edit adds keeps its delegate in the added-field store, so its
/// uses are rewritten to the store instead of being refused.
/// </summary>
/// <remarks>
/// Why the symbol and the compiled declaring type only, never a catalog: the catalogs fill one
/// type at a time, and a body of another type is decided before the declaring type is classified
/// when its file comes first, so a catalog answer would change with the order of the files.
/// </remarks>
internal static class AddedEventStorePolicy
{
    internal static bool IsStoreBacked(IEventSymbol eventSymbol, INamedTypeSymbol compiledDeclaringType)
    {
        // An event read from metadata belongs to an assembly the edit does not compile, and a
        // declaring type the Editor does not hold yet is decided by the introduced-type paths.
        if (eventSymbol.DeclaringSyntaxReferences.IsEmpty || compiledDeclaringType == null)
        {
            return false;
        }

        if (!IsFieldLike(eventSymbol) || !HasStoreHost(eventSymbol.ContainingType))
        {
            return false;
        }

        if (!AccessibilityRules.IsExternallyVisibleType(eventSymbol.Type)
            || !AccessibilityRules.IsExternallyVisibleType(eventSymbol.ContainingType))
        {
            return false;
        }

        // Why any member of the name and not only another event: a compiled field or method of
        // that name is what compiled callers still bind to, so the store would hide the change.
        return compiledDeclaringType.GetMembers(eventSymbol.Name).IsEmpty;
    }

    /// <summary>
    /// Stops the run when a body about to be emitted uses a store-backed event that has no store
    /// binding: the body was let through on the promise of the store rewrite, and without the
    /// binding the raw event access would reach the shim and bind to a member that does not exist.
    /// </summary>
    internal static void RequireBindings(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedEventLookup addedEvents,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (node is not SimpleNameSyntax
                || semanticModel.GetSymbolInfo(node).Symbol is not IEventSymbol eventSymbol
                || !addedEvents.IsStoreBacked(eventSymbol))
            {
                continue;
            }

            if (addedFieldCatalog.FindOrNull(AddedFieldBodyScan.FormatAddedStoreKeyOrNull(eventSymbol)) == null)
            {
                throw new System.InvalidOperationException(
                    "Added event '" + eventSymbol.ToDisplayString()
                    + "' is store-backed but its declaring type registered no store binding.");
            }
        }
    }

    private static bool IsFieldLike(IEventSymbol eventSymbol)
    {
        if (eventSymbol.IsAbstract || eventSymbol.IsExtern)
        {
            return false;
        }

        foreach (SyntaxReference reference in eventSymbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is EventDeclarationSyntax)
            {
                return false;
            }
        }

        return true;
    }

    // Why a struct is let through: its event gets a binding the classifier marks unavailable, so
    // a use is skipped with the reason an added struct field gets rather than an event reason.
    private static bool HasStoreHost(INamedTypeSymbol containingType)
    {
        return containingType.TypeKind == TypeKind.Class || containingType.TypeKind == TypeKind.Struct;
    }
}
