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
/// Rewrites reads and simple writes of added properties into accessor shim invocations.
/// </summary>
internal sealed class AddedPropertyShimRewrite
{
    private readonly ShimBodyRewriter _rewriter;

    internal AddedPropertyShimRewrite(ShimBodyRewriter rewriter)
    {
        _rewriter = rewriter;
    }

    internal SyntaxNode TryRewriteRead(
        ISymbol symbol,
        ExpressionSyntax receiverSyntax,
        string memberName,
        SyntaxNode triviaSource)
    {
        AddedPropertyBinding binding = ResolveBinding(symbol, receiverSyntax, memberName);
        if (binding == null || binding.UnavailableReason != null)
        {
            return null;
        }

        return CreateAccessorInvocation(binding, binding.Getter, receiverSyntax).WithTriviaFrom(triviaSource);
    }

    internal SyntaxNode TryRewriteBareRead(SimpleNameSyntax node, ISymbol symbol)
    {
        AddedPropertyBinding binding = ResolveBinding(symbol, null, null);
        if (binding == null || binding.UnavailableReason != null)
        {
            return null;
        }

        return CreateAccessorInvocation(
            binding,
            binding.Getter,
            SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName)).WithTriviaFrom(node);
    }

    internal SyntaxNode TryRewriteSimpleAssignment(AssignmentExpressionSyntax node)
    {
        if (!node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return null;
        }

        ISymbol symbol = _rewriter._semanticModel.GetSymbolInfo(node.Left).Symbol;
        ExpressionSyntax receiverSyntax = node.Left is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Expression
            : null;
        string memberName = node.Left is MemberAccessExpressionSyntax namedAccess
            ? namedAccess.Name.Identifier.ValueText
            : null;
        AddedPropertyBinding binding = ResolveBinding(symbol, receiverSyntax, memberName);
        if (binding == null || binding.Setter == null || binding.UnavailableReason != null)
        {
            return null;
        }

        List<ArgumentSyntax> arguments = CreateReceiverArguments(binding, receiverSyntax);
        arguments.Add(SyntaxFactory.Argument((ExpressionSyntax)_rewriter.Visit(node.Right)));
        return CreateAccessorInvocation(binding.Setter, arguments).WithTriviaFrom(node);
    }

    private AddedPropertyBinding ResolveBinding(
        ISymbol symbol,
        ExpressionSyntax receiverSyntax,
        string memberName)
    {
        if (symbol is IPropertySymbol propertySymbol)
        {
            return _rewriter._addedPropertyCatalog.FindBySymbolOrNull(propertySymbol);
        }

        if (receiverSyntax == null || memberName == null)
        {
            return null;
        }

        ITypeSymbol receiverType = _rewriter._semanticModel.GetTypeInfo(receiverSyntax).Type;
        if (receiverType is not INamedTypeSymbol namedType || namedType.TypeKind == TypeKind.Error)
        {
            return null;
        }

        return _rewriter._addedPropertyCatalog.FindOrNull(AddedPropertyCatalog.FormatPropertyKey(
            CecilTypeNames.ToMetadataName(namedType),
            memberName));
    }

    private InvocationExpressionSyntax CreateAccessorInvocation(
        AddedPropertyBinding binding,
        AddedMethodBinding accessor,
        ExpressionSyntax receiverSyntax)
    {
        List<ArgumentSyntax> arguments = CreateReceiverArguments(binding, receiverSyntax);
        return CreateAccessorInvocation(accessor, arguments);
    }

    private List<ArgumentSyntax> CreateReceiverArguments(
        AddedPropertyBinding binding,
        ExpressionSyntax receiverSyntax)
    {
        List<ArgumentSyntax> arguments = new List<ArgumentSyntax>();
        if (!binding.IsStatic)
        {
            ExpressionSyntax receiver = receiverSyntax
                ?? SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName);
            arguments.Add(SyntaxFactory.Argument(_rewriter.VisitReceiver(receiver)));
        }

        return arguments;
    }

    private static InvocationExpressionSyntax CreateAccessorInvocation(
        AddedMethodBinding accessor,
        List<ArgumentSyntax> arguments)
    {
        string qualifiedShimType = "global::"
            + ShimNamespaceNames.ResolveShimNamespaceName(accessor.NamespaceName)
            + "."
            + accessor.ShimTypeName;
        ExpressionSyntax member = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.ParseTypeName(qualifiedShimType),
            SyntaxFactory.IdentifierName(accessor.ShimMethodName));
        return SyntaxFactory.InvocationExpression(
            member,
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)));
    }
}
