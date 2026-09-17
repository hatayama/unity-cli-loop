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

/// <summary>
/// Detects added-property uses whose source shape cannot be rewritten without changing meaning.
/// </summary>
internal static class AddedPropertyBodyScan
{
    internal static WorkerReason EvaluateAddedPropertySkipReason(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedPropertyCatalog addedPropertyCatalog,
        INamedTypeSymbol enclosingType)
    {
        if (bodyNode == null || addedPropertyCatalog == null)
        {
            return null;
        }

        WorkerReason refArgumentReason = EvaluateRefArgumentSkipReason(
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
            WorkerReason expressionReason = EvaluateExpressionSkipReason(
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

    private static WorkerReason EvaluateRefArgumentSkipReason(
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
                return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyRefOutIn);
            }
        }

        return null;
    }

    private static WorkerReason EvaluateExpressionSkipReason(
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
            // The reason the accessor was refused is the only thing that tells a reader what to
            // change, so it is carried through instead of being replaced by the generic sentence.
            // Why the same code is not composed onto itself: a property excluded by key carries
            // this very reason, and nesting it would say the same thing twice.
            if (binding.UnavailableReason.Code
                == HotReloadWorkerReasonCode.AddedPropertyUnavailableAddedProperty)
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyUnavailableAddedProperty);
            }

            return WorkerReason.Composite(
                HotReloadWorkerReasonCode.AddedPropertyUnavailableAddedProperty,
                binding.UnavailableReason);
        }

        if (NameofRules.IsInsideNameofArgument(expression))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyNameofReference);
        }

        if (IsPropertyPatternMemberName(expression))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyPropertyPattern);
        }

        if (expression is MemberBindingExpressionSyntax || IsConditionalAccess(expression))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyConditionalAccess);
        }

        return EvaluateWriteSkipReason(expression);
    }

    private static WorkerReason EvaluateWriteSkipReason(ExpressionSyntax expression)
    {
        if (IsDeconstructionTarget(expression))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyDeconstructionTarget);
        }

        if (expression.Parent is AssignmentExpressionSyntax assignment && assignment.Left == expression)
        {
            if (assignment.Parent is InitializerExpressionSyntax)
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyObjectInitializer);
            }

            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyCompoundAssignment);
            }

            if (assignment.Parent is not ExpressionStatementSyntax)
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyConsumedWrite);
            }
        }

        if (expression.Parent is PrefixUnaryExpressionSyntax prefix
            && AddedFieldBodyScan.IsIncrementOrDecrement(prefix.Kind()))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyCompoundAssignment);
        }

        if (expression.Parent is PostfixUnaryExpressionSyntax postfix
            && AddedFieldBodyScan.IsIncrementOrDecrement(postfix.Kind()))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedPropertyCompoundAssignment);
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
