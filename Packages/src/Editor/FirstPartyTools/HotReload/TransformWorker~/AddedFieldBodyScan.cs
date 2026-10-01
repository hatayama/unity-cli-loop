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
    internal static WorkerReason BodyReferencesUnavailableAddedField(
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

            string storeKey = FormatAddedStoreKeyOrNull(TryGetFieldSymbolOrCandidate(semanticModel, node))
                ?? FormatAddedStoreKeyOrNull(semanticModel.GetSymbolInfo(node).Symbol as IEventSymbol);
            if (storeKey == null)
            {
                continue;
            }

            AddedFieldBinding binding = addedFieldCatalog.FindOrNull(storeKey);
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

    // Reports the first assignment to an added field that neither the simple-assignment rewrite
    // nor a supported compound one covers, together with the field it writes and the operator it
    // used. Why only the first: the caller reports one sentence, and the earliest such assignment
    // is the one a reader has to change before anything downstream of it can be judged.
    internal static bool TryFindUnsupportedAddedFieldCompound(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog,
        out string fieldName,
        out SyntaxKind assignmentKind)
    {
        foreach (AssignmentExpressionSyntax assignment in bodyNode.DescendantNodesAndSelf()
            .OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                || AccessorEligibility.IsSupportedCompoundAssignmentKind(assignment.Kind()))
            {
                continue;
            }

            if (!IsStoreAddedField(semanticModel, assignment.Left, addedFieldCatalog))
            {
                continue;
            }

            IFieldSymbol field = TryGetFieldSymbol(semanticModel, assignment.Left);
            fieldName = field != null ? field.Name : assignment.Left.ToString();
            assignmentKind = assignment.Kind();
            return true;
        }

        fieldName = null;
        assignmentKind = SyntaxKind.None;
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

    // Why a deconstruction element is a skip: it has to be assignable, and the store rewrite
    // turns an added field into a GetOrInit call whose value cannot be assigned, so the shim would
    // fail to compile instead of skipping the method.
    internal static bool BodyDeconstructsIntoAddedField(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedFieldCatalog addedFieldCatalog)
    {
        foreach (ExpressionSyntax expression in bodyNode.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
        {
            if (AssignmentTargetRules.IsDeconstructionTarget(expression)
                && IsStoreAddedField(semanticModel, expression, addedFieldCatalog))
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

    internal static bool IsIncrementOrDecrement(SyntaxKind kind)
    {
        return kind == SyntaxKind.PreIncrementExpression
            || kind == SyntaxKind.PreDecrementExpression
            || kind == SyntaxKind.PostIncrementExpression
            || kind == SyntaxKind.PostDecrementExpression;
    }

    // Fields and field-like events alike: an added event the store keeps is written and read
    // through the same store calls, so it is refused for the same shapes.
    internal static bool IsStoreAddedField(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        AddedFieldCatalog addedFieldCatalog)
    {
        return FindStoreBinding(semanticModel, expression, addedFieldCatalog) != null;
    }

    internal static bool IsStoreAddedInstanceField(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        AddedFieldCatalog addedFieldCatalog)
    {
        AddedFieldBinding binding = FindStoreBinding(semanticModel, expression, addedFieldCatalog);
        return binding != null && !binding.IsStatic;
    }

    private static AddedFieldBinding FindStoreBinding(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        AddedFieldCatalog addedFieldCatalog)
    {
        if (expression == null)
        {
            return null;
        }

        // Why unparenthesized: '(E) += h' writes E, and the symbol of the parentheses is not E's.
        ExpressionSyntax target = AssignmentTargetRules.Unparenthesized(expression);
        AddedFieldBinding binding = addedFieldCatalog.FindOrNull(
            FormatAddedStoreKeyOrNull(semanticModel.GetSymbolInfo(target).Symbol));
        return binding != null && binding.IsStoreRewriteable ? binding : null;
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

    /// <summary>
    /// The store key of an added field or field-like event, or null for any other symbol. Both
    /// are keyed by the declaring type's metadata name, whichever type the use is emitted from.
    /// </summary>
    internal static string FormatAddedStoreKeyOrNull(ISymbol symbol)
    {
        if (symbol is IFieldSymbol fieldSymbol)
        {
            return FormatAddedFieldKeyFromSymbol(fieldSymbol);
        }

        if (symbol is IEventSymbol eventSymbol && eventSymbol.ContainingType != null)
        {
            return AddedFieldClassifier.FormatAddedFieldStoreKey(
                CecilTypeNames.ToMetadataName(eventSymbol.ContainingType.OriginalDefinition),
                eventSymbol.Name);
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
