using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
        SyntaxKind handlerAssignmentKind,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        if (symbol is IFieldSymbol fieldSymbol)
        {
            return TryRegisterFieldRead(fieldSymbol, plan, out rejectReason);
        }

        if (symbol is IPropertySymbol propertySymbol)
        {
            return TryRegisterInaccessiblePropertyRead(propertySymbol, plan, addedMemberAccess, out rejectReason);
        }

        if (symbol is IEventSymbol eventSymbol)
        {
            return TryRegisterEventRead(eventSymbol, plan, addedMemberAccess, out rejectReason);
        }

        if (symbol is IMethodSymbol methodSymbol
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(methodSymbol))
        {
            rejectReason = DescribeMethodGroupReject(methodSymbol, handlerAssignmentKind);
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

    // Why the reason follows the assignment: a lambda on the right of '-=' is a new delegate that
    // was never subscribed, so offering one there would compile and leave the handler attached,
    // and on the right of '+=' a lambda could no longer be removed by a compiled '-='.
    private static WorkerReason DescribeMethodGroupReject(IMethodSymbol methodSymbol, SyntaxKind handlerAssignmentKind)
    {
        // Why no lambda example: the method-group reasons suggest a lambda that calls the method,
        // but a call to an extension method whose class is not public is refused as well, so that
        // advice would lead to another skip.
        if (AccessibilityRules.UnreducedMethod(methodSymbol).IsExtensionMethod)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AccessorExtensionMethodGroupNoShape);
        }

        // Why the struct reason: a lambda calling an instance method of a struct is refused too.
        if (AccessibilityRules.NeedsStructInstanceReceiver(methodSymbol))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AccessorStructMemberNoShape);
        }

        if (handlerAssignmentKind == SyntaxKind.SubtractAssignmentExpression)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AccessorMethodGroupUnsubscribeNoShape, methodSymbol.Name);
        }

        HotReloadWorkerReasonCode code = handlerAssignmentKind == SyntaxKind.AddAssignmentExpression
            ? HotReloadWorkerReasonCode.AccessorMethodGroupSubscribeNoShape
            : HotReloadWorkerReasonCode.AccessorMethodGroupNoShape;
        return WorkerReason.Of(code, methodSymbol.Name, MethodGroupLambdaExample.BuildSuffix(methodSymbol));
    }

    private static bool TryRegisterFieldRead(IFieldSymbol fieldSymbol, AccessorPlan plan, out WorkerReason rejectReason)
    {
        rejectReason = null;
        if (!AccessibilityRules.IsInaccessibleFromExternalAssembly(fieldSymbol))
        {
            return false;
        }

        if (fieldSymbol.IsConst)
        {
            return true;
        }

        if (AccessibilityRules.NeedsStructInstanceReceiver(fieldSymbol))
        {
            rejectReason = WorkerReason.Of(HotReloadWorkerReasonCode.AccessorStructMemberNoShape);
            return false;
        }

        plan.GetOrAddField(fieldSymbol);
        return true;
    }

    private static bool TryRegisterEventRead(
        IEventSymbol eventSymbol,
        AccessorPlan plan,
        AddedMemberAccessLookup addedMemberAccess,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        // An added event the store keeps has no backing field to reach; its reads become
        // store calls any assembly can compile.
        if (addedMemberAccess != null && addedMemberAccess.IsStoreBackedEvent(eventSymbol))
        {
            return false;
        }

        // Why: only code inside the struct's text (a nested class) can read its event as a
        // field, and the backing field of a struct instance has no field ref to bind.
        if (AccessibilityRules.NeedsStructInstanceReceiver(eventSymbol))
        {
            rejectReason = WorkerReason.Of(HotReloadWorkerReasonCode.AccessorStructMemberNoShape);
            return false;
        }

        plan.GetOrAddEventBackingField(eventSymbol);
        return true;
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

        if (propertySymbol.ReturnsByRef || propertySymbol.ReturnsByRefReadonly)
        {
            rejectReason =
                WorkerReason.Of(HotReloadWorkerReasonCode.AccessorRefReturningPropertyNoShape);
            return false;
        }

        if (AccessibilityRules.NeedsStructInstanceReceiver(propertySymbol))
        {
            rejectReason = WorkerReason.Of(HotReloadWorkerReasonCode.AccessorStructMemberNoShape);
            return false;
        }

        plan.GetOrAddPropertyGetter(propertySymbol);
        return true;
    }
}
