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
/// Emits bodied added-property accessors as added-method shims after ordinary methods are queued.
/// </summary>
internal static class AddedPropertyEmitter
{
    internal static void EmitAddedPropertyAccessors(
        TypeEmitState typeState,
        AddedPropertyCatalog addedPropertyCatalog,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerEntry> entries)
    {
        foreach (AddedPropertyBinding binding in addedPropertyCatalog.Bindings)
        {
            if (!SymbolEqualityComparer.Default.Equals(binding.HostType, typeState.TypeSymbol)
                || binding.UnavailableReason != null
                || binding.IsAuto)
            {
                continue;
            }

            EmitAccessor(typeState, binding, binding.Getter, addedPropertyCatalog, addedMethodCatalog, addedFieldCatalog, entries);
            if (binding.Setter != null)
            {
                EmitAccessor(typeState, binding, binding.Setter, addedPropertyCatalog, addedMethodCatalog, addedFieldCatalog, entries);
            }
        }
    }

    private static void EmitAccessor(
        TypeEmitState typeState,
        AddedPropertyBinding binding,
        AddedMethodBinding accessor,
        AddedPropertyCatalog addedPropertyCatalog,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerEntry> entries)
    {
        SyntaxNode body = GetAccessorBody(binding.Declaration, accessor.MethodKey == binding.Getter.MethodKey);
        if (body == null)
        {
            return;
        }

        MethodDeclarationSyntax shimMethod = BuildAccessorShim(
            binding,
            accessor,
            body,
            typeState,
            addedPropertyCatalog,
            addedMethodCatalog,
            addedFieldCatalog);
        typeState.CurrentShimType.AddMethod(shimMethod, accessor.ShimMethodName);

        FileLinePositionSpan span = binding.Declaration.GetLocation().GetLineSpan();
        entries.Add(new WorkerEntry
        {
            SourceProjectRelativePath = binding.SourceProjectRelativePath,
            TypeMetadataName = CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
            MethodName = accessor.MethodKey == binding.Getter.MethodKey
                ? binding.Symbol.GetMethod.Name
                : binding.Symbol.SetMethod.Name,
            ParameterTypeFullNames = accessor.MethodKey == binding.Getter.MethodKey
                ? Array.Empty<string>()
                : new[] { CecilTypeNames.ToCecilFullName(binding.ValueType) },
            GenericArity = 0,
            ShimTypeName = accessor.ShimTypeName,
            ShimMethodName = accessor.ShimMethodName,
            PatchKind = PatchKinds.AddedMethod,
            CalledAddedMethodKeys = AddedCallSiteGuard.CollectCalledAddedMethodKeys(
                body,
                typeState.SourceUnit.SemanticModel,
                addedMethodCatalog,
                addedPropertyCatalog,
                accessor.MethodKey),
            SourceStartLine = span.StartLinePosition.Line + 1,
            SourceEndLine = span.EndLinePosition.Line + 1,
            LifecycleNote = null
        });
    }

    private static MethodDeclarationSyntax BuildAccessorShim(
        AddedPropertyBinding binding,
        AddedMethodBinding accessor,
        SyntaxNode body,
        TypeEmitState typeState,
        AddedPropertyCatalog addedPropertyCatalog,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        ShimBodyRewriter rewriter = new ShimBodyRewriter(
            typeState.SourceUnit.SemanticModel,
            typeState.TypeSymbol,
            null,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog);
        SyntaxNode rewrittenBody = PropertyGetterEmitter.TransferUloopLineAnnotations(
            body,
            rewriter.Visit(body));
        bool isGetter = accessor.MethodKey == binding.Getter.MethodKey;
        MethodDeclarationSyntax method = SyntaxFactory.MethodDeclaration(
                isGetter
                    ? binding.Declaration.Type.WithoutTrivia()
                    : SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                accessor.ShimMethodName)
            .WithParameterList(isGetter ? SyntaxFactory.ParameterList() : CreateSetterParameterList(binding));
        method = ApplyBody(method, rewrittenBody);
        return ShimMethodFactory.ToShimMethod(method, isGetter ? binding.Symbol.GetMethod : binding.Symbol.SetMethod);
    }

    private static ParameterListSyntax CreateSetterParameterList(AddedPropertyBinding binding)
    {
        ParameterSyntax parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("value"))
            .WithType(binding.Declaration.Type.WithoutTrivia());
        return SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(parameter));
    }

    private static MethodDeclarationSyntax ApplyBody(MethodDeclarationSyntax method, SyntaxNode body)
    {
        if (body is BlockSyntax block)
        {
            return method.WithBody(block);
        }

        ArrowExpressionClauseSyntax arrow = body as ArrowExpressionClauseSyntax;
        if (arrow == null)
        {
            arrow = SyntaxFactory.ArrowExpressionClause((ExpressionSyntax)body);
        }

        return method
            .WithExpressionBody(arrow)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
    }

    private static SyntaxNode GetAccessorBody(PropertyDeclarationSyntax declaration, bool getter)
    {
        if (getter && declaration.ExpressionBody != null)
        {
            return declaration.ExpressionBody;
        }

        if (declaration.AccessorList == null)
        {
            return null;
        }

        SyntaxKind expectedKind = getter ? SyntaxKind.GetAccessorDeclaration : SyntaxKind.SetAccessorDeclaration;
        foreach (AccessorDeclarationSyntax accessor in declaration.AccessorList.Accessors)
        {
            if (accessor.IsKind(expectedKind))
            {
                return (SyntaxNode)accessor.Body ?? accessor.ExpressionBody;
            }
        }

        return null;
    }
}
