using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

internal static class AccessorAccessRegistrar
{
    /// <summary>
    /// Returns false with null rejectReason when the node is not an inaccessible access site.
    /// Returns false with a reason when the site is inaccessible but not rewriteable.
    /// Returns true when the site was registered (or was already present).
    /// </summary>
    internal static bool TryRegisterInaccessibleAccess(
        SemanticModel semanticModel,
        SyntaxNode node,
        AccessorPlan plan,
        AddedMemberAccessLookup addedMemberAccess,
        out WorkerReason rejectReason)
    {
        // Why first: a call the compiler generates has no syntax this registrar could rewrite, and
        // the node that causes it (a deconstruction assignment, a collection initializer) would
        // otherwise be taken by the branches below.
        rejectReason = ImplicitCallTargets.FindInaccessibleCallReasonOrNull(semanticModel, node);
        if (rejectReason != null)
        {
            return false;
        }

        if (node is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax)
        {
            return TryRegisterObjectCreation(semanticModel, node, out rejectReason);
        }

        if (node is AssignmentExpressionSyntax assignment
            && assignment.Parent is InitializerExpressionSyntax)
        {
            return TryRegisterInitializerAssignment(semanticModel, assignment, out rejectReason);
        }

        if (node is InvocationExpressionSyntax invocation)
        {
            return TryRegisterInvocation(semanticModel, invocation, plan, addedMemberAccess, out rejectReason);
        }

        if (node is ElementAccessExpressionSyntax elementAccess)
        {
            return TryRegisterElementAccess(semanticModel, elementAccess, out rejectReason);
        }

        if (node is MemberBindingExpressionSyntax memberBinding)
        {
            return TryRegisterMemberBinding(semanticModel, memberBinding, out rejectReason);
        }

        if (node is AssignmentExpressionSyntax propertyOrFieldAssignment)
        {
            return TryRegisterAssignment(
                semanticModel,
                propertyOrFieldAssignment,
                plan,
                addedMemberAccess,
                out rejectReason);
        }

        if (node is MemberAccessExpressionSyntax memberAccess)
        {
            return TryRegisterMemberAccess(semanticModel, memberAccess, plan, addedMemberAccess, out rejectReason);
        }

        if (node is IdentifierNameSyntax or GenericNameSyntax)
        {
            return TryRegisterSimpleName(
                semanticModel,
                (SimpleNameSyntax)node,
                plan,
                addedMemberAccess,
                out rejectReason);
        }

        return false;
    }

    internal static bool TryRegisterObjectCreation(
        SemanticModel semanticModel,
        SyntaxNode node,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        ISymbol ctorSymbol = AccessibilityRules.BoundSymbolOrNull(semanticModel, node);
        if (ctorSymbol != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(ctorSymbol))
        {
            rejectReason =
                WorkerReason.Of(HotReloadWorkerReasonCode.AccessorConstructorCallNoShape);
            return false;
        }

        return false;
    }

    internal static bool TryRegisterInitializerAssignment(
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        // Initializer assignments are always writes (including ImplicitElementAccess indexers).
        ISymbol initializerSymbol = AccessibilityRules.BoundSymbolOrNull(semanticModel, assignment.Left);
        bool inaccessibleWrite = initializerSymbol is IPropertySymbol initializerProperty
            ? AccessibilityRules.IsInaccessibleAccessor(AssignmentTargetRules.AccessorCalledByWrite(initializerProperty))
            : initializerSymbol != null
                && AccessibilityRules.IsInaccessibleFromExternalAssembly(initializerSymbol);
        if (inaccessibleWrite)
        {
            rejectReason =
                WorkerReason.Of(HotReloadWorkerReasonCode.AccessorInitializerAssignmentNoShape);
            return false;
        }

        return false;
    }

    internal static bool TryRegisterElementAccess(
        SemanticModel semanticModel,
        ElementAccessExpressionSyntax elementAccess,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        // Assignment-left ElementAccess is owned by the assignment branch (write context).
        if (AssignmentTargetRules.AssignmentTargetedBy(elementAccess) != null)
        {
            return false;
        }

        ISymbol symbol = AccessibilityRules.BoundSymbolOrNull(semanticModel, elementAccess);
        if (symbol is IPropertySymbol indexer && indexer.IsIndexer)
        {
            if (AssignmentTargetRules.IsDeconstructedThroughSetter(indexer, elementAccess))
            {
                return AccessorPropertyWriteRules.TryRegisterDeconstructedPropertyWrite(indexer, out rejectReason);
            }

            // Any other ElementAccess calls only the getter: a read, or a deconstruction into a
            // ref-returning indexer, which writes through the reference the getter returns.
            if (AccessibilityRules.IsInaccessibleAccessor(indexer.GetMethod))
            {
                rejectReason =
                    WorkerReason.Of(HotReloadWorkerReasonCode.AccessorIndexerNoShape);
                return false;
            }
        }
        else if (symbol != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol))
        {
            rejectReason = WorkerReason.Of(HotReloadWorkerReasonCode.AccessorIndexerNoShape);
            return false;
        }

