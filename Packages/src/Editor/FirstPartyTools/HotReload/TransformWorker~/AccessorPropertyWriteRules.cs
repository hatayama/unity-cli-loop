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

internal static class AccessorPropertyWriteRules
{
    internal static bool TryRegisterPropertyWrite(
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment,
        IPropertySymbol propertySymbol,
        AccessorPlan plan,
        out string rejectReason)
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

        string shapeRejectReason = TryGetPropertyWriteShapeRejectReason(
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
                rejectReason = "inaccessible property has no setter to bind.";
                return false;
            }

            plan.GetOrAddPropertySetter(propertySymbol);
        }

        if (getterInaccessible)
        {
            if (propertySymbol.GetMethod == null)
            {
                rejectReason = "inaccessible property has no getter to bind.";
                return false;
            }

            plan.GetOrAddPropertyGetter(propertySymbol);
        }

        return true;
    }

    internal static string TryGetPropertyWriteShapeRejectReason(
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
                "null-coalescing assignment writes conditionally and has no accessor rewrite shape.";
        }

        if (needsGetter && !AccessorEligibility.IsSupportedCompoundAssignmentKind(assignment.Kind()))
        {
            return
                "unsupported compound assignment kind has no accessor rewrite shape.";
        }

        // Compound assignment with a private getter and a public setter has no rewrite shape:
        // RewritePropertyAssignment only fires when the setter is inaccessible.
        if (getterInaccessible && !setterInaccessible)
        {
            return
                "compound assignment reading an inaccessible getter with an accessible setter "
                + "has no accessor rewrite shape.";
        }

        // Setter delegates are void — consuming the assignment expression value cannot compile.
        if (assignment.Parent is not ExpressionStatementSyntax)
        {
            return
                "assignment value is consumed; the setter delegate returns void.";
        }

        // Compound/get+set rewrite embeds the receiver twice; reject side-effecting receivers.
        if (needsGetter && !AccessorEligibility.IsSideEffectFreeAssignmentReceiver(semanticModel, assignment.Left))
        {
            return
                "receiver with possible side effects would be evaluated twice.";
        }

        if (propertySymbol.IsIndexer)
        {
            return "inaccessible indexer access has no accessor rewrite shape.";
        }

        if (propertySymbol.IsStatic)
        {
            return
                "inaccessible static property access has no accessor rewrite shape.";
        }

        if (propertySymbol.ReturnsByRef || propertySymbol.ReturnsByRefReadonly)
        {
            return
                "inaccessible ref-returning properties have no accessor rewrite shape.";
        }

        return null;
    }
}
