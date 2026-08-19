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
/// Qualifies bare instance/static member references and, when an accessor plan is supplied,
/// rewrites inaccessible field/method/property accesses into Harmony accessor delegate calls.
/// </summary>
internal sealed class ShimBodyRewriter : CSharpSyntaxRewriter
{
    internal readonly SemanticModel _semanticModel;
    internal readonly INamedTypeSymbol _targetType;
    internal readonly AccessorPlan _accessorPlan;
    internal readonly AddedMethodCatalog _addedMethodCatalog;
    internal readonly AddedFieldCatalog _addedFieldCatalog;
    internal readonly AddedFieldShimRewrite AddedFields;
    internal readonly HarmonyAccessorShimRewrite HarmonyAccessors;

    public ShimBodyRewriter(
        SemanticModel semanticModel,
        INamedTypeSymbol targetType,
        AccessorPlan accessorPlan,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        _semanticModel = semanticModel;
        _targetType = targetType;
        _accessorPlan = accessorPlan;
        _addedMethodCatalog = addedMethodCatalog ?? new AddedMethodCatalog();
        _addedFieldCatalog = addedFieldCatalog ?? new AddedFieldCatalog();
        AddedFields = new AddedFieldShimRewrite(this);
        HarmonyAccessors = new HarmonyAccessorShimRewrite(this);
    }

