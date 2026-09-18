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
using Microsoft.CodeAnalysis.Text;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

internal static class AccessorPropertyWriteRules
{
    internal static bool TryRegisterPropertyWrite(
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment,
        IPropertySymbol propertySymbol,
        AccessorPlan plan,
        AddedMemberAccessLookup addedMemberAccess,
        out WorkerReason rejectReason)
    {
        rejectReason = null;
        bool needsGetter = !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);

        // Why accessibility first: shape gates (indexer/static/ref-return) must not reject fully
        // public writes such as dict[key]=value or Time.timeScale=0f. The read-side path already
        // pre-filters with IsInaccessibleAccessor/IsInaccessibleFromExternalAssembly before shape
        // checks — keep that order here for symmetry.
        bool setterInaccessible = AccessibilityRules.IsInaccessibleAccessor(propertySymbol.SetMethod);
        bool getterInaccessible = needsGetter
            && AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod);
        if (!setterInaccessible && !getterInaccessible)
        {
            return false;
        }

        // Returned before the shape gates and the setter registration: the added-property
        // rewrite writes a static added property through its shim accessor, and a compiled
        // setter delegate for a property the compiled type lacks could never bind.
        if (propertySymbol.IsStatic
            && addedMemberAccess != null
            && addedMemberAccess.IsAddedProperty(propertySymbol))
        {
            return false;
        }

        WorkerReason shapeRejectReason = TryGetPropertyWriteShapeRejectReason(
            semanticModel,
            assignment,
            propertySymbol,
            needsGetter,
            setterInaccessible,
            getterInaccessible);
        if (shapeRejectReason != null)
        {
            rejectReason = shapeRejectReason;
            return false;
        }

        if (setterInaccessible)
        {
            if (propertySymbol.SetMethod == null)
            {
                rejectReason = WorkerReason.Of(HotReloadWorkerReasonCode.AccessorPropertyNoSetter);
                return false;
            }

            plan.GetOrAddPropertySetter(propertySymbol);
        }

        if (getterInaccessible)
        {
            if (propertySymbol.GetMethod == null)
            {
                rejectReason = WorkerReason.Of(HotReloadWorkerReasonCode.AccessorPropertyNoGetter);
                return false;
            }

            plan.GetOrAddPropertyGetter(propertySymbol);
        }

        return true;
    }

    internal static WorkerReason TryGetPropertyWriteShapeRejectReason(
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment,
        IPropertySymbol propertySymbol,
        bool needsGetter,
        bool setterInaccessible,
        bool getterInaccessible)
    {
        if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            return
                WorkerReason.Of(HotReloadWorkerReasonCode.AccessorCoalesceAssignmentNoShape);
        }

        if (needsGetter && !AccessorEligibility.IsSupportedCompoundAssignmentKind(assignment.Kind()))
        {
            return
                WorkerReason.Of(HotReloadWorkerReasonCode.AccessorCompoundAssignmentKindUnsupported);
        }

        // Compound assignment with a private getter and a public setter has no rewrite shape:
        // RewritePropertyAssignment only fires when the setter is inaccessible.
        if (getterInaccessible && !setterInaccessible)
        {
            return
                WorkerReason.Of(HotReloadWorkerReasonCode.AccessorCompoundInaccessibleGetterNoShape);
        }

        // Setter delegates are void — consuming the assignment expression value cannot compile.
        if (assignment.Parent is not ExpressionStatementSyntax)
        {
            return
                WorkerReason.Of(HotReloadWorkerReasonCode.AccessorAssignmentValueConsumed);
        }

        // Compound/get+set rewrite embeds the receiver twice; reject side-effecting receivers.
        if (needsGetter && !AccessorEligibility.IsSideEffectFreeAssignmentReceiver(semanticModel, assignment.Left))
        {
            return
                WorkerReason.Of(HotReloadWorkerReasonCode.AccessorReceiverDoubleEvaluation);
        }

        if (propertySymbol.IsIndexer)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AccessorIndexerNoShape);
        }

        if (propertySymbol.IsStatic)
        {
            return
                WorkerReason.Of(HotReloadWorkerReasonCode.AccessorStaticPropertyNoShape);
        }

        if (propertySymbol.ReturnsByRef || propertySymbol.ReturnsByRefReadonly)
        {
            return
                WorkerReason.Of(HotReloadWorkerReasonCode.AccessorRefReturningPropertyNoShape);
        }

        return null;
    }
}
