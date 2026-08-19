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
            if (NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            if (!AccessorAccessRegistrar.TryRegisterInaccessibleAccess(semanticModel, node, built, out rejectReason))
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
            if (NameofRules.IsInsideNameofArgument(node))
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
            if (NameofRules.IsInsideNameofArgument(node))
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
