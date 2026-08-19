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
        out string rejectReason)
    {
        rejectReason = null;

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
            return TryRegisterInvocation(semanticModel, invocation, plan, out rejectReason);
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
                out rejectReason);
        }

        if (node is MemberAccessExpressionSyntax memberAccess)
        {
            return TryRegisterMemberAccess(semanticModel, memberAccess, plan, out rejectReason);
        }

        if (node is IdentifierNameSyntax or GenericNameSyntax)
        {
            return TryRegisterSimpleName(semanticModel, (SimpleNameSyntax)node, plan, out rejectReason);
        }

        return false;
    }

    internal static bool TryRegisterObjectCreation(
        SemanticModel semanticModel,
        SyntaxNode node,
        out string rejectReason)
    {
        rejectReason = null;
        ISymbol ctorSymbol = semanticModel.GetSymbolInfo(node).Symbol;
        if (ctorSymbol != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(ctorSymbol))
        {
            rejectReason =
                "inaccessible constructor call has no accessor rewrite shape.";
            return false;
        }

        return false;
    }

    internal static bool TryRegisterInitializerAssignment(
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment,
        out string rejectReason)
    {
        rejectReason = null;
        // Initializer assignments are always writes (including ImplicitElementAccess indexers).
        ISymbol initializerSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
        bool inaccessibleWrite = initializerSymbol is IPropertySymbol initializerProperty
            ? AccessibilityRules.IsInaccessibleAccessor(initializerProperty.SetMethod)
            : initializerSymbol != null
                && AccessibilityRules.IsInaccessibleFromExternalAssembly(initializerSymbol);
        if (inaccessibleWrite)
        {
            rejectReason =
                "inaccessible member assignment in an object/collection initializer has no "
                + "accessor rewrite shape.";
            return false;
        }

        return false;
    }

    internal static bool TryRegisterElementAccess(
        SemanticModel semanticModel,
        ElementAccessExpressionSyntax elementAccess,
        out string rejectReason)
    {
        rejectReason = null;
        // Assignment-left ElementAccess is owned by the assignment branch (write context).
        if (elementAccess.Parent is AssignmentExpressionSyntax parentElementAssignment
            && parentElementAssignment.Left == elementAccess)
        {
            return false;
        }

        ISymbol symbol = semanticModel.GetSymbolInfo(elementAccess).Symbol;
        if (symbol is IPropertySymbol indexer && indexer.IsIndexer)
        {
            // Standalone ElementAccess is a read — only the getter matters.
            if (AccessibilityRules.IsInaccessibleAccessor(indexer.GetMethod))
            {
                rejectReason =
                    "inaccessible indexer access has no accessor rewrite shape.";
                return false;
            }
        }
        else if (symbol != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol))
        {
            rejectReason = "inaccessible indexer access has no accessor rewrite shape.";
            return false;
        }

        return false;
    }

    internal static bool TryRegisterMemberBinding(
        SemanticModel semanticModel,
        MemberBindingExpressionSyntax memberBinding,
        out string rejectReason)
    {
        rejectReason = null;
        ISymbol bound = semanticModel.GetSymbolInfo(memberBinding.Name).Symbol;
        if (bound != null
            && bound is not INamespaceSymbol
            && bound is not ITypeSymbol
            && IsInaccessibleBindingTarget(bound))
        {
            rejectReason =
                "inaccessible member access via conditional access has no rewrite shape.";
            return false;
        }

        return false;
    }

    internal static bool TryRegisterMemberAccess(
        SemanticModel semanticModel,
        MemberAccessExpressionSyntax memberAccess,
        AccessorPlan plan,
        out string rejectReason)
    {
        rejectReason = null;
        // Method-group invocation targets are owned by the invocation branch; delegate-typed
        // field invokes (`this._cb()` / `other._cb()`) register as field reads here.
        if (memberAccess.Parent is InvocationExpressionSyntax parentInvocation
            && parentInvocation.Expression == memberAccess)
        {
            ISymbol invocationTarget = semanticModel.GetSymbolInfo(memberAccess).Symbol
                ?? semanticModel.GetSymbolInfo(memberAccess.Name).Symbol;
            if (invocationTarget is IMethodSymbol)
            {
                return false;
            }
        }

        if (memberAccess.Parent is AssignmentExpressionSyntax parentAssignment
            && parentAssignment.Left == memberAccess)
        {
            return false;
        }

        return TryRegisterPropertyOrFieldRead(
            semanticModel.GetSymbolInfo(memberAccess).Symbol
            ?? semanticModel.GetSymbolInfo(memberAccess.Name).Symbol,
            plan,
            out rejectReason);
    }

    internal static bool TryRegisterSimpleName(
        SemanticModel semanticModel,
        SimpleNameSyntax name,
        AccessorPlan plan,
        out string rejectReason)
    {
        rejectReason = null;
        if (AccessorEligibility.IsNameHandledByParent(name))
        {
            return false;
        }

        if (name.Parent is AssignmentExpressionSyntax parentAssignment
            && parentAssignment.Left == name)
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

        return TryRegisterPropertyOrFieldRead(
            semanticModel.GetSymbolInfo(name).Symbol,
            plan,
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
        out string rejectReason)
    {
        rejectReason = null;
        ISymbol leftSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
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
                out rejectReason);
        }

        if (leftSymbol is IEventSymbol)
        {
            rejectReason = "inaccessible event add/remove is out of scope for accessor rewrite.";
            return false;
        }

        return false;
    }

    internal static bool TryRegisterInvocation(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        AccessorPlan plan,
        out string rejectReason)
    {
        rejectReason = null;
        if (NameofRules.IsNameofInvocation(invocation))
        {
            return false;
        }

        ISymbol symbol = semanticModel.GetSymbolInfo(invocation).Symbol;
        if (symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        if (methodSymbol.MethodKind != MethodKind.Ordinary)
        {
            return false;
        }

        if (!AccessibilityRules.IsInaccessibleFromExternalAssembly(methodSymbol))
        {
            return false;
        }

        if (methodSymbol.IsExtensionMethod)
        {
            rejectReason = "inaccessible extension method calls are not rewritten.";
            return false;
        }

        if (methodSymbol.IsGenericMethod)
        {
            rejectReason = "inaccessible generic method calls are not rewritten.";
            return false;
        }

        if (methodSymbol.ReturnsByRef || methodSymbol.ReturnsByRefReadonly)
        {
            rejectReason =
                "inaccessible methods that return by ref have no accessor rewrite shape.";
            return false;
        }

        foreach (IParameterSymbol parameter in methodSymbol.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                rejectReason =
                    "inaccessible method calls with ref/out/in parameters are not rewritten.";
                return false;
            }
        }

        foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon != null)
            {
                rejectReason =
                    "inaccessible method calls with named arguments are not rewritten.";
                return false;
            }
        }

        if (invocation.ArgumentList.Arguments.Count != methodSymbol.Parameters.Length)
        {
            rejectReason =
                "inaccessible method calls with omitted optional or expanded params arguments "
                + "are not rewritten.";
            return false;
        }

        plan.GetOrAddMethod(methodSymbol);
        return true;
    }

    internal static bool TryRegisterPropertyOrFieldRead(
        ISymbol symbol,
        AccessorPlan plan,
        out string rejectReason)
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
            if (!AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod))
            {
                return false;
            }

            return TryRegisterPropertyRead(propertySymbol, plan, out rejectReason);
        }

        if (symbol is IEventSymbol)
        {
            rejectReason = "inaccessible event add/remove is out of scope for accessor rewrite.";
            return false;
        }

        if (symbol is IMethodSymbol methodSymbol
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(methodSymbol))
        {
            rejectReason =
                "inaccessible method group (non-invocation) has no accessor rewrite shape.";
            return false;
        }

        if (symbol != null
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol)
            && symbol is not INamespaceSymbol
            && symbol is not ITypeSymbol
            && symbol is not ILocalSymbol
            && symbol is not IParameterSymbol)
        {
            rejectReason = "inaccessible member kind is not field/method/property access.";
            return false;
        }

        return false;
    }

    internal static bool TryRegisterPropertyRead(
        IPropertySymbol propertySymbol,
        AccessorPlan plan,
        out string rejectReason)
    {
        rejectReason = null;
        if (propertySymbol.IsIndexer)
        {
            rejectReason = "inaccessible indexer access has no accessor rewrite shape.";
            return false;
        }

        if (propertySymbol.IsStatic)
        {
            rejectReason =
                "inaccessible static property access has no accessor rewrite shape.";
            return false;
        }

        if (propertySymbol.ReturnsByRef || propertySymbol.ReturnsByRefReadonly)
        {
            rejectReason =
                "inaccessible ref-returning properties have no accessor rewrite shape.";
            return false;
        }

        plan.GetOrAddPropertyGetter(propertySymbol);
        return true;
    }
}
