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
    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol _targetType;
    private readonly AccessorPlan _accessorPlan;
    private readonly AddedMethodCatalog _addedMethodCatalog;
    private readonly AddedFieldCatalog _addedFieldCatalog;

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
                TransformWorkerProgram.FormatAddedFieldKeyFromSymbol(fieldSymbol)) != null)
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

        AddedFieldBinding assignedField = FindStoreBinding(_semanticModel.GetSymbolInfo(node.Left).Symbol);
        if (assignedField != null)
        {
            return RewriteAddedFieldAssignment(node, assignedField);
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
            return RewritePropertyAssignment(node, propertySymbol);
        }

        if (leftSymbol is IFieldSymbol fieldSymbol
            && !fieldSymbol.IsConst
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(fieldSymbol))
        {
            AccessorEntry entry = _accessorPlan.GetOrAddField(fieldSymbol);
            ExpressionSyntax fieldRefCall = CreateFieldRefInvocation(
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
        if (TransformWorkerProgram.IsIncrementOrDecrement(node.Kind()))
        {
            AddedFieldBinding binding = FindStoreBinding(_semanticModel.GetSymbolInfo(node.Operand).Symbol);
            if (binding != null)
            {
                return RewriteAddedFieldIncrement(node.Operand, binding, node);
            }
        }

        return base.VisitPrefixUnaryExpression(node);
    }

    public override SyntaxNode VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        if (TransformWorkerProgram.IsIncrementOrDecrement(node.Kind()))
        {
            AddedFieldBinding binding = FindStoreBinding(_semanticModel.GetSymbolInfo(node.Operand).Symbol);
            if (binding != null)
            {
                return RewriteAddedFieldIncrement(node.Operand, binding, node);
            }
        }

        return base.VisitPostfixUnaryExpression(node);
    }

    private AddedFieldBinding FindStoreBinding(ISymbol symbol)
    {
        if (symbol is not IFieldSymbol fieldSymbol)
        {
            return null;
        }

        AddedFieldBinding binding = _addedFieldCatalog.FindOrNull(
            TransformWorkerProgram.FormatAddedFieldKeyFromSymbol(fieldSymbol));
        if (binding == null || !binding.IsStoreRewriteable)
        {
            return null;
        }

        return binding;
    }

    private AddedFieldBinding FindAnyAddedBinding(ISymbol symbol)
    {
        if (symbol is not IFieldSymbol fieldSymbol)
        {
            return null;
        }

        return _addedFieldCatalog.FindOrNull(
            TransformWorkerProgram.FormatAddedFieldKeyFromSymbol(fieldSymbol));
    }

    private SyntaxNode TryRewriteAddedFieldRead(
        ISymbol symbol,
        ExpressionSyntax receiverSyntax,
        SyntaxNode triviaSource)
    {
        AddedFieldBinding binding = FindAnyAddedBinding(symbol);
        if (binding == null || binding.UnavailableReason != null)
        {
            return null;
        }

        if (binding.IsConst)
        {
            ExpressionSyntax literal = TransformWorkerProgram.TryCreateConstantLiteral(
                binding.ConstantValue,
                binding.FieldType);
            if (literal == null)
            {
                return null;
            }

            _addedFieldCatalog.MarkConstFold(binding.FieldKey);
            return literal.WithTriviaFrom(triviaSource);
        }

        if (!binding.IsStoreRewriteable)
        {
            return null;
        }

        return CreateAddedFieldGetOrInit(binding, receiverSyntax).WithTriviaFrom(triviaSource);
    }

    private SyntaxNode RewriteAddedFieldAssignment(
        AssignmentExpressionSyntax node,
        AddedFieldBinding binding)
    {
        ExpressionSyntax receiver = ExtractAddedFieldReceiver(node.Left, binding.IsStatic);
        ExpressionSyntax visitedRight = (ExpressionSyntax)Visit(node.Right);
        if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return CreateAddedFieldSet(binding, receiver, visitedRight).WithTriviaFrom(node);
        }

        SyntaxKind binaryKind = GetCompoundAssignmentBinaryKind(node.Kind());
        ExpressionSyntax getCall = CreateAddedFieldGetOrInit(binding, receiver);
        ExpressionSyntax combined = SyntaxFactory.BinaryExpression(binaryKind, getCall, visitedRight);
        return CreateAddedFieldSet(
                binding,
                receiver,
                CastToAddedFieldType(combined, binding.FieldType))
            .WithTriviaFrom(node);
    }

    private SyntaxNode RewriteAddedFieldIncrement(
        ExpressionSyntax operand,
        AddedFieldBinding binding,
        SyntaxNode triviaSource)
    {
        ExpressionSyntax receiver = ExtractAddedFieldReceiver(operand, binding.IsStatic);
        ExpressionSyntax getCall = CreateAddedFieldGetOrInit(binding, receiver);
        SyntaxKind binaryKind = IsDecrementNode(triviaSource)
            ? SyntaxKind.SubtractExpression
            : SyntaxKind.AddExpression;
        ExpressionSyntax combined = SyntaxFactory.BinaryExpression(
            binaryKind,
            getCall,
            SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(1)));
        return CreateAddedFieldSet(
                binding,
                receiver,
                CastToAddedFieldType(combined, binding.FieldType))
            .WithTriviaFrom(triviaSource);
    }

    private static bool IsDecrementNode(SyntaxNode node)
    {
        if (node is PrefixUnaryExpressionSyntax prefix)
        {
            return prefix.IsKind(SyntaxKind.PreDecrementExpression);
        }

        return node is PostfixUnaryExpressionSyntax postfix
            && postfix.IsKind(SyntaxKind.PostDecrementExpression);
    }

    // Why cast: C# compound assignment and ++/-- apply a conversion back to the field type
    // (byte += 1 is (byte)(byte + 1)). Emitting the binary without that conversion is CS1503.
    private static ExpressionSyntax CastToAddedFieldType(ExpressionSyntax expression, ITypeSymbol fieldType)
    {
        TypeSyntax typeSyntax = SyntaxFactory.ParseTypeName(
            fieldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        return SyntaxFactory.CastExpression(
            typeSyntax,
            SyntaxFactory.ParenthesizedExpression(expression));
    }

    private ExpressionSyntax ExtractAddedFieldReceiver(ExpressionSyntax expression, bool isStatic)
    {
        if (isStatic)
        {
            return null;
        }

        ExpressionSyntax receiver = ExtractReceiver(expression);
        if (receiver is ThisExpressionSyntax || receiver is BaseExpressionSyntax)
        {
            return SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName);
        }

        return VisitReceiver(receiver);
    }

    private InvocationExpressionSyntax CreateAddedFieldGetOrInit(
        AddedFieldBinding binding,
        ExpressionSyntax receiver)
    {
        _addedFieldCatalog.MarkStoreRewrite(binding.FieldKey);
        string methodName = binding.IsStatic
            ? TransformWorkerProgramMarker.AddedFieldGetOrInitStaticMethodName
            : TransformWorkerProgramMarker.AddedFieldGetOrInitMethodName;
        List<ArgumentSyntax> arguments = new List<ArgumentSyntax>();
        if (!binding.IsStatic)
        {
            arguments.Add(SyntaxFactory.Argument(receiver));
        }

        arguments.Add(
            SyntaxFactory.Argument(
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(binding.FieldKey))));
        arguments.Add(SyntaxFactory.Argument(CreateAddedFieldInitializer(binding)));
        return CreateAddedFieldStoreInvocation(methodName, binding.FieldType, arguments);
    }

    private InvocationExpressionSyntax CreateAddedFieldSet(
        AddedFieldBinding binding,
        ExpressionSyntax receiver,
        ExpressionSyntax value)
    {
        _addedFieldCatalog.MarkStoreRewrite(binding.FieldKey);
        string methodName = binding.IsStatic
            ? TransformWorkerProgramMarker.AddedFieldSetStaticMethodName
            : TransformWorkerProgramMarker.AddedFieldSetMethodName;
        List<ArgumentSyntax> arguments = new List<ArgumentSyntax>();
        if (!binding.IsStatic)
        {
            arguments.Add(SyntaxFactory.Argument(receiver));
        }

        arguments.Add(
            SyntaxFactory.Argument(
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(binding.FieldKey))));
        arguments.Add(SyntaxFactory.Argument(value));
        return CreateAddedFieldStoreInvocation(methodName, binding.FieldType, arguments);
    }

    private static ExpressionSyntax CreateAddedFieldInitializer(AddedFieldBinding binding)
    {
        if (binding.Initializer == null)
        {
            return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
        }

        ExpressionSyntax cloned = SyntaxFactory.ParseExpression(binding.Initializer.ToString());
        return SyntaxFactory.ParenthesizedLambdaExpression(
                SyntaxFactory.ParameterList(),
                cloned)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword)));
    }

    private static InvocationExpressionSyntax CreateAddedFieldStoreInvocation(
        string methodName,
        ITypeSymbol fieldType,
        List<ArgumentSyntax> arguments)
    {
        TypeSyntax storeType = SyntaxFactory.ParseTypeName(
            TransformWorkerProgramMarker.AddedFieldStoreTypeName);
        TypeSyntax typeArgument = SyntaxFactory.ParseTypeName(
            fieldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        GenericNameSyntax genericName = SyntaxFactory.GenericName(SyntaxFactory.Identifier(methodName))
            .WithTypeArgumentList(
                SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(typeArgument)));
        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                storeType,
                genericName),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)));
    }

    private static bool IsAssignmentLeft(SyntaxNode node)
    {
        return node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node;
    }

    private static bool IsIncrementOperand(SyntaxNode node)
    {
        if (node.Parent is PrefixUnaryExpressionSyntax prefix
            && TransformWorkerProgram.IsIncrementOrDecrement(prefix.Kind()))
        {
            return true;
        }

        return node.Parent is PostfixUnaryExpressionSyntax postfix
            && TransformWorkerProgram.IsIncrementOrDecrement(postfix.Kind());
    }

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (NameofRules.IsInsideNameofArgument(node))
        {
            return base.VisitMemberAccessExpression(node);
        }

        ISymbol symbol = _semanticModel.GetSymbolInfo(node).Symbol
            ?? _semanticModel.GetSymbolInfo(node.Name).Symbol;
        if (!IsAssignmentLeft(node) && !IsIncrementOperand(node))
        {
            SyntaxNode addedFieldRead = TryRewriteAddedFieldRead(symbol, node.Expression, node);
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

        ExpressionSyntax rewritten = TryRewriteInaccessibleRead(symbol, node.Expression, node);
        if (rewritten != null)
        {
            return rewritten;
        }

        return base.VisitMemberAccessExpression(node);
    }

    private SyntaxNode VisitName(SimpleNameSyntax node, SyntaxNode original)
    {
        if (IsNameSideOfLargerExpression(node))
        {
            return original;
        }

        ISymbol symbol = _semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol == null)
        {
            return original;
        }

        SyntaxNode addedFieldRead = TryRewriteUnownedAddedFieldRead(node, symbol);
        if (addedFieldRead != null)
        {
            return addedFieldRead;
        }

        // Local/anonymous functions are emitted into the shim assembly — keep bare calls.
        if (IsLocalOrAnonymousFunctionSymbol(symbol))
        {
            return original;
        }

        SyntaxNode accessorRead = TryRewriteNameAsAccessorRead(node, symbol);
        if (accessorRead != null)
        {
            return accessorRead;
        }

        (bool owned, bool isStatic, INamedTypeSymbol containingType) ownership = ResolveOwnedMember(symbol);
        if (!ownership.owned)
        {
            return original;
        }

        return QualifyOwnedMemberAccess(node, ownership.isStatic, ownership.containingType);
    }

    private static bool IsNameSideOfLargerExpression(SimpleNameSyntax node)
    {
        return IsMemberAccessNameSide(node)
            || IsQualifiedNameRightSide(node)
            || IsMemberBindingName(node)
            || IsObjectOrCollectionInitializerMemberName(node);
    }

    private SyntaxNode TryRewriteUnownedAddedFieldRead(SimpleNameSyntax node, ISymbol symbol)
    {
        if (IsAssignmentLeft(node)
            || IsIncrementOperand(node)
            || NameofRules.IsInsideNameofArgument(node))
        {
            return null;
        }

        return TryRewriteAddedFieldRead(
            symbol,
            SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName),
            node);
    }

    private static bool IsLocalOrAnonymousFunctionSymbol(ISymbol symbol)
    {
        return symbol is IMethodSymbol methodSymbol
            && (methodSymbol.MethodKind == MethodKind.LocalFunction
                || methodSymbol.MethodKind == MethodKind.AnonymousFunction);
    }

    private SyntaxNode TryRewriteNameAsAccessorRead(SimpleNameSyntax node, ISymbol symbol)
    {
        // nameof(...) and assignment left sides must keep a member-reference shape: qualify only,
        // never rewrite to an accessor read (Func<> call results are not assignable).
        bool suppressAccessorRead = NameofRules.IsInsideNameofArgument(node)
            || (node.Parent is AssignmentExpressionSyntax assignmentLeft
                && assignmentLeft.Left == node);
        if (_accessorPlan == null || suppressAccessorRead)
        {
            return null;
        }

        return TryRewriteInaccessibleRead(
            symbol,
            SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName),
            node);
    }

    private static SyntaxNode QualifyOwnedMemberAccess(
        SimpleNameSyntax node,
        bool isStatic,
        INamedTypeSymbol containingType)
    {
        if (isStatic)
        {
            TypeSyntax typeSyntax = SyntaxFactory.ParseTypeName(
                containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    typeSyntax,
                    (SimpleNameSyntax)node.WithoutTrivia())
                .WithTriviaFrom(node);
        }

        return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName),
                (SimpleNameSyntax)node.WithoutTrivia())
            .WithTriviaFrom(node);
    }

    private ExpressionSyntax TryRewriteInaccessibleRead(
        ISymbol symbol,
        ExpressionSyntax receiverSyntax,
        SyntaxNode triviaSource)
    {
        if (symbol is IFieldSymbol fieldSymbol
            && !fieldSymbol.IsConst
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(fieldSymbol))
        {
            AccessorEntry entry = _accessorPlan.GetOrAddField(fieldSymbol);
            return CreateFieldRefInvocation(entry, VisitReceiver(receiverSyntax))
                .WithTriviaFrom(triviaSource);
        }

        if (symbol is IPropertySymbol propertySymbol
            && !propertySymbol.IsIndexer
            && !propertySymbol.IsStatic
            && AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod))
        {
            AccessorEntry entry = _accessorPlan.GetOrAddPropertyGetter(propertySymbol);
            return CreateDelegateInvocation(
                    entry.DelegateFieldName,
                    new[] { VisitReceiver(receiverSyntax) })
                .WithTriviaFrom(triviaSource);
        }

        return null;
    }

    private SyntaxNode RewritePropertyAssignment(
        AssignmentExpressionSyntax node,
        IPropertySymbol propertySymbol)
    {
        ExpressionSyntax receiver = ExtractReceiver(node.Left);
        ExpressionSyntax visitedReceiver = VisitReceiver(receiver);
        ExpressionSyntax visitedRight = (ExpressionSyntax)Visit(node.Right);
        AccessorEntry setter = _accessorPlan.GetOrAddPropertySetter(propertySymbol);

        if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return CreateDelegateInvocation(
                    setter.DelegateFieldName,
                    new[] { visitedReceiver, visitedRight })
                .WithTriviaFrom(node);
        }

        AccessorEntry getter = _accessorPlan.GetOrAddPropertyGetter(propertySymbol);
        ExpressionSyntax getCall = CreateDelegateInvocation(
            getter.DelegateFieldName,
            new[] { visitedReceiver });
        SyntaxKind binaryKind = GetCompoundAssignmentBinaryKind(node.Kind());
        ExpressionSyntax combined = SyntaxFactory.BinaryExpression(binaryKind, getCall, visitedRight);
        return CreateDelegateInvocation(
                setter.DelegateFieldName,
                new[] { visitedReceiver, combined })
            .WithTriviaFrom(node);
    }

    private static SyntaxKind GetCompoundAssignmentBinaryKind(SyntaxKind assignmentKind)
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

    private static ExpressionSyntax CreateFieldRefInvocation(
        AccessorEntry entry,
        ExpressionSyntax visitedReceiver)
    {
        if (entry.FieldSymbol.IsStatic)
        {
            return CreateDelegateInvocation(entry.DelegateFieldName, Array.Empty<ExpressionSyntax>());
        }

        return CreateDelegateInvocation(entry.DelegateFieldName, new[] { visitedReceiver });
    }

    private static ExpressionSyntax CreateDelegateInvocation(
        string delegateFieldName,
        IReadOnlyList<ExpressionSyntax> arguments)
    {
        SeparatedSyntaxList<ArgumentSyntax> argumentList = SyntaxFactory.SeparatedList(
            arguments.Select(SyntaxFactory.Argument));
        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(delegateFieldName),
            SyntaxFactory.ArgumentList(argumentList));
    }

    private ExpressionSyntax ExtractReceiver(ExpressionSyntax expression)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Expression;
        }

        return SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName);
    }

    // Why not Visit synthetic nodes: GetSymbolInfo requires nodes from the original SemanticModel
    // tree. Bare-member rewrite invents IdentifierName(InstanceParameterName), which must not be re-visited.
    private ExpressionSyntax VisitReceiver(ExpressionSyntax receiver)
    {
        if (receiver.SyntaxTree != _semanticModel.SyntaxTree)
        {
            return receiver;
        }

        return (ExpressionSyntax)Visit(receiver);
    }

    private (bool owned, bool isStatic, INamedTypeSymbol containingType) ResolveOwnedMember(ISymbol symbol)
    {
        INamedTypeSymbol containingType;
        bool isStatic;

        if (symbol is IMethodSymbol methodSymbol)
        {
            if (methodSymbol.IsExtensionMethod)
            {
                return (false, false, null);
            }

            containingType = methodSymbol.ContainingType;
            isStatic = methodSymbol.IsStatic;
        }
        else if (symbol is IFieldSymbol fieldSymbol)
        {
            containingType = fieldSymbol.ContainingType;
            isStatic = fieldSymbol.IsStatic;
        }
        else if (symbol is IPropertySymbol propertySymbol)
        {
            containingType = propertySymbol.ContainingType;
            isStatic = propertySymbol.IsStatic;
        }
        else if (symbol is IEventSymbol eventSymbol)
        {
            containingType = eventSymbol.ContainingType;
            isStatic = eventSymbol.IsStatic;
        }
        else
        {
            return (false, false, null);
        }

        if (containingType == null)
        {
            return (false, false, null);
        }

        if (!IsInInheritanceHierarchy(_targetType, containingType))
        {
            return (false, false, null);
        }

        return (true, isStatic, containingType);
    }

    private static bool IsInInheritanceHierarchy(INamedTypeSymbol derived, INamedTypeSymbol candidate)
    {
        for (INamedTypeSymbol current = derived; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMemberAccessNameSide(SimpleNameSyntax node)
    {
        return node.Parent is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name == node;
    }

    private static bool IsQualifiedNameRightSide(SimpleNameSyntax node)
    {
        return node.Parent is QualifiedNameSyntax qualifiedName
            && qualifiedName.Right == node;
    }

    private static bool IsMemberBindingName(SimpleNameSyntax node)
    {
        return node.Parent is MemberBindingExpressionSyntax memberBinding
            && memberBinding.Name == node;
    }

    // `new T { _field = 1 }` must keep the bare member name; qualifying to instance._field is
    // invalid inside an object/collection initializer.
    private static bool IsObjectOrCollectionInitializerMemberName(SimpleNameSyntax node)
    {
        if (node.Parent is not AssignmentExpressionSyntax assignment || assignment.Left != node)
        {
            return false;
        }

        return assignment.Parent is InitializerExpressionSyntax;
    }
}
