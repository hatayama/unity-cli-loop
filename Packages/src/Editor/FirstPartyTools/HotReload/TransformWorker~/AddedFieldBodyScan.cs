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

internal static class AddedFieldBodyScan
{
    internal static string BodyReferencesUnavailableAddedField(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            IFieldSymbol field = TryGetFieldSymbolOrCandidate(semanticModel, node);
            if (field == null)
            {
                continue;
            }

            AddedFieldBinding binding = addedFieldCatalog.FindOrNull(FormatAddedFieldKeyFromSymbol(field));
            if (binding != null && binding.UnavailableReason != null)
            {
                return binding.UnavailableReason;
            }
        }

        return null;
    }

    internal static bool BodyPassesAddedFieldByRef(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (ArgumentSyntax argument in bodyNode.DescendantNodesAndSelf().OfType<ArgumentSyntax>())
        {
            if (argument.RefKindKeyword.Kind() != SyntaxKind.RefKeyword
                && argument.RefKindKeyword.Kind() != SyntaxKind.OutKeyword
                && argument.RefKindKeyword.Kind() != SyntaxKind.InKeyword)
            {
                continue;
            }

            if (IsStoreAddedField(semanticModel, argument.Expression, addedFieldCatalog))
            {
                return true;
            }
        }

        foreach (RefExpressionSyntax refExpression in bodyNode.DescendantNodesAndSelf()
            .OfType<RefExpressionSyntax>())
        {
            if (IsStoreAddedField(semanticModel, refExpression.Expression, addedFieldCatalog))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool BodyHasUnsupportedAddedFieldCompound(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (AssignmentExpressionSyntax assignment in bodyNode.DescendantNodesAndSelf()
            .OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                || AccessorEligibility.IsSupportedCompoundAssignmentKind(assignment.Kind()))
            {
                continue;
            }

            if (IsStoreAddedField(semanticModel, assignment.Left, addedFieldCatalog))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool BodyHasConsumedAddedFieldWrite(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (AssignmentExpressionSyntax assignment in bodyNode.DescendantNodesAndSelf()
            .OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Parent is ExpressionStatementSyntax)
            {
                continue;
            }

            if (IsStoreAddedField(semanticModel, assignment.Left, addedFieldCatalog))
            {
                return true;
            }
        }

        foreach (PrefixUnaryExpressionSyntax prefix in bodyNode.DescendantNodesAndSelf()
            .OfType<PrefixUnaryExpressionSyntax>())
        {
            if (!IsIncrementOrDecrement(prefix.Kind()) || prefix.Parent is ExpressionStatementSyntax)
            {
                continue;
            }

            if (IsStoreAddedField(semanticModel, prefix.Operand, addedFieldCatalog))
            {
                return true;
            }
        }

        foreach (PostfixUnaryExpressionSyntax postfix in bodyNode.DescendantNodesAndSelf()
            .OfType<PostfixUnaryExpressionSyntax>())
        {
            if (!IsIncrementOrDecrement(postfix.Kind()) || postfix.Parent is ExpressionStatementSyntax)
            {
                continue;
            }

            if (IsStoreAddedField(semanticModel, postfix.Operand, addedFieldCatalog))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool BodyHasDoubleEvalAddedFieldReceiver(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (AssignmentExpressionSyntax assignment in bodyNode.DescendantNodesAndSelf()
            .OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                || !IsStoreAddedInstanceField(semanticModel, assignment.Left, addedFieldCatalog))
            {
                continue;
            }

            if (!AccessorEligibility.IsSideEffectFreeAssignmentReceiver(semanticModel, assignment.Left))
            {
                return true;
            }
        }

        foreach (PrefixUnaryExpressionSyntax prefix in bodyNode.DescendantNodesAndSelf()
            .OfType<PrefixUnaryExpressionSyntax>())
        {
            if (!IsIncrementOrDecrement(prefix.Kind())
                || !IsStoreAddedInstanceField(semanticModel, prefix.Operand, addedFieldCatalog))
            {
                continue;
            }

            if (!AccessorEligibility.IsSideEffectFreeAssignmentReceiver(semanticModel, prefix.Operand))
            {
                return true;
            }
        }

        foreach (PostfixUnaryExpressionSyntax postfix in bodyNode.DescendantNodesAndSelf()
            .OfType<PostfixUnaryExpressionSyntax>())
        {
            if (!IsIncrementOrDecrement(postfix.Kind())
                || !IsStoreAddedInstanceField(semanticModel, postfix.Operand, addedFieldCatalog))
            {
                continue;
            }

            if (!AccessorEligibility.IsSideEffectFreeAssignmentReceiver(semanticModel, postfix.Operand))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool BodyHasNonNumericAddedFieldIncrement(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (PrefixUnaryExpressionSyntax prefix in bodyNode.DescendantNodesAndSelf()
            .OfType<PrefixUnaryExpressionSyntax>())
        {
            if (IsNonNumericAddedFieldIncrement(semanticModel, prefix.Kind(), prefix.Operand, addedFieldCatalog))
            {
                return true;
            }
        }

        foreach (PostfixUnaryExpressionSyntax postfix in bodyNode.DescendantNodesAndSelf()
            .OfType<PostfixUnaryExpressionSyntax>())
        {
            if (IsNonNumericAddedFieldIncrement(semanticModel, postfix.Kind(), postfix.Operand, addedFieldCatalog))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsNonNumericAddedFieldIncrement(
        SemanticModel semanticModel,
        SyntaxKind kind,
        ExpressionSyntax operand,
        AddedFieldCatalog addedFieldCatalog)
    {
        if (!IsIncrementOrDecrement(kind) || !IsStoreAddedField(semanticModel, operand, addedFieldCatalog))
        {
            return false;
        }

        IFieldSymbol field = TryGetFieldSymbol(semanticModel, operand);
        return field != null && !IsIncrementablePrimitiveOrEnum(field.Type);
    }

    internal static bool IsIncrementablePrimitiveOrEnum(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
        {
            return false;
        }

        if (typeSymbol.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        return IsIncrementableSpecialType(typeSymbol.SpecialType);
    }

    internal static bool IsIncrementableSpecialType(SpecialType specialType)
    {
        switch (specialType)
        {
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Char:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return true;
            default:
                return false;
        }
    }

    internal static bool BodyHasValueTypeAddedFieldMemberWrite(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (AssignmentExpressionSyntax assignment in bodyNode.DescendantNodesAndSelf()
            .OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is MemberAccessExpressionSyntax memberAccess
                && IsStoreAddedValueTypeField(semanticModel, memberAccess.Expression, addedFieldCatalog))
            {
                return true;
            }
        }

        foreach (InvocationExpressionSyntax invocation in bodyNode.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>())
        {
            if (NameofRules.IsNameofInvocation(invocation)
                || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                continue;
            }

            ISymbol invoked = semanticModel.GetSymbolInfo(invocation).Symbol;
            if (invoked is IMethodSymbol methodSymbol
                && !methodSymbol.IsStatic
                && IsStoreAddedValueTypeField(semanticModel, memberAccess.Expression, addedFieldCatalog))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsIncrementOrDecrement(SyntaxKind kind)
    {
        return kind == SyntaxKind.PreIncrementExpression
            || kind == SyntaxKind.PreDecrementExpression
            || kind == SyntaxKind.PostIncrementExpression
            || kind == SyntaxKind.PostDecrementExpression;
    }

    internal static bool IsStoreAddedField(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        AddedFieldCatalog addedFieldCatalog)
    {
        IFieldSymbol field = TryGetFieldSymbol(semanticModel, expression);
        if (field == null)
        {
            return false;
        }

        AddedFieldBinding binding = addedFieldCatalog.FindOrNull(FormatAddedFieldKeyFromSymbol(field));
        return binding != null && binding.IsStoreRewriteable;
    }

    internal static bool IsStoreAddedInstanceField(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        AddedFieldCatalog addedFieldCatalog)
    {
        IFieldSymbol field = TryGetFieldSymbol(semanticModel, expression);
        if (field == null || field.IsStatic)
        {
            return false;
        }

        AddedFieldBinding binding = addedFieldCatalog.FindOrNull(FormatAddedFieldKeyFromSymbol(field));
        return binding != null && binding.IsStoreRewriteable;
    }

    internal static bool IsStoreAddedValueTypeField(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        AddedFieldCatalog addedFieldCatalog)
    {
        IFieldSymbol field = TryGetFieldSymbol(semanticModel, expression);
        if (field == null || !field.Type.IsValueType)
        {
            return false;
        }

        AddedFieldBinding binding = addedFieldCatalog.FindOrNull(FormatAddedFieldKeyFromSymbol(field));
        return binding != null && binding.IsStoreRewriteable;
    }

    internal static IFieldSymbol TryGetFieldSymbol(SemanticModel semanticModel, SyntaxNode node)
    {
        if (node == null)
        {
            return null;
        }

        ISymbol symbol = semanticModel.GetSymbolInfo(node).Symbol;
        return symbol as IFieldSymbol;
    }

    internal static IFieldSymbol TryGetFieldSymbolOrCandidate(
        SemanticModel semanticModel,
        SyntaxNode node)
    {
        IFieldSymbol field = TryGetFieldSymbol(semanticModel, node);
        if (field != null)
        {
            return field;
        }

        if (node == null)
        {
            return null;
        }

        // Why candidates: assigning to a const (or other illegal field use) still
        // names that field, but GetSymbolInfo leaves it in CandidateSymbols.
        foreach (ISymbol candidate in semanticModel.GetSymbolInfo(node).CandidateSymbols)
        {
            if (candidate is IFieldSymbol candidateField)
            {
                return candidateField;
            }
        }

        return null;
    }

    internal static string FormatAddedFieldKeyFromSymbol(IFieldSymbol fieldSymbol)
    {
        if (fieldSymbol.ContainingType == null)
        {
            return fieldSymbol.Name;
        }

        return AddedFieldClassifier.FormatAddedFieldStoreKey(
            CecilTypeNames.ToMetadataName(fieldSymbol.ContainingType),
            fieldSymbol.Name);
    }
}
