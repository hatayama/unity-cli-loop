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

internal static class InaccessibleAccessScanner
{
    internal static bool SubtreeHasInaccessibleMemberAccess(
        SemanticModel semanticModel,
        IEnumerable<SyntaxNode> roots)
    {
        foreach (SyntaxNode root in roots)
        {
            if (root == null)
            {
                continue;
            }

            foreach (SyntaxNode node in root.DescendantNodesAndSelf())
            {
                if (NameofRules.IsInsideNameofArgument(node))
                {
                    continue;
                }

                if (HasInaccessibleAccessAtNode(semanticModel, node))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// What: site-aware inaccessible access detection (property get vs set, ctor, etc.).
    /// </summary>
    internal static bool HasInaccessibleAccessAtNode(SemanticModel semanticModel, SyntaxNode node)
    {
        if (node is AssignmentExpressionSyntax assignment)
        {
            return IsInaccessibleAssignment(semanticModel, assignment);
        }

        if (node is PostfixUnaryExpressionSyntax postfix)
        {
            return IsInaccessiblePostfixIncrement(semanticModel, postfix);
        }

        if (node is PrefixUnaryExpressionSyntax prefix)
        {
            return IsInaccessiblePrefixIncrement(semanticModel, prefix);
        }

        if (node is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax)
        {
            return IsInaccessibleObjectCreation(semanticModel, node);
        }

        if (node is InvocationExpressionSyntax invocation)
        {
            return IsInaccessibleInvocation(semanticModel, invocation);
        }

        if (node is ElementAccessExpressionSyntax elementAccess)
        {
            return IsInaccessibleElementAccess(semanticModel, elementAccess);
        }

        if (node is MemberBindingExpressionSyntax memberBinding)
        {
            return IsInaccessibleMemberBinding(semanticModel, memberBinding);
        }

        if (node is IdentifierNameSyntax or GenericNameSyntax)
        {
            return IsInaccessibleSimpleName(semanticModel, (SimpleNameSyntax)node);
        }

        if (node is MemberAccessExpressionSyntax memberAccess)
        {
            return IsInaccessibleMemberAccess(semanticModel, memberAccess);
        }

        return false;
    }

    internal static bool IsInaccessibleAssignment(
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment)
    {
        if (assignment.Parent is InitializerExpressionSyntax)
        {
            // Initializer assignments are always writes (including ImplicitElementAccess indexers).
            ISymbol initializerSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
            if (initializerSymbol is IPropertySymbol initializerProperty)
            {
                return AccessibilityRules.IsInaccessibleAccessor(initializerProperty.SetMethod);
            }

            return IsInaccessibleNonConstSymbol(initializerSymbol);
        }

        ISymbol leftSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
        if (leftSymbol is IPropertySymbol propertySymbol)
        {
            bool needsGetter = !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);
            if (needsGetter && AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod))
            {
                return true;
            }

            return AccessibilityRules.IsInaccessibleAccessor(propertySymbol.SetMethod);
        }

        return IsInaccessibleNonConstSymbol(leftSymbol);
    }

    internal static bool IsInaccessiblePostfixIncrement(
        SemanticModel semanticModel,
        PostfixUnaryExpressionSyntax postfix)
    {
        if (!(postfix.IsKind(SyntaxKind.PostIncrementExpression)
            || postfix.IsKind(SyntaxKind.PostDecrementExpression)))
        {
            return false;
        }

        return IsInaccessibleIncrementOperand(semanticModel, postfix.Operand);
    }

    internal static bool IsInaccessiblePrefixIncrement(
        SemanticModel semanticModel,
        PrefixUnaryExpressionSyntax prefix)
    {
        if (!(prefix.IsKind(SyntaxKind.PreIncrementExpression)
            || prefix.IsKind(SyntaxKind.PreDecrementExpression)))
        {
            return false;
        }

        return IsInaccessibleIncrementOperand(semanticModel, prefix.Operand);
    }

    internal static bool IsInaccessibleObjectCreation(SemanticModel semanticModel, SyntaxNode node)
    {
        ISymbol ctorSymbol = semanticModel.GetSymbolInfo(node).Symbol;
        return ctorSymbol != null
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(ctorSymbol);
    }

    internal static bool IsInaccessibleInvocation(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation)
    {
        if (NameofRules.IsNameofInvocation(invocation))
        {
            return false;
        }

        ISymbol symbol = semanticModel.GetSymbolInfo(invocation).Symbol;
        return symbol is IMethodSymbol methodSymbol
            && methodSymbol.MethodKind == MethodKind.Ordinary
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(methodSymbol);
    }

    internal static bool IsInaccessibleElementAccess(
        SemanticModel semanticModel,
        ElementAccessExpressionSyntax elementAccess)
    {
        // Assignment-left ElementAccess is owned by the assignment branch (write context).
        if (elementAccess.Parent is AssignmentExpressionSyntax parentAssignment
            && parentAssignment.Left == elementAccess)
        {
            return false;
        }

        ISymbol symbol = semanticModel.GetSymbolInfo(elementAccess).Symbol;
        if (symbol is IPropertySymbol indexer)
        {
            // Standalone ElementAccess is a read.
            return AccessibilityRules.IsInaccessibleAccessor(indexer.GetMethod);
        }

        return IsInaccessibleNonConstSymbol(symbol);
    }

    internal static bool IsInaccessibleMemberBinding(
        SemanticModel semanticModel,
        MemberBindingExpressionSyntax memberBinding)
    {
        // ?.Member — visibility of the bound member (not the receiver).
        ISymbol bound = semanticModel.GetSymbolInfo(memberBinding.Name).Symbol;
        if (bound is IPropertySymbol propertySymbol)
        {
            return AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod);
        }

        return bound != null
            && bound is not INamespaceSymbol
            && bound is not ITypeSymbol
            && IsInaccessibleNonConstSymbol(bound);
    }