    public override SyntaxNode VisitThisExpression(ThisExpressionSyntax node)
    {
        return SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName)
            .WithTriviaFrom(node);
    }

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        return VisitName(node, node);
    }

    public override SyntaxNode VisitGenericName(GenericNameSyntax node)
    {
        return VisitName(node, node);
    }

    public override SyntaxNode VisitInterpolation(InterpolationSyntax node)
    {
        InterpolationSyntax visited = (InterpolationSyntax)base.VisitInterpolation(node);

        // Why: a top-level ':' in an interpolation hole starts a format clause, so a
        // rewrite that inserts bare `global::` yields CS0103 ('global'). Parenthesizing
        // keeps the alias qualifier out of the format-clause scan and still coexists
        // with alignment/format clauses. Nested positions do not need parentheses, but
        // wrapping whenever an AliasQualifiedNameSyntax is present is always safe.
        // Alignment widths are integer expressions and hit the same ':' scan, so they
        // need the same wrapping; format clauses are literal text and need none.
        ExpressionSyntax parenthesizedExpression = ParenthesizeIfAliasQualified(visited.Expression);
        if (!ReferenceEquals(parenthesizedExpression, visited.Expression))
        {
            visited = visited.WithExpression(parenthesizedExpression);
        }

        InterpolationAlignmentClauseSyntax alignmentClause = visited.AlignmentClause;
        if (alignmentClause != null)
        {
            ExpressionSyntax parenthesizedAlignment = ParenthesizeIfAliasQualified(alignmentClause.Value);
            if (!ReferenceEquals(parenthesizedAlignment, alignmentClause.Value))
            {
                visited = visited.WithAlignmentClause(
                    alignmentClause.WithValue(parenthesizedAlignment));
            }
        }

        return visited;
    }

    private static ExpressionSyntax ParenthesizeIfAliasQualified(ExpressionSyntax expression)
    {
        if (expression is ParenthesizedExpressionSyntax)
        {
            return expression;
        }

        foreach (SyntaxNode descendant in expression.DescendantNodesAndSelf())
        {
            if (descendant is AliasQualifiedNameSyntax)
            {
                return SyntaxFactory.ParenthesizedExpression(expression);
            }
        }

        return expression;
    }

    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // Why first: added-member rewrite must not depend on _accessorPlan. Transplant bodies
        // have a null plan and would skip rewrite; delegation bodies would otherwise bind
        // Harmony accessors onto members that do not exist on the compiled type (B1).
        if (NameofRules.IsNameofInvocation(node))
        {
            ExpressionSyntax folded = TryFoldNameofAddedMember(node);
            if (folded != null)
            {
                return folded;
            }

            return base.VisitInvocationExpression(node);
        }

        if (NameofRules.IsInsideNameofArgument(node))
        {
            return base.VisitInvocationExpression(node);
        }

        ISymbol invokedSymbol = _semanticModel.GetSymbolInfo(node).Symbol;
        if (AddedCallSiteGuard.IsConditionalAccessReceiverSpine(node))
        {
            // Why not rewrite the spine invocation: ExtractReceiver cannot recover a
            // MemberBinding/ElementBinding receiver and would emit a parse-invalid shim.
            // Arguments and lambdas are not on the spine; base.Visit still rewrites those.
            return base.VisitInvocationExpression(node);
        }

        if (invokedSymbol is IMethodSymbol addedMethod
            && addedMethod.MethodKind == MethodKind.Ordinary)
        {
            AddedMethodBinding binding = _addedMethodCatalog.FindOrNull(BuildAddedMethodKey(addedMethod));
            if (binding != null)
            {
                return RewriteAddedMethodInvocation(node, addedMethod, binding);
            }
        }

        if (_accessorPlan == null)
        {
            return base.VisitInvocationExpression(node);
        }

        ISymbol symbol = invokedSymbol;
        if (symbol is not IMethodSymbol methodSymbol
            || methodSymbol.MethodKind != MethodKind.Ordinary
            || !AccessibilityRules.IsInaccessibleFromExternalAssembly(methodSymbol)
            || methodSymbol.IsExtensionMethod)
        {
            return base.VisitInvocationExpression(node);
        }

        AccessorEntry entry = _accessorPlan.GetOrAddMethod(methodSymbol);
        List<ArgumentSyntax> arguments = new List<ArgumentSyntax>();
        if (!methodSymbol.IsStatic)
        {
            ExpressionSyntax receiver = ExtractReceiver(node.Expression);
            arguments.Add(SyntaxFactory.Argument(VisitReceiver(receiver)));
        }

        foreach (ArgumentSyntax argument in node.ArgumentList.Arguments)
        {
            arguments.Add((ArgumentSyntax)Visit(argument));
        }

        return SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(entry.DelegateFieldName),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)))
            .WithTriviaFrom(node);
    }

    private ExpressionSyntax TryFoldNameofAddedMember(InvocationExpressionSyntax nameofInvocation)
    {
        if (nameofInvocation.ArgumentList.Arguments.Count != 1)
        {
            return null;
        }

        ExpressionSyntax argument = nameofInvocation.ArgumentList.Arguments[0].Expression;
        ISymbol symbol = ResolveNameofArgumentSymbol(argument);
        if (symbol is IMethodSymbol methodSymbol
            && _addedMethodCatalog.Contains(BuildAddedMethodKey(methodSymbol)))
        {
            return SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(methodSymbol.Name))
                .WithTriviaFrom(nameofInvocation);
        }

        if (symbol is IFieldSymbol fieldSymbol
            && _addedFieldCatalog.FindOrNull(
                AddedFieldBodyScan.FormatAddedFieldKeyFromSymbol(fieldSymbol)) != null)
        {
            return SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(fieldSymbol.Name))
                .WithTriviaFrom(nameofInvocation);
        }

        return null;
    }

    private ISymbol ResolveNameofArgumentSymbol(ExpressionSyntax argument)
    {
        // Why CandidateSymbols: nameof(method) is a method group, so Symbol is often null
        // and the unique candidate is the added method we need to fold.
        SymbolInfo symbolInfo = _semanticModel.GetSymbolInfo(argument);
        if (symbolInfo.Symbol != null)
        {
            return symbolInfo.Symbol;
        }

        if (symbolInfo.CandidateSymbols.Length == 1)
        {
            return symbolInfo.CandidateSymbols[0];
        }

        return null;
    }

    private SyntaxNode RewriteAddedMethodInvocation(
        InvocationExpressionSyntax node,
        IMethodSymbol addedMethod,
        AddedMethodBinding binding)
    {
        string qualifiedShimType = string.IsNullOrEmpty(binding.NamespaceName)
            ? "global::" + binding.ShimTypeName
            : "global::" + binding.NamespaceName + "." + binding.ShimTypeName;
        ExpressionSyntax shimTypeExpression = SyntaxFactory.ParseTypeName(qualifiedShimType);
        ExpressionSyntax shimAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            shimTypeExpression,
            SyntaxFactory.IdentifierName(binding.ShimMethodName));

        List<ArgumentSyntax> arguments = new List<ArgumentSyntax>();
        if (!addedMethod.IsStatic)
        {
            ExpressionSyntax receiver = ExtractReceiver(node.Expression);
            arguments.Add(SyntaxFactory.Argument(VisitReceiver(receiver)));
        }

        foreach (ArgumentSyntax argument in node.ArgumentList.Arguments)
        {
            arguments.Add((ArgumentSyntax)Visit(argument));
        }

        return SyntaxFactory.InvocationExpression(
                shimAccess,
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)))
            .WithTriviaFrom(node);
    }

    private static string BuildAddedMethodKey(IMethodSymbol methodSymbol)
    {
        string[] parameterTypeFullNames = methodSymbol.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        return methodSymbol.ContainingType == null
            ? methodSymbol.Name
            : CecilTypeNames.ToMetadataName(methodSymbol.ContainingType)
                + "::" + methodSymbol.Name + "("
                + string.Join(",", parameterTypeFullNames) + ")";
    }

    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        // Object/collection initializer member names must stay bare identifiers.
        if (node.Parent is InitializerExpressionSyntax)
        {
            return base.VisitAssignmentExpression(node);
        }

        if (NameofRules.IsInsideNameofArgument(node))
        {
            return base.VisitAssignmentExpression(node);
        }

        AddedFieldBinding assignedField = AddedFields.FindStoreBinding(_semanticModel.GetSymbolInfo(node.Left).Symbol);
        if (assignedField != null)
        {
            return AddedFields.RewriteAddedFieldAssignment(node, assignedField);
        }

        if (_accessorPlan == null)
        {
            return base.VisitAssignmentExpression(node);
        }

        ISymbol leftSymbol = _semanticModel.GetSymbolInfo(node.Left).Symbol;
        if (leftSymbol is IPropertySymbol propertySymbol
            && !propertySymbol.IsIndexer
            && !propertySymbol.IsStatic
            && AccessibilityRules.IsInaccessibleAccessor(propertySymbol.SetMethod))
        {
            return HarmonyAccessors.RewritePropertyAssignment(node, propertySymbol);
        }

        if (leftSymbol is IFieldSymbol fieldSymbol
            && !fieldSymbol.IsConst
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(fieldSymbol))
        {
            AccessorEntry entry = _accessorPlan.GetOrAddField(fieldSymbol);
            ExpressionSyntax fieldRefCall = HarmonyAccessors.CreateFieldRefInvocation(
                entry,
                VisitReceiver(ExtractReceiver(node.Left)));
            return node
                .WithLeft(fieldRefCall)
                .WithRight((ExpressionSyntax)Visit(node.Right))
                .WithTriviaFrom(node);
        }

        return base.VisitAssignmentExpression(node);
    }

    public override SyntaxNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        if (AddedFieldBodyScan.IsIncrementOrDecrement(node.Kind()))
        {
            AddedFieldBinding binding = AddedFields.FindStoreBinding(_semanticModel.GetSymbolInfo(node.Operand).Symbol);
            if (binding != null)
            {
                return AddedFields.RewriteAddedFieldIncrement(node.Operand, binding, node);
            }
        }

        return base.VisitPrefixUnaryExpression(node);
    }

    public override SyntaxNode VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        if (AddedFieldBodyScan.IsIncrementOrDecrement(node.Kind()))
        {
            AddedFieldBinding binding = AddedFields.FindStoreBinding(_semanticModel.GetSymbolInfo(node.Operand).Symbol);
            if (binding != null)
            {
                return AddedFields.RewriteAddedFieldIncrement(node.Operand, binding, node);
            }
        }

        return base.VisitPostfixUnaryExpression(node);
    }

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (NameofRules.IsInsideNameofArgument(node))
        {
            return base.VisitMemberAccessExpression(node);
        }

        ISymbol symbol = _semanticModel.GetSymbolInfo(node).Symbol
            ?? _semanticModel.GetSymbolInfo(node.Name).Symbol;
        if (!AddedFields.IsAssignmentLeft(node) && !AddedFields.IsIncrementOperand(node))
        {
            SyntaxNode addedFieldRead = AddedFields.TryRewriteAddedFieldRead(symbol, node.Expression, node);
            if (addedFieldRead != null)
            {
                return addedFieldRead;
            }
        }

        if (_accessorPlan == null)
        {
            return base.VisitMemberAccessExpression(node);
        }

        // Method-group invocation targets stay with VisitInvocationExpression; field/property
        // delegate invokes (`this._cb()`) must rewrite here so the call becomes `__F__(recv)()`.
        if (node.Parent is InvocationExpressionSyntax invocation
            && invocation.Expression == node
            && symbol is IMethodSymbol)
        {
            return base.VisitMemberAccessExpression(node);
        }

        if (node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node)
        {
            return base.VisitMemberAccessExpression(node);
        }

        ExpressionSyntax rewritten = HarmonyAccessors.TryRewriteInaccessibleRead(symbol, node.Expression, node);
        if (rewritten != null)
        {
            return rewritten;
        }

        return base.VisitMemberAccessExpression(node);
    }

    private SyntaxNode VisitName(SimpleNameSyntax node, SyntaxNode original)
    {
        if (HarmonyAccessors.IsNameSideOfLargerExpression(node))
        {
            return original;
        }

        ISymbol symbol = _semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol == null)
        {
            return original;
        }

        SyntaxNode addedFieldRead = HarmonyAccessors.TryRewriteUnownedAddedFieldRead(node, symbol);
        if (addedFieldRead != null)
        {
            return addedFieldRead;
        }

        // Local/anonymous functions are emitted into the shim assembly — keep bare calls.
        if (HarmonyAccessors.IsLocalOrAnonymousFunctionSymbol(symbol))
        {
            return original;
        }

        SyntaxNode accessorRead = HarmonyAccessors.TryRewriteNameAsAccessorRead(node, symbol);
        if (accessorRead != null)
        {
            return accessorRead;
        }

        (bool owned, bool isStatic, INamedTypeSymbol containingType) ownership = HarmonyAccessors.ResolveOwnedMember(symbol);
        if (!ownership.owned)
        {
            return original;
        }

        return HarmonyAccessors.QualifyOwnedMemberAccess(node, ownership.isStatic, ownership.containingType);
    }

    internal static SyntaxKind GetCompoundAssignmentBinaryKind(SyntaxKind assignmentKind)
    {
        return assignmentKind switch
        {
            SyntaxKind.AddAssignmentExpression => SyntaxKind.AddExpression,
            SyntaxKind.SubtractAssignmentExpression => SyntaxKind.SubtractExpression,
            SyntaxKind.MultiplyAssignmentExpression => SyntaxKind.MultiplyExpression,
            SyntaxKind.DivideAssignmentExpression => SyntaxKind.DivideExpression,
            SyntaxKind.ModuloAssignmentExpression => SyntaxKind.ModuloExpression,
            SyntaxKind.AndAssignmentExpression => SyntaxKind.BitwiseAndExpression,
            SyntaxKind.ExclusiveOrAssignmentExpression => SyntaxKind.ExclusiveOrExpression,
            SyntaxKind.OrAssignmentExpression => SyntaxKind.BitwiseOrExpression,
            SyntaxKind.LeftShiftAssignmentExpression => SyntaxKind.LeftShiftExpression,
            SyntaxKind.RightShiftAssignmentExpression => SyntaxKind.RightShiftExpression,
            SyntaxKind.UnsignedRightShiftAssignmentExpression => SyntaxKind.UnsignedRightShiftExpression,
            // Eligibility must reject unsupported compounds (including ??=) before rewrite.
            _ => throw new System.InvalidOperationException(
                "Unsupported compound assignment kind reached property rewrite: " + assignmentKind)
        };
    }

    internal ExpressionSyntax ExtractReceiver(ExpressionSyntax expression)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Expression;
        }

        return SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName);
    }

    // Why not Visit synthetic nodes: GetSymbolInfo requires nodes from the original SemanticModel
    // tree. Bare-member rewrite invents IdentifierName(InstanceParameterName), which must not be re-visited.
    internal ExpressionSyntax VisitReceiver(ExpressionSyntax receiver)
    {
        if (receiver.SyntaxTree != _semanticModel.SyntaxTree)
        {
            return receiver;
        }

        return (ExpressionSyntax)Visit(receiver);
    }
}
