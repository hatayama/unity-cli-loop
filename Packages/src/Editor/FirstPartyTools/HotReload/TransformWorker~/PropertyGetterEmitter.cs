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

internal static class PropertyGetterEmitter
{
    internal static (int ShimTypeCounter, int GlobalShimMethodCounter) EmitPropertyGettersForType(
        TypeEmitState typeState,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        CompilationUnitSyntax root,
        WorkerInput input,
        BaselineSnapshotState baseline,
        List<WorkerEntry> entries,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        List<ShimTypeBuilder> shimTypes,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        int shimTypeCounter,
        int globalShimMethodCounter)
    {
        foreach (PropertyDeclarationSyntax propertyDeclaration in typeState.TypeDeclaration.Members
            .OfType<PropertyDeclarationSyntax>())
        {
            if (typeState.TypeIsAbsentFromCompiledAssembly)
            {
                PropertyGetterClassifier.SkipPropertyGetterOnUncompiledType(
                    propertyDeclaration,
                    semanticModel,
                    skipped);
                continue;
            }

            (ShimTypeBuilder nextShimType, int nextShimTypeCounter, int nextGlobalShimMethodCounter) =
                AppendPropertyGetterEntry(
                    propertyDeclaration,
                    typeState.TypeDeclaration,
                    typeState.TypeSymbol,
                    typeState.TypeMetadataNameFromSyntax,
                    semanticModel,
                    root,
                    input,
                    baseline.HasBaseline,
                    baseline.SnapshotPropertyMap,
                    baseline.PlainCurrentPropertyMap,
                    entries,
                    skipped,
                    unchangedMethods,
                    shimTypes,
                    shimTypeCounter,
                    globalShimMethodCounter,
                    typeState.CurrentShimType,
                    assemblyGlobalUsings,
                    addedMethodCatalog,
                    addedFieldCatalog);
            typeState.CurrentShimType = nextShimType;
            shimTypeCounter = nextShimTypeCounter;
            globalShimMethodCounter = nextGlobalShimMethodCounter;
        }

        return (shimTypeCounter, globalShimMethodCounter);
    }