    internal static bool IsInaccessibleSimpleName(SemanticModel semanticModel, SimpleNameSyntax name)
    {
        if (AccessorEligibility.IsNameHandledByParent(name))
        {
            return false;
        }

        if (name.Parent is AssignmentExpressionSyntax parentAssignment
            && parentAssignment.Left == name)
        {
            return false;
        }

        // Invocation-target exclusion applies only to method groups; delegate-typed fields
        // invoked as `_cb()` must be detected as field reads.
        if (name.Parent is InvocationExpressionSyntax parentInvocation
            && parentInvocation.Expression == name)
        {
            ISymbol invocationTarget = semanticModel.GetSymbolInfo(name).Symbol;
            if (invocationTarget is IMethodSymbol)
            {
                return false;
            }
        }

        ISymbol symbol = semanticModel.GetSymbolInfo(name).Symbol;
        if (symbol is IPropertySymbol propertySymbol)
        {
            return AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod);
        }

        return IsInaccessibleNonConstSymbol(symbol);
    }

    internal static bool IsInaccessibleMemberAccess(
        SemanticModel semanticModel,
        MemberAccessExpressionSyntax memberAccess)
    {
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

        ISymbol symbol = semanticModel.GetSymbolInfo(memberAccess).Symbol
            ?? semanticModel.GetSymbolInfo(memberAccess.Name).Symbol;
        if (symbol is IPropertySymbol propertySymbol)
        {
            return AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod);
        }

        return IsInaccessibleNonConstSymbol(symbol);
    }

    // Why exclude const: a const field is IsStatic, but it has no runtime storage.
    // Publicized references fold the literal at compile time, so treating const as
    // inaccessible would force a StaticFieldRefAccess bind that cannot succeed.
    internal static bool IsInaccessibleNonConstSymbol(ISymbol symbol)
    {
        if (symbol is IFieldSymbol fieldSymbol && fieldSymbol.IsConst)
        {
            return false;
        }

        return symbol != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(symbol);
    }

    internal static bool IsInaccessibleIncrementOperand(
        SemanticModel semanticModel,
        ExpressionSyntax operand)
    {
        ISymbol symbol = semanticModel.GetSymbolInfo(operand).Symbol;
        if (symbol is IPropertySymbol propertySymbol)
        {
            return AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod)
                || AccessibilityRules.IsInaccessibleAccessor(propertySymbol.SetMethod);
        }

        return IsInaccessibleNonConstSymbol(symbol);
    }
}