        return false;
    }

    internal static bool TryRegisterMemberBinding(
        SemanticModel semanticModel,
        MemberBindingExpressionSyntax memberBinding,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        ISymbol bound = semanticModel.GetSymbolInfo(memberBinding.Name).Symbol;
        if (bound != null
            && bound is not INamespaceSymbol
            && bound is not ITypeSymbol
            && IsInaccessibleBindingTarget(bound))
        {
            rejectReason =
                WorkerReason.Of(HotReloadWorkerReasonCode.AccessorConditionalAccessNoShape);
            return false;
        }

        return false;
    }

    internal static bool TryRegisterMemberAccess(
        SemanticModel semanticModel,
        MemberAccessExpressionSyntax memberAccess,
        AccessorPlan plan,
        AddedMemberAccessLookup addedMemberAccess,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        // Method-group invocation targets are owned by the invocation branch; delegate-typed
        // field invokes (`this._cb()` / `other._cb()`) register as field reads here.
        if (memberAccess.Parent is InvocationExpressionSyntax parentInvocation
            && parentInvocation.Expression == memberAccess)
        {
            // Why the bound symbol: a call into a static class seen twice binds to no symbol, and
            // the method would otherwise be taken for a method group here.
            ISymbol invocationTarget = AccessibilityRules.BoundSymbolOrNull(semanticModel, memberAccess)
                ?? semanticModel.GetSymbolInfo(memberAccess.Name).Symbol;
            if (invocationTarget is IMethodSymbol)
            {
                return false;
            }
        }

        if (AssignmentTargetRules.AssignmentTargetedBy(memberAccess) != null)
        {
            return false;
        }

        return TryRegisterUseOutsideAssignment(
            memberAccess,
            AccessibilityRules.BoundSymbolOrNull(semanticModel, memberAccess)
            ?? semanticModel.GetSymbolInfo(memberAccess.Name).Symbol,
            plan,
            addedMemberAccess,
            out rejectReason);
    }

    internal static bool TryRegisterSimpleName(
        SemanticModel semanticModel,
        SimpleNameSyntax name,
        AccessorPlan plan,
        AddedMemberAccessLookup addedMemberAccess,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        if (AccessorEligibility.IsNameHandledByParent(name))
        {
            return false;
        }

        if (AssignmentTargetRules.AssignmentTargetedBy(name) != null)
        {
            return false;
        }

        // Method-group invocation targets are owned by the invocation branch; delegate-typed
        // field invokes (`_cb()`) register as field reads here.
        if (name.Parent is InvocationExpressionSyntax parentInvocation
            && parentInvocation.Expression == name)
        {
            ISymbol invocationTarget = semanticModel.GetSymbolInfo(name).Symbol;
            if (invocationTarget is IMethodSymbol)
            {
                return false;
            }
        }

        return TryRegisterUseOutsideAssignment(
            name,
            semanticModel.GetSymbolInfo(name).Symbol,
            plan,
            addedMemberAccess,
            out rejectReason);
    }

    // A property a deconstruction sets through its setter is a write; every other use outside an
    // assignment left side calls the getter as a read does, including a deconstruction into a
    // ref-returning property, which writes through the reference the getter returns. Why a field
    // element stays with the reads: its ref accessor is assignable, so the deconstruction writes
    // through it.
    private static bool TryRegisterUseOutsideAssignment(
        ExpressionSyntax site,
        ISymbol symbol,
        AccessorPlan plan,
        AddedMemberAccessLookup addedMemberAccess,
        out WorkerReason rejectReason)
    {
        if (symbol is IPropertySymbol propertySymbol
            && AssignmentTargetRules.IsDeconstructedThroughSetter(propertySymbol, site))
        {
            return AccessorPropertyWriteRules.TryRegisterDeconstructedPropertyWrite(propertySymbol, out rejectReason);
        }

        return AccessorReadRegistrar.TryRegisterPropertyOrFieldRead(
            symbol,
            plan,
            addedMemberAccess,
            EventAccessorRules.FindHandlerAssignmentKind(site),
            out rejectReason);
    }

    internal static bool IsInaccessibleBindingTarget(ISymbol bound)
    {
        // Member binding only appears in read/invoke contexts (x?.P = v is not valid C#).
        if (bound is IPropertySymbol propertySymbol)
        {
            return AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod);
        }

        return AccessibilityRules.IsInaccessibleFromExternalAssembly(bound);
    }

    internal static bool TryRegisterAssignment(
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment,
        AccessorPlan plan,
        AddedMemberAccessLookup addedMemberAccess,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        ISymbol leftSymbol = AccessibilityRules.BoundSymbolOrNull(semanticModel, assignment.Left);
        if (leftSymbol is IFieldSymbol fieldSymbol)
        {
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

        if (leftSymbol is IPropertySymbol propertySymbol)
        {
            return AccessorPropertyWriteRules.TryRegisterPropertyWrite(
                semanticModel,
                assignment,
                propertySymbol,
                plan,
                addedMemberAccess,
                out rejectReason);
        }

        if (leftSymbol is IEventSymbol eventSymbol)
        {
            return TryRegisterEventAssignment(assignment, eventSymbol, plan, addedMemberAccess, out rejectReason);
        }

        return false;
    }

    private static bool TryRegisterEventAssignment(
        AssignmentExpressionSyntax assignment,
        IEventSymbol eventSymbol,
        AccessorPlan plan,
        AddedMemberAccessLookup addedMemberAccess,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        // An added event the store keeps is written through the store, never a backing field.
        if (addedMemberAccess != null && addedMemberAccess.IsStoreBackedEvent(eventSymbol))
        {
            return false;
        }

        if (EventAccessorRules.IsSubscriptionAssignment(assignment))
        {
            // += / -= stay on the publicized add/remove accessors, which keep the
            // compiler's Interlocked.CompareExchange loop. Nothing to register.
            return false;
        }

        if (!EventAccessorRules.IsBackingFieldWrite(assignment))
        {
            rejectReason = WorkerReason.Of(HotReloadWorkerReasonCode.AccessorEventCompoundAssignmentNoShape);
            return false;
        }

        // Why: the backing field of a struct instance has no field ref to bind.
        if (AccessibilityRules.NeedsStructInstanceReceiver(eventSymbol))
        {
            rejectReason = WorkerReason.Of(HotReloadWorkerReasonCode.AccessorStructMemberNoShape);
            return false;
        }

        plan.GetOrAddEventBackingField(eventSymbol);
        return true;
    }

    internal static bool TryRegisterInvocation(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        AccessorPlan plan,
        AddedMemberAccessLookup addedMemberAccess,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        if (NameofRules.IsNameofInvocation(invocation))
        {
            return false;
        }

        ISymbol symbol = AccessibilityRules.BoundSymbolOrNull(semanticModel, invocation);
        if (symbol is not IMethodSymbol invokedMethod)
        {
            return false;
        }

        // Why unreduce first: a reduced extension call is not MethodKind.Ordinary, and returning
        // here left it unregistered and unrefused, so the shim called the method and failed.
        IMethodSymbol methodSymbol = AccessibilityRules.UnreducedMethod(invokedMethod);
        if (methodSymbol.MethodKind != MethodKind.Ordinary)
        {
            return false;
        }

        if (!AccessibilityRules.IsInaccessibleFromExternalAssembly(methodSymbol))
        {
            return false;
        }

        // Why refuse here: the shim rewrite leaves a call on a conditional-access spine as written,
        // anywhere down its receiver chain, so registering it would transplant a direct call the
        // shim cannot make.
        if (AddedCallSiteGuard.IsConditionalAccessReceiverSpine(invocation))
        {
            rejectReason = WorkerReason.Of(HotReloadWorkerReasonCode.AccessorConditionalAccessNoShape);
            return false;
        }

        // Why only the receiver form: its accessor finds no compiled method to bind, because the
        // compiled class does not hold a method this reload adds, and the shim fails at binding.
        // A call written in the static form applies (measured in a lambda and an async body).
        if (invokedMethod.MethodKind == MethodKind.ReducedExtension
            && addedMemberAccess != null
            && addedMemberAccess.IsAddedExtensionMethod(methodSymbol))
        {
            rejectReason = WorkerReason.Of(
                HotReloadWorkerReasonCode.AccessorAddedExtensionReducedCallNoShape,
                methodSymbol.Name,
                methodSymbol.ContainingType.Name);
            return false;
        }

        rejectReason = FindMethodShapeRejectReason(methodSymbol);
        if (rejectReason != null)
        {
            return false;
        }

        // The added-method rewrite calls the shim with the argument list as written, so no delegate
        // has to carry the arguments and the compiled type has no such method to bind one to.
        if (addedMemberAccess != null
            && addedMemberAccess.IsAddedMethod(methodSymbol))
        {
            return false;
        }

        rejectReason = FindDelegateCallRejectReason(methodSymbol);
        if (rejectReason != null)
        {
            return false;
        }

        rejectReason = FindArgumentRejectReason(semanticModel, invocation, invokedMethod, methodSymbol);
        if (rejectReason != null)
        {
            return false;
        }

        plan.GetOrAddMethod(methodSymbol);
        return true;
    }

    // The accessor delegate takes exactly the method's parameters by value, in order.
    private static WorkerReason FindArgumentRejectReason(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        IMethodSymbol invokedMethod,
        IMethodSymbol methodSymbol)
    {
        foreach (IParameterSymbol parameter in methodSymbol.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.AccessorRefOutInParameterNotRewritten);
            }
        }

        foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon != null)
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.AccessorNamedArgumentNotRewritten);
            }
        }

        // A reduced extension call passes its receiver as the first parameter, outside the list.
        int receiverArgumentCount = invokedMethod.MethodKind == MethodKind.ReducedExtension ? 1 : 0;
        if (invocation.ArgumentList.Arguments.Count + receiverArgumentCount != methodSymbol.Parameters.Length
            || HasExpandedParamsArgument(semanticModel, invocation))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AccessorOptionalOrParamsArgumentNotRewritten);
        }

        return null;
    }

    // Why the bound form and not the count: one element passed to a params parameter matches the
    // parameter count, yet the accessor delegate takes the array, so the element must be refused.
    // Why the enforcing binding when the first finds no call: the accessor target of a call left
    // unbound by an internal BCL copy came from that binding, so only it shows how the arguments map.
    private static bool HasExpandedParamsArgument(SemanticModel semanticModel, InvocationExpressionSyntax invocation)
    {
        IInvocationOperation operation = semanticModel.GetOperation(invocation) as IInvocationOperation
            ?? AccessibilityRules.EnforcingModel(semanticModel).GetOperation(invocation) as IInvocationOperation;
        if (operation == null)
        {
            return false;
        }

        foreach (IArgumentOperation argument in operation.Arguments)
        {
            if (argument.ArgumentKind == ArgumentKind.ParamArray)
            {
                return true;
            }
        }

        return false;
    }

    private static WorkerReason FindMethodShapeRejectReason(IMethodSymbol methodSymbol)
    {
        if (AccessibilityRules.NeedsStructInstanceReceiver(methodSymbol))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AccessorStructMemberNoShape);
        }

        if (methodSymbol.IsGenericMethod)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AccessorGenericMethodNotRewritten);
        }

        if (methodSymbol.ReturnsByRef || methodSymbol.ReturnsByRefReadonly)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AccessorRefReturningMethodNoShape);
        }

        // Why refuse: the accessor call is an ordinary delegate call, so it would run the method
        // and evaluate its arguments where the compiler omits the call. Why before the added-method
        // exemption: the added-method rewrite also runs the call (measured), so an added
        // [Conditional] method is refused too.
        if (BaseCallRules.IsConditional(methodSymbol))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AccessorConditionalMethodNotRewritten);
        }

        return null;
    }

    // What: refuses a method whose call an accessor delegate cannot carry. Only asked for a
    // method the compiled assembly holds; a method added in the same reload is called directly.
    private static WorkerReason FindDelegateCallRejectReason(IMethodSymbol methodSymbol)
    {

        // Why refuse: the accessor is a Func or Action, and a ref struct cannot be a generic
        // type argument, so the shim would not compile. Parameters count too: a call that passes a
        // Span or ReadOnlySpan binds, and its delegate would not compile either.
        if (HasRefLikeSignature(methodSymbol))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AccessorRefLikeSignatureNoShape);
        }

        return null;
    }

    private static bool HasRefLikeSignature(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.ReturnType.IsRefLikeType)
        {
            return true;
        }

        foreach (IParameterSymbol parameter in methodSymbol.Parameters)
        {
            if (parameter.Type.IsRefLikeType)
            {
                return true;
            }
        }

        return false;
    }
}
