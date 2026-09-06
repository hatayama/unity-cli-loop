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
/// Detects added-property uses whose source shape cannot be rewritten without changing meaning.
/// </summary>
internal static class AddedPropertyBodyScan
{
    internal static string EvaluateAddedPropertySkipReason(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedPropertyCatalog addedPropertyCatalog,
        INamedTypeSymbol enclosingType)
    {
        if (bodyNode == null || addedPropertyCatalog == null)
        {
            return null;
        }

        string refArgumentReason = EvaluateRefArgumentSkipReason(
            bodyNode,
            semanticModel,
            addedPropertyCatalog,
            enclosingType);
        if (refArgumentReason != null)
        {
            return refArgumentReason;
        }

        foreach (ExpressionSyntax expression in bodyNode.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
        {
            string expressionReason = EvaluateExpressionSkipReason(
                expression,
                semanticModel,
                addedPropertyCatalog,
                enclosingType);
            if (expressionReason != null)
            {
                return expressionReason;
            }
        }

        return null;
    }

    private static string EvaluateRefArgumentSkipReason(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedPropertyCatalog addedPropertyCatalog,
        INamedTypeSymbol enclosingType)
    {
        foreach (ArgumentSyntax argument in bodyNode.DescendantNodesAndSelf().OfType<ArgumentSyntax>())
        {
            if (!HasRefKind(argument))
            {
                continue;
            }

            AddedPropertyBinding binding = FindBinding(
                argument.Expression,
                semanticModel,
                addedPropertyCatalog,
                enclosingType);
            if (binding != null)
            {
                return AddedPropertySkipReasons.RefOutIn;
            }
        }

        return null;
    }

    private static string EvaluateExpressionSkipReason(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        AddedPropertyCatalog addedPropertyCatalog,
        INamedTypeSymbol enclosingType)
    {
        AddedPropertyBinding binding = FindBinding(
            expression,
            semanticModel,
            addedPropertyCatalog,
            enclosingType);
        if (binding == null)
        {
            return null;
        }

        if (binding.UnavailableReason != null)
        {
            return AddedPropertySkipReasons.UnavailableAddedProperty;
        }

        if (NameofRules.IsInsideNameofArgument(expression))
        {
            return AddedPropertySkipReasons.NameofReference;
        }

        if (IsPropertyPatternMemberName(expression))
        {
            return AddedPropertySkipReasons.PropertyPattern;
        }

        if (expression is MemberBindingExpressionSyntax || IsConditionalAccess(expression))
        {
            return AddedPropertySkipReasons.ConditionalAccess;
        }

        return EvaluateWriteSkipReason(expression);
    }

    private static string EvaluateWriteSkipReason(ExpressionSyntax expression)
    {
        if (IsDeconstructionTarget(expression))
        {
            return AddedPropertySkipReasons.DeconstructionTarget;
        }

        if (expression.Parent is AssignmentExpressionSyntax assignment && assignment.Left == expression)
        {
            if (assignment.Parent is InitializerExpressionSyntax)
            {
                return AddedPropertySkipReasons.ObjectInitializer;
            }

            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                return AddedPropertySkipReasons.CompoundAssignment;
            }

            if (assignment.Parent is not ExpressionStatementSyntax)
            {
                return AddedPropertySkipReasons.ConsumedWrite;
            }
        }

        if (expression.Parent is PrefixUnaryExpressionSyntax prefix
            && IsIncrementOrDecrement(prefix.Kind()))
        {
            return AddedPropertySkipReasons.CompoundAssignment;
        }

        if (expression.Parent is PostfixUnaryExpressionSyntax postfix
            && IsIncrementOrDecrement(postfix.Kind()))
        {
            return AddedPropertySkipReasons.CompoundAssignment;
        }

        return null;
    }

    // Why a walk instead of a single parent test: deconstruction targets nest, so an added
    // property can sit in an inner tuple such as (first, (Count, second)) = ...
    private static bool IsDeconstructionTarget(ExpressionSyntax expression)
    {
        SyntaxNode current = expression;
        while (current.Parent is ArgumentSyntax argument && argument.Parent is TupleExpressionSyntax tuple)
        {
            if (tuple.Parent is AssignmentExpressionSyntax assignment && assignment.Left == tuple)
            {
                return true;
            }

            current = tuple;
        }

        return false;
    }

    private static bool IsIncrementOrDecrement(SyntaxKind kind)
    {
        return kind == SyntaxKind.PreIncrementExpression
            || kind == SyntaxKind.PreDecrementExpression
            || kind == SyntaxKind.PostIncrementExpression
            || kind == SyntaxKind.PostDecrementExpression;
    }

    private static bool HasRefKind(ArgumentSyntax argument)
    {
        SyntaxKind kind = argument.RefKindKeyword.Kind();
        return kind == SyntaxKind.RefKeyword
            || kind == SyntaxKind.OutKeyword
            || kind == SyntaxKind.InKeyword;
    }

    private static AddedPropertyBinding FindBinding(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        AddedPropertyCatalog addedPropertyCatalog,
        INamedTypeSymbol enclosingType)
    {
        ISymbol symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        IPropertySymbol propertySymbol = symbol as IPropertySymbol;
        if (propertySymbol == null && symbol is IMethodSymbol accessorSymbol)
        {
            propertySymbol = accessorSymbol.AssociatedSymbol as IPropertySymbol;
        }

        AddedPropertyBinding binding = addedPropertyCatalog.FindBySymbolOrNull(propertySymbol);
        if (binding != null || symbol != null || enclosingType == null)
        {
            return binding;
        }

        if (expression is not IdentifierNameSyntax identifier)
        {
            return null;
        }

        return addedPropertyCatalog.FindOrNull(AddedPropertyCatalog.FormatPropertyKey(
            CecilTypeNames.ToMetadataName(enclosingType),
            identifier.Identifier.ValueText));
    }

    // Why the walk needs no spine test: it stops as soon as a parent is not an expression, and
    // every way into the when-not-null side (an argument, a bracketed index, an interpolation)
    // passes through such a node. Only the receiver spine, as in Label?.Length, reaches here.
    // Why the colon's parent must be a subpattern: NameColonSyntax also carries named arguments
    // such as Call(value: Added), where the name's sibling is a real read that must still be
    // rewritten. Only a pattern member name is a position no expression can stand in.
    private static bool IsPropertyPatternMemberName(ExpressionSyntax expression)
    {
        SyntaxNode current = expression;
        while (current != null)
        {
            if (current.Parent is BaseExpressionColonSyntax colon
                && colon.Expression == current
                && colon.Parent is SubpatternSyntax)
            {
                return true;
            }

            if (current.Parent is not ExpressionSyntax parent)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    private static bool IsConditionalAccess(ExpressionSyntax expression)
    {
        SyntaxNode current = expression;
        while (current != null)
        {
            if (current is ConditionalAccessExpressionSyntax)
            {
                return true;
            }

            if (current.Parent is not ExpressionSyntax parent)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }
}
