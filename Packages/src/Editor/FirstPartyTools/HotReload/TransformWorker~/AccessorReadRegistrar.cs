using Microsoft.CodeAnalysis;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// Registers a read of a property or a field on the accessor plan, or rejects the shape the plan
// cannot express. Split from the write and call registrations because a read has to decide which
// accessor of a property it needs, which none of the other registrations do.
internal static class AccessorReadRegistrar
{
    internal static bool TryRegisterPropertyOrFieldRead(
        ISymbol symbol,
        AccessorPlan plan,
        AddedMemberAccessLookup addedMemberAccess,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        if (symbol is IFieldSymbol fieldSymbol)
        {
            if (!AccessibilityRules.IsInaccessibleFromExternalAssembly(fieldSymbol))
            {
                return false;
            }

            if (fieldSymbol.IsConst)
            {
                return true;
            }

            plan.GetOrAddField(fieldSymbol);
            return true;
        }

        if (symbol is IPropertySymbol propertySymbol)
        {
            return TryRegisterInaccessiblePropertyRead(propertySymbol, plan, addedMemberAccess, out rejectReason);
        }

        if (symbol is IEventSymbol eventSymbol)
        {
            plan.GetOrAddEventBackingField(eventSymbol);
            return true;
        }

        if (symbol is IMethodSymbol methodSymbol
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(methodSymbol))
        {
            rejectReason =
                WorkerReason.Of(HotReloadWorkerReasonCode.AccessorMethodGroupNoShape, methodSymbol.Name);
            return false;
        }

        if (symbol != null
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol)
            && symbol is not INamespaceSymbol
            && symbol is not ITypeSymbol
            && symbol is not ILocalSymbol
            && symbol is not IParameterSymbol)
        {
            rejectReason = WorkerReason.Of(HotReloadWorkerReasonCode.AccessorMemberKindUnsupported);
            return false;
        }

        return false;
    }

    private static bool TryRegisterInaccessiblePropertyRead(
        IPropertySymbol propertySymbol,
        AccessorPlan plan,
        AddedMemberAccessLookup addedMemberAccess,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        if (!AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod))
        {
            return false;
        }

        // The added-property rewrite reads a static added property through its shim accessor,
        // a shape the compiled-member delegates do not have.
        if (propertySymbol.IsStatic
            && addedMemberAccess != null
            && addedMemberAccess.IsAddedProperty(propertySymbol))
        {
            return false;
        }

        return TryRegisterPropertyRead(propertySymbol, plan, out rejectReason);
    }

    private static bool TryRegisterPropertyRead(
        IPropertySymbol propertySymbol,
        AccessorPlan plan,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        if (propertySymbol.IsIndexer)
        {
            rejectReason = WorkerReason.Of(HotReloadWorkerReasonCode.AccessorIndexerNoShape);
            return false;
        }

        if (propertySymbol.IsStatic)
        {
            rejectReason =
                WorkerReason.Of(HotReloadWorkerReasonCode.AccessorStaticPropertyNoShape);
            return false;
        }

        if (propertySymbol.ReturnsByRef || propertySymbol.ReturnsByRefReadonly)
        {
            rejectReason =
                WorkerReason.Of(HotReloadWorkerReasonCode.AccessorRefReturningPropertyNoShape);
            return false;
        }

        plan.GetOrAddPropertyGetter(propertySymbol);
        return true;
    }
}
