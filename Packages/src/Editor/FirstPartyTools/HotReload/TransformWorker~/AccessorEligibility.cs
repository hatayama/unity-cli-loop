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

/// <summary>
/// What: decides whether an async/iterator/closure private-access skip can be rescued by
/// rewriting inaccessible member accesses into Harmony accessor delegates (conditions b/c).
/// </summary>
internal static class AccessorEligibility
{
    public static bool TryBuildPlan(
        SemanticModel semanticModel,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol typeSymbol,
        SyntaxNode bodyNode,
        out AccessorPlan plan,
        out string rejectReason)
    {
        plan = null;
        rejectReason = null;

        if (!AccessibilityRules.IsExternallyVisibleType(typeSymbol))
        {
            rejectReason = "containing type is not visible from an external assembly (condition c).";
            return false;
        }

        if (!AreMethodSignatureTypesVisible(methodSymbol, out rejectReason))
        {
            return false;
        }

        if (!AreBodyTypeUsagesVisible(semanticModel, bodyNode, out rejectReason))
        {
            return false;
        }

        AccessorPlan built = new AccessorPlan();
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (TransformWorkerProgram.NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            if (!TryRegisterInaccessibleAccess(semanticModel, node, built, out rejectReason))
            {
                if (rejectReason != null)
                {
                    return false;
                }
            }
        }

        foreach (AccessorEntry entry in built.Entries)
        {
            if (entry.TryGetVisibilityFailure(out rejectReason))
            {
                rejectReason = rejectReason + " (condition c).";
                return false;
            }
        }

        if (NeedsPropertyIncrementRewrite(semanticModel, bodyNode))
        {
            rejectReason =
                "inaccessible property increment/decrement has no accessor rewrite shape.";
            return false;
        }

        plan = built;
        rejectReason = null;
        return true;
    }

    private static bool AreMethodSignatureTypesVisible(IMethodSymbol methodSymbol, out string rejectReason)
    {
        if (!AccessibilityRules.IsExternallyVisibleType(methodSymbol.ReturnType))
        {
            rejectReason = "method return type is not visible from an external assembly (condition c).";
            return false;
        }

        foreach (IParameterSymbol parameter in methodSymbol.Parameters)
        {
            if (!AccessibilityRules.IsExternallyVisibleType(parameter.Type))
            {
                rejectReason =
                    "method parameter type is not visible from an external assembly (condition c).";
                return false;
            }
        }

        rejectReason = null;
        return true;
    }

    private static bool AreBodyTypeUsagesVisible(
        SemanticModel semanticModel,
        SyntaxNode bodyNode,
        out string rejectReason)
    {
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (TransformWorkerProgram.NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            ITypeSymbol typeSymbol = null;
            if (node is TypeSyntax typeSyntax)
            {
                typeSymbol = semanticModel.GetTypeInfo(typeSyntax).Type
                    ?? semanticModel.GetSymbolInfo(typeSyntax).Symbol as ITypeSymbol;
            }
            else if (node is VariableDeclarationSyntax variableDeclaration
                && variableDeclaration.Type.IsVar)
            {
                typeSymbol = semanticModel.GetTypeInfo(variableDeclaration.Type).Type;
            }
            else if (node is ImplicitObjectCreationExpressionSyntax implicitObjectCreation)
            {
                typeSymbol = semanticModel.GetTypeInfo(implicitObjectCreation).Type;
            }

            if (typeSymbol == null || typeSymbol.TypeKind == TypeKind.Error)
            {
                continue;
            }

            if (!AccessibilityRules.IsExternallyVisibleType(typeSymbol))
            {
                rejectReason = "body uses a type that is not visible from an external assembly: "
                    + typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    + " (condition c).";
                return false;
            }
        }

        rejectReason = null;
        return true;
    }

