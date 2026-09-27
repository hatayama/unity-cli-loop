using Microsoft.CodeAnalysis;

// Finds where a member added to one type uses a non-public member of a retained introduced type
// that this run keeps in its binding tree. Such a use binds only on the reloads that keep that
// declaration: every other reload binds the retained artifact through the public surface the
// default metadata import exposes, where the member is absent, so an addition that used it would
// be applied on one reload and dropped on the next unrelated one.
internal static class RetainedNonPublicMemberUseGuard
{
    /// <summary>
    /// The first member the body uses that is non-public on a retained type this run keeps, other
    /// than the type the added member belongs to, or null when the body uses none.
    /// </summary>
    internal static ISymbol FindUse(
        SemanticModel semanticModel,
        SyntaxNode bodyNode,
        INamedTypeSymbol hostType,
        WorkerSourceUnit sourceUnit)
    {
        if (sourceUnit.RunRetainedBodyEditTypes.Count == 0)
        {
            return null;
        }

        // Why nameof arguments are not skipped, unlike InaccessibleAccessScanner: that scanner asks
        // whether the emitted code needs an accessor, while this asks whether the name binds, and a
        // reload that binds the artifact cannot find the member inside nameof either.
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            ISymbol symbol = semanticModel.GetSymbolInfo(node).Symbol;
            if (IsNonPublicMemberOfAnotherRetainedType(symbol, hostType, semanticModel, sourceUnit))
            {
                return symbol;
            }
        }

        return null;
    }

    // Why the type the added member belongs to is excluded: its added members exist only while its
    // source differs from its artifact, and every such reload keeps its declaration, so they bind
    // the same way on each reload that has them.
    private static bool IsNonPublicMemberOfAnotherRetainedType(
        ISymbol symbol,
        INamedTypeSymbol hostType,
        SemanticModel semanticModel,
        WorkerSourceUnit sourceUnit)
    {
        if (symbol is not IFieldSymbol
            && symbol is not IPropertySymbol
            && symbol is not IMethodSymbol
            && symbol is not IEventSymbol)
        {
            return false;
        }

        INamedTypeSymbol containingType = symbol.ContainingType;
        if (containingType == null
            || SymbolEqualityComparer.Default.Equals(containingType.OriginalDefinition, hostType.OriginalDefinition)
            || !AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol))
        {
            return false;
        }

        return RetainedBodyEditHome.FindRunRetainedType(sourceUnit, semanticModel, containingType.OriginalDefinition) != null;
    }
}
