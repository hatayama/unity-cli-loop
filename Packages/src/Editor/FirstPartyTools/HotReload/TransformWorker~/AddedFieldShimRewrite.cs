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

internal sealed class AddedFieldShimRewrite
{
    private readonly ShimBodyRewriter _rewriter;

    internal AddedFieldShimRewrite(ShimBodyRewriter rewriter)
    {
        _rewriter = rewriter;
    }

    internal AddedFieldBinding FindStoreBinding(ISymbol symbol)
    {
        if (symbol is not IFieldSymbol fieldSymbol)
        {
            return null;
        }

        AddedFieldBinding binding = _rewriter._addedFieldCatalog.FindOrNull(
            AddedFieldBodyScan.FormatAddedFieldKeyFromSymbol(fieldSymbol));
        if (binding == null || !binding.IsStoreRewriteable)
        {
            return null;
        }

        return binding;
    }

    internal AddedFieldBinding FindAnyAddedBinding(ISymbol symbol)
    {
        if (symbol is not IFieldSymbol fieldSymbol)
        {
            return null;
        }

        return _rewriter._addedFieldCatalog.FindOrNull(
            AddedFieldBodyScan.FormatAddedFieldKeyFromSymbol(fieldSymbol));
    }

    internal SyntaxNode TryRewriteAddedFieldRead(
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
            ExpressionSyntax literal = ConstantLiteralFactory.TryCreateConstantLiteral(
                binding.ConstantValue,
                binding.FieldType);
            if (literal == null)
            {
                return null;
            }

            _rewriter._addedFieldCatalog.MarkConstFold(binding.FieldKey);
            return literal.WithTriviaFrom(triviaSource);
        }

        if (!binding.IsStoreRewriteable)
        {
            return null;
        }

        ExpressionSyntax rewrittenReceiver = receiverSyntax;
        if (!binding.IsStatic)
        {
            // Why: CSharpSyntaxRewriter does not re-visit children of a replacement node, so a
            // raw ThisExpression left in the store call would survive as `this` in the static shim.
            rewrittenReceiver = _rewriter.VisitReceiver(receiverSyntax);
        }

        return CreateAddedFieldGetOrInit(binding, rewrittenReceiver).WithTriviaFrom(triviaSource);
    }

    internal SyntaxNode RewriteAddedFieldAssignment(
        AssignmentExpressionSyntax node,
        AddedFieldBinding binding)
    {
        ExpressionSyntax receiver = ExtractAddedFieldReceiver(node.Left, binding.IsStatic);
        ExpressionSyntax visitedRight = (ExpressionSyntax)_rewriter.Visit(node.Right);
        if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return CreateAddedFieldSet(binding, receiver, visitedRight).WithTriviaFrom(node);
        }

        SyntaxKind binaryKind = ShimBodyRewriter.GetCompoundAssignmentBinaryKind(node.Kind());
        ExpressionSyntax getCall = CreateAddedFieldGetOrInit(binding, receiver);
        ExpressionSyntax combined = SyntaxFactory.BinaryExpression(binaryKind, getCall, visitedRight);
        return CreateAddedFieldSet(
                binding,
                receiver,
                CastToAddedFieldType(combined, binding.FieldType))
            .WithTriviaFrom(node);
    }

    internal SyntaxNode RewriteAddedFieldIncrement(
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

    internal static bool IsDecrementNode(SyntaxNode node)
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
    internal static ExpressionSyntax CastToAddedFieldType(ExpressionSyntax expression, ITypeSymbol fieldType)
    {
        TypeSyntax typeSyntax = SyntaxFactory.ParseTypeName(
            fieldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        return SyntaxFactory.CastExpression(
            typeSyntax,
            SyntaxFactory.ParenthesizedExpression(expression));
    }

    internal ExpressionSyntax ExtractAddedFieldReceiver(ExpressionSyntax expression, bool isStatic)
    {
        if (isStatic)
        {
            return null;
        }

        ExpressionSyntax receiver = _rewriter.ExtractReceiver(expression);
        if (receiver is ThisExpressionSyntax || receiver is BaseExpressionSyntax)
        {
            return SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName);
        }

        return _rewriter.VisitReceiver(receiver);
    }

    internal InvocationExpressionSyntax CreateAddedFieldGetOrInit(
        AddedFieldBinding binding,
        ExpressionSyntax receiver)
    {
        _rewriter._addedFieldCatalog.MarkStoreRewrite(binding.FieldKey);
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

    internal InvocationExpressionSyntax CreateAddedFieldSet(
        AddedFieldBinding binding,
        ExpressionSyntax receiver,
        ExpressionSyntax value)
    {
        _rewriter._addedFieldCatalog.MarkStoreRewrite(binding.FieldKey);
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

    internal static ExpressionSyntax CreateAddedFieldInitializer(AddedFieldBinding binding)
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

    internal static InvocationExpressionSyntax CreateAddedFieldStoreInvocation(
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

    internal static bool IsAssignmentLeft(SyntaxNode node)
    {
        return node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node;
    }

    internal static bool IsIncrementOperand(SyntaxNode node)
    {
        if (node.Parent is PrefixUnaryExpressionSyntax prefix
            && AddedFieldBodyScan.IsIncrementOrDecrement(prefix.Kind()))
        {
            return true;
        }

        return node.Parent is PostfixUnaryExpressionSyntax postfix
            && AddedFieldBodyScan.IsIncrementOrDecrement(postfix.Kind());
    }
}
