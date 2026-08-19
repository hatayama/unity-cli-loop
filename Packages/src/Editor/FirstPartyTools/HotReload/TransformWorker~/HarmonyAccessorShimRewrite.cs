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

internal sealed class HarmonyAccessorShimRewrite
{
    private readonly ShimBodyRewriter _rewriter;

    internal HarmonyAccessorShimRewrite(ShimBodyRewriter rewriter)
    {
        _rewriter = rewriter;
    }

    internal SyntaxNode TryRewriteUnownedAddedFieldRead(SimpleNameSyntax node, ISymbol symbol)
    {
        if (AddedFieldShimRewrite.IsAssignmentLeft(node)
            || AddedFieldShimRewrite.IsIncrementOperand(node)
            || NameofRules.IsInsideNameofArgument(node))
        {
            return null;
        }

        return _rewriter.AddedFields.TryRewriteAddedFieldRead(
            symbol,
            SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName),
            node);
    }

    internal SyntaxNode TryRewriteNameAsAccessorRead(SimpleNameSyntax node, ISymbol symbol)
    {
        // nameof(...) and assignment left sides must keep a member-reference shape: qualify only,
        // never rewrite to an accessor read (Func<> call results are not assignable).
        bool suppressAccessorRead = NameofRules.IsInsideNameofArgument(node)
            || (node.Parent is AssignmentExpressionSyntax assignmentLeft
                && assignmentLeft.Left == node);
        if (_rewriter._accessorPlan == null || suppressAccessorRead)
        {
            return null;
        }

        return TryRewriteInaccessibleRead(
            symbol,
            SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName),
            node);
    }

    internal static SyntaxNode QualifyOwnedMemberAccess(
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

    internal ExpressionSyntax TryRewriteInaccessibleRead(
        ISymbol symbol,
        ExpressionSyntax receiverSyntax,
        SyntaxNode triviaSource)
    {
        if (symbol is IFieldSymbol fieldSymbol
            && !fieldSymbol.IsConst
            && AccessibilityRules.IsInaccessibleFromExternalAssembly(fieldSymbol))
        {
            AccessorEntry entry = _rewriter._accessorPlan.GetOrAddField(fieldSymbol);
            return CreateFieldRefInvocation(entry, _rewriter.VisitReceiver(receiverSyntax))
                .WithTriviaFrom(triviaSource);
        }

        if (symbol is IPropertySymbol propertySymbol
            && !propertySymbol.IsIndexer
            && !propertySymbol.IsStatic
            && AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod))
        {
            AccessorEntry entry = _rewriter._accessorPlan.GetOrAddPropertyGetter(propertySymbol);
            return CreateDelegateInvocation(
                    entry.DelegateFieldName,
                    new[] { _rewriter.VisitReceiver(receiverSyntax) })
                .WithTriviaFrom(triviaSource);
        }

        return null;
    }

    internal SyntaxNode RewritePropertyAssignment(
        AssignmentExpressionSyntax node,
        IPropertySymbol propertySymbol)
    {
        ExpressionSyntax receiver = _rewriter.ExtractReceiver(node.Left);
        ExpressionSyntax visitedReceiver = _rewriter.VisitReceiver(receiver);
        ExpressionSyntax visitedRight = (ExpressionSyntax)_rewriter.Visit(node.Right);
        AccessorEntry setter = _rewriter._accessorPlan.GetOrAddPropertySetter(propertySymbol);

        if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return CreateDelegateInvocation(
                    setter.DelegateFieldName,
                    new[] { visitedReceiver, visitedRight })
                .WithTriviaFrom(node);
        }

        AccessorEntry getter = _rewriter._accessorPlan.GetOrAddPropertyGetter(propertySymbol);
        ExpressionSyntax getCall = CreateDelegateInvocation(
            getter.DelegateFieldName,
            new[] { visitedReceiver });
        SyntaxKind binaryKind = ShimBodyRewriter.GetCompoundAssignmentBinaryKind(node.Kind());
        ExpressionSyntax combined = SyntaxFactory.BinaryExpression(binaryKind, getCall, visitedRight);
        return CreateDelegateInvocation(
                setter.DelegateFieldName,
                new[] { visitedReceiver, combined })
            .WithTriviaFrom(node);
    }

    internal static ExpressionSyntax CreateFieldRefInvocation(
        AccessorEntry entry,
        ExpressionSyntax visitedReceiver)
    {
        if (entry.FieldSymbol.IsStatic)
        {
            return CreateDelegateInvocation(entry.DelegateFieldName, Array.Empty<ExpressionSyntax>());
        }

        return CreateDelegateInvocation(entry.DelegateFieldName, new[] { visitedReceiver });
    }

    internal static ExpressionSyntax CreateDelegateInvocation(
        string delegateFieldName,
        IReadOnlyList<ExpressionSyntax> arguments)
    {
        SeparatedSyntaxList<ArgumentSyntax> argumentList = SyntaxFactory.SeparatedList(
            arguments.Select(SyntaxFactory.Argument));
        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(delegateFieldName),
            SyntaxFactory.ArgumentList(argumentList));
    }

    internal (bool owned, bool isStatic, INamedTypeSymbol containingType) ResolveOwnedMember(ISymbol symbol)
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

        if (!IsInInheritanceHierarchy(_rewriter._targetType, containingType))
        {
            return (false, false, null);
        }

        return (true, isStatic, containingType);
    }

    internal static bool IsInInheritanceHierarchy(INamedTypeSymbol derived, INamedTypeSymbol candidate)
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

    internal static bool IsNameSideOfLargerExpression(SimpleNameSyntax node)
    {
        return IsMemberAccessNameSide(node)
            || IsQualifiedNameRightSide(node)
            || IsMemberBindingName(node)
            || IsObjectOrCollectionInitializerMemberName(node);
    }

    internal static bool IsLocalOrAnonymousFunctionSymbol(ISymbol symbol)
    {
        return symbol is IMethodSymbol methodSymbol
            && (methodSymbol.MethodKind == MethodKind.LocalFunction
                || methodSymbol.MethodKind == MethodKind.AnonymousFunction);
    }

    internal static bool IsMemberAccessNameSide(SimpleNameSyntax node)
    {
        return node.Parent is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name == node;
    }

    internal static bool IsQualifiedNameRightSide(SimpleNameSyntax node)
    {
        return node.Parent is QualifiedNameSyntax qualifiedName
            && qualifiedName.Right == node;
    }

    internal static bool IsMemberBindingName(SimpleNameSyntax node)
    {
        return node.Parent is MemberBindingExpressionSyntax memberBinding
            && memberBinding.Name == node;
    }

    // `new T { _field = 1 }` must keep the bare member name; qualifying to instance._field is
    // invalid inside an object/collection initializer.
    internal static bool IsObjectOrCollectionInitializerMemberName(SimpleNameSyntax node)
    {
        if (node.Parent is not AssignmentExpressionSyntax assignment || assignment.Left != node)
        {
            return false;
        }

        return assignment.Parent is InitializerExpressionSyntax;
    }
}