    private static bool NeedsPropertyIncrementRewrite(SemanticModel semanticModel, SyntaxNode bodyNode)
    {
        foreach (SyntaxNode node in bodyNode.DescendantNodes())
        {
            if (TransformWorkerProgram.NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            ExpressionSyntax operand = null;
            if (node is PostfixUnaryExpressionSyntax postfix
                && (postfix.IsKind(SyntaxKind.PostIncrementExpression)
                    || postfix.IsKind(SyntaxKind.PostDecrementExpression)))
            {
                operand = postfix.Operand;
            }
            else if (node is PrefixUnaryExpressionSyntax prefix
                && (prefix.IsKind(SyntaxKind.PreIncrementExpression)
                    || prefix.IsKind(SyntaxKind.PreDecrementExpression)))
            {
                operand = prefix.Operand;
            }

            if (operand == null)
            {
                continue;
            }

            ISymbol symbol = semanticModel.GetSymbolInfo(operand).Symbol;
            if (symbol is IPropertySymbol propertySymbol
                && (AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod)
                    || AccessibilityRules.IsInaccessibleAccessor(propertySymbol.SetMethod)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns false with null rejectReason when the node is not an inaccessible access site.
    /// Returns false with a reason when the site is inaccessible but not rewriteable.
    /// Returns true when the site was registered (or was already present).
    /// </summary>
    private static bool TryRegisterInaccessibleAccess(
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

    private static bool TryRegisterObjectCreation(
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

    private static bool TryRegisterInitializerAssignment(
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

    private static bool TryRegisterElementAccess(
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

    private static bool TryRegisterMemberBinding(
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

    private static bool TryRegisterMemberAccess(
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

    private static bool TryRegisterSimpleName(
        SemanticModel semanticModel,
        SimpleNameSyntax name,
        AccessorPlan plan,
        out string rejectReason)
    {
        rejectReason = null;
        if (IsNameHandledByParent(name))
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

    private static bool IsInaccessibleBindingTarget(ISymbol bound)
    {
        // Member binding only appears in read/invoke contexts (x?.P = v is not valid C#).
        if (bound is IPropertySymbol propertySymbol)
        {
            return AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod);
        }

        return AccessibilityRules.IsInaccessibleFromExternalAssembly(bound);
    }

    private static bool TryRegisterAssignment(
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
            return TryRegisterPropertyWrite(
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

    private static bool TryRegisterInvocation(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        AccessorPlan plan,
        out string rejectReason)
    {
        rejectReason = null;
        if (TransformWorkerProgram.NameofRules.IsNameofInvocation(invocation))
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

    private static bool TryRegisterPropertyOrFieldRead(
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

    private static bool TryRegisterPropertyRead(
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

    private static bool TryRegisterPropertyWrite(
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

    private static string TryGetPropertyWriteShapeRejectReason(
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

        if (needsGetter && !IsSupportedCompoundAssignmentKind(assignment.Kind()))
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
        if (needsGetter && !IsSideEffectFreeAssignmentReceiver(semanticModel, assignment.Left))
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

    internal static bool IsSupportedCompoundAssignmentKind(SyntaxKind kind)
    {
        return kind == SyntaxKind.AddAssignmentExpression
            || kind == SyntaxKind.SubtractAssignmentExpression
            || kind == SyntaxKind.MultiplyAssignmentExpression
            || kind == SyntaxKind.DivideAssignmentExpression
            || kind == SyntaxKind.ModuloAssignmentExpression
            || kind == SyntaxKind.AndAssignmentExpression
            || kind == SyntaxKind.ExclusiveOrAssignmentExpression
            || kind == SyntaxKind.OrAssignmentExpression
            || kind == SyntaxKind.LeftShiftAssignmentExpression
            || kind == SyntaxKind.RightShiftAssignmentExpression
            || kind == SyntaxKind.UnsignedRightShiftAssignmentExpression;
    }

    /// <summary>
    /// What: whether an assignment left's receiver chain is free of re-evaluable members
    /// (properties/methods). Only this/locals/parameters/fields (and type/namespace qualifiers)
    /// are allowed — FieldRef re-reads the same storage, so field links are idempotent.
    /// </summary>
    internal static bool IsSideEffectFreeAssignmentReceiver(
        SemanticModel semanticModel,
        ExpressionSyntax left)
    {
        ExpressionSyntax receiver = left is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Expression
            : null;
        if (receiver == null)
        {
            // Bare member — implicit this.
            return true;
        }

        ExpressionSyntax current = receiver;
        while (current is MemberAccessExpressionSyntax nested)
        {
            ISymbol linkSymbol = semanticModel.GetSymbolInfo(nested.Name).Symbol
                ?? semanticModel.GetSymbolInfo(nested).Symbol;
            if (!IsSideEffectFreeReceiverLink(linkSymbol))
            {
                return false;
            }

            current = nested.Expression;
        }

        if (current is ThisExpressionSyntax || current is BaseExpressionSyntax)
        {
            return true;
        }

        if (current is IdentifierNameSyntax)
        {
            ISymbol headSymbol = semanticModel.GetSymbolInfo(current).Symbol;
            return headSymbol is ILocalSymbol
                || headSymbol is IParameterSymbol
                || headSymbol is IFieldSymbol
                || headSymbol is ITypeSymbol
                || headSymbol is INamespaceSymbol;
        }

        return false;
    }

    private static bool IsSideEffectFreeReceiverLink(ISymbol linkSymbol)
    {
        // Fields re-read the same storage; type/namespace qualifiers are not evaluated.
        return linkSymbol is IFieldSymbol
            || linkSymbol is ITypeSymbol
            || linkSymbol is INamespaceSymbol;
    }

    public static bool IsNameHandledByParent(SimpleNameSyntax node)
    {
        if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node)
        {
            return true;
        }

        if (node.Parent is QualifiedNameSyntax qualifiedName && qualifiedName.Right == node)
        {
            return true;
        }

        if (node.Parent is MemberBindingExpressionSyntax memberBinding && memberBinding.Name == node)
        {
            return true;
        }

        // Invocation targets are NOT handled here: method groups are skipped by the caller after
        // a symbol check; delegate-typed field invokes must reach the field-read path.
        return false;
    }
}
