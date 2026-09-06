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
                || binding.Declaration.SyntaxTree != typeState.SourceUnit.SyntaxTree
                || binding.UnavailableReason != null)
            {
                continue;
            }

            if (binding.IsAuto)
            {
                EmitStoreBackedAccessor(typeState, binding, binding.Getter, entries);
                if (binding.Setter != null)
                {
                    EmitStoreBackedAccessor(typeState, binding, binding.Setter, entries);
                }

                continue;
            }

            EmitAccessor(typeState, binding, binding.Getter, addedPropertyCatalog, addedMethodCatalog, addedFieldCatalog, entries);
            if (binding.Setter != null)
            {
                EmitAccessor(typeState, binding, binding.Setter, addedPropertyCatalog, addedMethodCatalog, addedFieldCatalog, entries);
            }
        }
    }

    // Why synthesized instead of rewritten: an auto-property has no user body, so the accessor
    // is built directly against the added-field store rather than visited by the body rewriter.
    private static void EmitStoreBackedAccessor(
        TypeEmitState typeState,
        AddedPropertyBinding binding,
        AddedMethodBinding accessor,
        List<WorkerEntry> entries)
    {
        bool isGetter = accessor.MethodKey == binding.Getter.MethodKey;
        MethodDeclarationSyntax method = isGetter
            ? BuildStoreGetter(binding, accessor)
            : BuildStoreSetter(binding, accessor);
        method = ShimMethodFactory.ToShimMethod(
            method,
            isGetter ? binding.Symbol.GetMethod : binding.Symbol.SetMethod);
        typeState.CurrentShimType.AddMethod(method, accessor.ShimMethodName);
        entries.Add(CreateAccessorEntry(typeState, binding, accessor, Array.Empty<string>()));
    }

    private static MethodDeclarationSyntax BuildStoreGetter(
        AddedPropertyBinding binding,
        AddedMethodBinding accessor)
    {
        List<ArgumentSyntax> arguments = CreateStoreArguments(binding);
        arguments.Add(SyntaxFactory.Argument(CreateStoreInitializer(binding)));
        InvocationExpressionSyntax invocation = AddedFieldShimRewrite.CreateAddedFieldStoreInvocation(
            binding.IsStatic
                ? TransformWorkerProgramMarker.AddedFieldGetOrInitStaticMethodName
                : TransformWorkerProgramMarker.AddedFieldGetOrInitMethodName,
            binding.ValueType,
            arguments);
        // Why a block and not an arrow: a synthesized accessor carries no source line, so a
        // braced body keeps every emitted store accessor delimited in the shim source.
        return SyntaxFactory.MethodDeclaration(binding.Declaration.Type.WithoutTrivia(), accessor.ShimMethodName)
            .WithParameterList(SyntaxFactory.ParameterList())
            .WithBody(SyntaxFactory.Block(SyntaxFactory.ReturnStatement(invocation)));
    }

    private static MethodDeclarationSyntax BuildStoreSetter(
        AddedPropertyBinding binding,
        AddedMethodBinding accessor)
    {
        List<ArgumentSyntax> arguments = CreateStoreArguments(binding);
        arguments.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName("value")));
        InvocationExpressionSyntax invocation = AddedFieldShimRewrite.CreateAddedFieldStoreInvocation(
            binding.IsStatic
                ? TransformWorkerProgramMarker.AddedFieldSetStaticMethodName
                : TransformWorkerProgramMarker.AddedFieldSetMethodName,
            binding.ValueType,
            arguments);
        return SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                accessor.ShimMethodName)
            .WithParameterList(CreateSetterParameterList(binding))
            .WithBody(SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(invocation)));
    }

    private static List<ArgumentSyntax> CreateStoreArguments(AddedPropertyBinding binding)
    {
        List<ArgumentSyntax> arguments = new List<ArgumentSyntax>();
        if (!binding.IsStatic)
        {
            arguments.Add(SyntaxFactory.Argument(
                SyntaxFactory.IdentifierName(TransformWorkerProgramMarker.InstanceParameterName)));
        }

        arguments.Add(SyntaxFactory.Argument(
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(binding.StoreFieldKey))));
        return arguments;
    }

    private static ExpressionSyntax CreateStoreInitializer(AddedPropertyBinding binding)
    {
        return AddedFieldShimRewrite.CreateAddedFieldInitializer(new AddedFieldBinding
        {
            Initializer = binding.Initializer
        });
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
        bool isGetter = accessor.MethodKey == binding.Getter.MethodKey;
        SyntaxNode body = GetAccessorBody(binding.Declaration, isGetter);
        if (body == null)
        {
            return;
        }

        MethodTransformDecision decision = isGetter
            ? binding.GetterDecision
            : binding.SetterDecision;
        Debug.Assert(decision != null, "Each emitted added accessor must keep its transform decision.");

        MethodDeclarationSyntax shimMethod = BuildAccessorShim(
            binding,
            accessor,
            body,
            decision,
            typeState,
            addedPropertyCatalog,
            addedMethodCatalog,
            addedFieldCatalog);
        typeState.CurrentShimType.AddMethod(shimMethod, accessor.ShimMethodName);
        entries.Add(CreateAccessorEntry(
            typeState,
            binding,
            accessor,
            AddedCallSiteGuard.CollectCalledAddedMethodKeys(
                body,
                typeState.SourceUnit.SemanticModel,
                addedMethodCatalog,
                addedPropertyCatalog,
                accessor.MethodKey)));
    }

    private static WorkerEntry CreateAccessorEntry(
        TypeEmitState typeState,
        AddedPropertyBinding binding,
        AddedMethodBinding accessor,
        string[] calledAddedMethodKeys)
    {
        bool isGetter = accessor.MethodKey == binding.Getter.MethodKey;
        FileLinePositionSpan span = binding.Declaration.GetLocation().GetLineSpan();
        return new WorkerEntry
        {
            SourceProjectRelativePath = binding.SourceProjectRelativePath,
            TypeMetadataName = CecilTypeNames.ToMetadataName(typeState.TypeSymbol),
            MethodName = isGetter ? binding.Symbol.GetMethod.Name : binding.Symbol.SetMethod.Name,
            ParameterTypeFullNames = isGetter
                ? Array.Empty<string>()
                : new[] { CecilTypeNames.ToCecilFullName(binding.ValueType) },
            GenericArity = 0,
            ShimTypeName = accessor.ShimTypeName,
            ShimMethodName = accessor.ShimMethodName,
            PatchKind = PatchKinds.AddedMethod,
            CalledAddedMethodKeys = calledAddedMethodKeys,
            SourceStartLine = span.StartLinePosition.Line + 1,
            SourceEndLine = span.EndLinePosition.Line + 1,
            LifecycleNote = null
        };
    }

    private static MethodDeclarationSyntax BuildAccessorShim(
        AddedPropertyBinding binding,
        AddedMethodBinding accessor,
        SyntaxNode body,
        MethodTransformDecision decision,
        TypeEmitState typeState,
        AddedPropertyCatalog addedPropertyCatalog,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        // The guard decides this before caller rewrites; emission must retain the same plan so
        // private compiled members are not emitted as illegal direct shim access.
        AccessorPlan accessorPlan = decision.UsesDelegation
            ? typeState.CurrentShimType.AccessorPlan
            : null;
        ShimBodyRewriter rewriter = new ShimBodyRewriter(
            typeState.SourceUnit.SemanticModel,
            typeState.TypeSymbol,
            accessorPlan,
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