    // What: emit a get_<Name> entry / unchanged row / skip for one property with a getter body.
    internal static (ShimTypeBuilder CurrentShimType, int ShimTypeCounter, int GlobalShimMethodCounter)
        AppendPropertyGetterEntry(
            PropertyDeclarationSyntax propertyDeclaration,
            TypeDeclarationSyntax typeDeclaration,
            INamedTypeSymbol typeSymbol,
            string typeMetadataNameFromSyntax,
            SemanticModel semanticModel,
            CompilationUnitSyntax root,
            WorkerInput input,
            bool hasBaseline,
            Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
            Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap,
            List<WorkerEntry> entries,
            List<WorkerSkipped> skipped,
            List<WorkerUnchangedMethod> unchangedMethods,
            List<ShimTypeBuilder> shimTypes,
            int shimTypeCounter,
            int globalShimMethodCounter,
            ShimTypeBuilder currentShimType,
            List<UsingDirectiveSyntax> assemblyGlobalUsings,
            AddedMethodCatalog addedMethodCatalog,
            AddedFieldCatalog addedFieldCatalog)
    {
        IPropertySymbol propertySymbol = semanticModel.GetDeclaredSymbol(propertyDeclaration);
        if (propertySymbol == null || propertySymbol.GetMethod == null)
        {
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        (bool hasGetterBody, AccessorDeclarationSyntax getAccessor) =
            PropertyGetterClassifier.TryGetPropertyGetterBody(propertyDeclaration);
        if (!hasGetterBody)
        {
            // Auto-property / setter-only: not a patch candidate (no Skipped row either).
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        IMethodSymbol getterSymbol = propertySymbol.GetMethod;
        string[] parameterTypeFullNames = Array.Empty<string>();
        string methodKey = WorkerMethodKeys.BuildMethodKey(
            CecilTypeNames.ToMetadataName(typeSymbol),
            getterSymbol.Name,
            parameterTypeFullNames,
            getterSymbol.Arity);
        if (input.ExcludedMethodKeys.Contains(methodKey))
        {
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        if (PropertyGetterClassifier.TryRecordUnchangedPropertyGetter(
            hasBaseline,
            snapshotPropertyMap,
            plainCurrentPropertyMap,
            typeMetadataNameFromSyntax,
            propertyDeclaration,
            typeSymbol,
            getterSymbol,
            parameterTypeFullNames,
            unchangedMethods))
        {
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        // Why skip newly added properties: Harmony looks up get_<Name> on the compiled type
        // and fails with "No method 'get_X' ... was found" when the member does not exist.
        if (PropertyGetterClassifier.TrySkipAddedProperty(
            hasBaseline,
            snapshotPropertyMap,
            plainCurrentPropertyMap,
            typeMetadataNameFromSyntax,
            propertyDeclaration,
            getterSymbol,
            skipped,
            addedMethodCatalog))
        {
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        if (propertyDeclaration.ExplicitInterfaceSpecifier != null)
        {
            skipped.Add(new WorkerSkipped
            {
                Method = WorkerMethodKeys.FormatMethodLabel(getterSymbol),
                Reason = "Explicit interface implementations are skipped in v1."
            });
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        // Why body stays on the property tree: SemanticModel rejects nodes re-parented onto a
        // synthetic MethodDeclaration ("Syntax node is not within syntax tree").
        SyntaxNode getterBodyNode = (SyntaxNode)propertyDeclaration.ExpressionBody
            ?? (SyntaxNode)getAccessor.Body
            ?? getAccessor.ExpressionBody;
        (bool skipGetter, MethodTransformDecision decision) = PropertyGetterClassifier.TrySkipPropertyGetterByDecision(
            typeDeclaration,
            typeSymbol,
            getterSymbol,
            getterBodyNode,
            semanticModel,
            addedMethodCatalog,
            addedFieldCatalog,
            skipped);
        if (skipGetter)
        {
            return (currentShimType, shimTypeCounter, globalShimMethodCounter);
        }

        return EmitPropertyGetterShim(
            propertyDeclaration,
            typeDeclaration,
            typeSymbol,
            getterSymbol,
            getterBodyNode,
            decision,
            methodKey,
            parameterTypeFullNames,
            semanticModel,
            root,
            entries,
            shimTypes,
            shimTypeCounter,
            globalShimMethodCounter,
            currentShimType,
            assemblyGlobalUsings,
            addedMethodCatalog,
            addedFieldCatalog);
    }

    internal static (ShimTypeBuilder CurrentShimType, int ShimTypeCounter, int GlobalShimMethodCounter)
        EmitPropertyGetterShim(
            PropertyDeclarationSyntax propertyDeclaration,
            TypeDeclarationSyntax typeDeclaration,
            INamedTypeSymbol typeSymbol,
            IMethodSymbol getterSymbol,
            SyntaxNode getterBodyNode,
            MethodTransformDecision decision,
            string methodKey,
            string[] parameterTypeFullNames,
            SemanticModel semanticModel,
            CompilationUnitSyntax root,
            List<WorkerEntry> entries,
            List<ShimTypeBuilder> shimTypes,
            int shimTypeCounter,
            int globalShimMethodCounter,
            ShimTypeBuilder currentShimType,
            List<UsingDirectiveSyntax> assemblyGlobalUsings,
            AddedMethodCatalog addedMethodCatalog,
            AddedFieldCatalog addedFieldCatalog)
    {
        if (currentShimType == null)
        {
            string shimTypeName = typeSymbol.Name + "_UloopHotReloadShims_" + shimTypeCounter;
            shimTypeCounter++;
            string namespaceName = typeSymbol.ContainingNamespace == null
                || typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : typeSymbol.ContainingNamespace.ToDisplayString();
            currentShimType = new ShimTypeBuilder(
                shimTypeName,
                namespaceName,
                WorkerUsingCollector.CollectUsingsForType(root, typeDeclaration, assemblyGlobalUsings));
            shimTypes.Add(currentShimType);
        }

        string shimMethodName = getterSymbol.Name + "__shim" + globalShimMethodCounter;
        globalShimMethodCounter++;

        FileLinePositionSpan originalSpan = propertyDeclaration.GetLocation().GetLineSpan();
        int sourceStartLine = originalSpan.StartLinePosition.Line + 1;
        int sourceEndLine = originalSpan.EndLinePosition.Line + 1;

        AccessorPlan rewritePlan = decision.UsesDelegation
            ? currentShimType.AccessorPlan
            : null;
        MethodDeclarationSyntax rewrittenMethod = RewritePropertyGetterBody(
            propertyDeclaration,
            getterBodyNode,
            getterSymbol,
            typeSymbol,
            semanticModel,
            rewritePlan,
            addedMethodCatalog,
            addedFieldCatalog);
        currentShimType.AddMethod(rewrittenMethod, shimMethodName);

        entries.Add(new WorkerEntry
        {
            TypeMetadataName = CecilTypeNames.ToMetadataName(typeSymbol),
            MethodName = getterSymbol.Name,
            ParameterTypeFullNames = parameterTypeFullNames,
            GenericArity = getterSymbol.Arity,
            ShimTypeName = currentShimType.ShimTypeName,
            ShimMethodName = shimMethodName,
            PatchKind = decision.PatchKind,
            CalledAddedMethodKeys = AddedCallSiteGuard.CollectCalledAddedMethodKeys(
                getterBodyNode,
                semanticModel,
                addedMethodCatalog,
                methodKey),
            SourceStartLine = sourceStartLine,
            SourceEndLine = sourceEndLine,
            LifecycleNote = null
        });

        return (currentShimType, shimTypeCounter, globalShimMethodCounter);
    }

    // What: rewrite a getter body while it is still in the bound tree, then wrap as a shim method.
    internal static MethodDeclarationSyntax RewritePropertyGetterBody(
        PropertyDeclarationSyntax propertyDeclaration,
        SyntaxNode getterBodyNode,
        IMethodSymbol getterSymbol,
        INamedTypeSymbol targetType,
        SemanticModel semanticModel,
        AccessorPlan accessorPlan,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        ShimBodyRewriter rewriter = new ShimBodyRewriter(
            semanticModel,
            targetType,
            accessorPlan,
            addedMethodCatalog,
            addedFieldCatalog);
        SyntaxNode rewrittenBody = rewriter.Visit(getterBodyNode);
        // Why transfer: Visit may rebuild ArrowExpressionClause nodes and drop #line annotations.
        rewrittenBody = TransferUloopLineAnnotations(getterBodyNode, rewrittenBody);

        TypeSyntax returnType = propertyDeclaration.Type.WithoutTrivia();
        // ToShimMethod forces public static and injects __instance for instance getters.
        MethodDeclarationSyntax method = SyntaxFactory.MethodDeclaration(
                returnType,
                SyntaxFactory.Identifier(getterSymbol.Name))
            .WithModifiers(
                SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList());

        if (rewrittenBody is ArrowExpressionClauseSyntax arrowBody)
        {
            method = method
                .WithExpressionBody(arrowBody)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }
        else if (rewrittenBody is BlockSyntax blockBody)
        {
            method = method.WithBody(blockBody);
        }
        else
        {
            // get => expr rewritten to a bare expression: wrap as arrow.
            ArrowExpressionClauseSyntax wrappedArrow = SyntaxFactory.ArrowExpressionClause(
                (ExpressionSyntax)rewrittenBody);
            wrappedArrow = (ArrowExpressionClauseSyntax)TransferUloopLineAnnotations(
                getterBodyNode,
                wrappedArrow);
            method = method
                .WithExpressionBody(wrappedArrow)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        return ShimMethodFactory.ToShimMethod(method, getterSymbol);
    }

    internal static SyntaxNode TransferUloopLineAnnotations(SyntaxNode source, SyntaxNode target)
    {
        if (source == null || target == null)
        {
            return target;
        }

        SyntaxNode result = target;
        foreach (SyntaxAnnotation annotation in source.GetAnnotations(TransformWorkerProgram.UloopLineAnnotationKind))
        {
            result = result.WithAdditionalAnnotations(annotation);
        }

        return result;
    }
}
