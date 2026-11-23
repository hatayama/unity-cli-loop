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
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

internal static class PropertyGetterEmitter
{
    internal static void EmitPropertyGettersForType(
        TypeEmitState typeState,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        AddedPropertyCatalog addedPropertyCatalog,
        WorkerInput input,
        List<WorkerEntry> entries,
        List<WorkerSkipped> skipped,
        List<WorkerUnchangedMethod> unchangedMethods,
        List<ShimTypeBuilder> shimTypes,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        ShimNameAllocator shimNames)
    {
        SemanticModel semanticModel = typeState.SourceUnit.SemanticModel;
        CompilationUnitSyntax root = typeState.SourceUnit.BindingRoot;
        BaselineSnapshotState baseline = typeState.SourceUnit.Baseline;
        foreach (PropertyDeclarationSyntax propertyDeclaration in typeState.TypeDeclaration.Members
            .OfType<PropertyDeclarationSyntax>())
        {
            IPropertySymbol propertySymbol = semanticModel.GetDeclaredSymbol(propertyDeclaration);
            if (addedPropertyCatalog.FindBySymbolOrNull(propertySymbol) != null)
            {
                continue;
            }

            // Why a getter of a type a retained artifact serves is decided here rather than by
            // the baseline comparison below: that comparison reads a snapshot of the assembly the
            // type was compiled into, and a type introduced by an earlier reload has none - so
            // every getter of it would be emitted as an entry. The fingerprint comparison that
            // kept this declaration is the only account of which bodies changed, and it named the
            // properties whose getter is their only bodied accessor. The rest run the getter the
            // artifact holds.
            bool isServedByRetainedArtifact = typeState.RetainedChangedMethodKeys != null;
            if (isServedByRetainedArtifact
                && !typeState.RetainedChangedGetterPropertyKeys.Contains(
                    WorkerSyntaxIndex.BuildSyntaxPropertyKey(
                        typeState.TypeMetadataNameFromSyntax,
                        propertyDeclaration)))
            {
                UnchangedOrdinaryMethodRecorder.RecordRetainedPropertyGetterAsUnchanged(
                    typeState,
                    propertyDeclaration,
                    semanticModel,
                    unchangedMethods);
                continue;
            }

            if (typeState.TypeIsAbsentFromCompiledAssembly)
            {
                PropertyGetterClassifier.SkipPropertyGetterOnUncompiledType(
                    typeState.SourceUnit.Input.ProjectRelativePath,
                    propertyDeclaration,
                    semanticModel,
                    skipped);
                continue;
            }

            typeState.CurrentShimType = AppendPropertyGetterEntry(
                typeState.SourceUnit.ExplicitImplementations,
                    propertyDeclaration,
                    typeState.TypeDeclaration,
                    typeState.TypeSymbol,
                    typeState.CompiledType,
                    typeState.AddedEvents,
                    typeState.TypeMetadataNameFromSyntax,
                    semanticModel,
                    root,
                    typeState.SourceUnit.Input.ProjectRelativePath,
                    input,
                    // Why no baseline for a retained type: the snapshot the comparison would read
                    // belongs to the assembly the request named, which never held this getter.
                    // Consulting it would answer about a member of a different type.
                    !isServedByRetainedArtifact && baseline.HasBaseline,
                    baseline.SnapshotPropertyMap,
                    baseline.PlainCurrentPropertyMap,
                    entries,
                    skipped,
                    unchangedMethods,
                    shimTypes,
                    shimNames,
                    typeState.CurrentShimType,
                    assemblyGlobalUsings,
                    addedMethodCatalog,
                    addedFieldCatalog,
                    addedPropertyCatalog,
                    typeState.HomeAssemblyName,
                    typeState.SourceUnit.Input.ReappliedSibling,
                    typeState.TargetAssembly,
                    typeState.SourceUnit.PartialTypeParts,
                    typeState.SourceUnit.PatchBinding);
        }
    }

    // What: emit a get_<Name> entry / unchanged row / skip for one property with a getter body.
    internal static ShimTypeBuilder AppendPropertyGetterEntry(
            ExplicitImplementationMatcher explicitImplementations,
            PropertyDeclarationSyntax propertyDeclaration,
            TypeDeclarationSyntax typeDeclaration,
            INamedTypeSymbol typeSymbol,
            INamedTypeSymbol compiledType,
            AddedEventLookup addedEvents,
            string typeMetadataNameFromSyntax,
            SemanticModel semanticModel,
            CompilationUnitSyntax root,
            string sourceProjectRelativePath,
            WorkerInput input,
            bool hasBaseline,
            Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
            Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap,
            List<WorkerEntry> entries,
            List<WorkerSkipped> skipped,
            List<WorkerUnchangedMethod> unchangedMethods,
            List<ShimTypeBuilder> shimTypes,
            ShimNameAllocator shimNames,
            ShimTypeBuilder currentShimType,
            List<UsingDirectiveSyntax> assemblyGlobalUsings,
            AddedMethodCatalog addedMethodCatalog,
            AddedFieldCatalog addedFieldCatalog,
            AddedPropertyCatalog addedPropertyCatalog,
            string homeAssemblyName,
            bool reappliedSibling,
            IAssemblySymbol targetAssembly,
            PartialTypeParts partialTypeParts,
            PatchBindingCompilation patchBinding)
    {
        IPropertySymbol propertySymbol = semanticModel.GetDeclaredSymbol(propertyDeclaration);
        if (propertySymbol == null || propertySymbol.GetMethod == null)
        {
            return currentShimType;
        }

        string propertyKey = AddedPropertyCatalog.FormatPropertyKey(
            CecilTypeNames.ToMetadataName(typeSymbol),
            propertySymbol.Name);
        if (addedPropertyCatalog.IsClassifiedAdded(propertyKey))
        {
            return currentShimType;
        }

        (bool hasGetterBody, AccessorDeclarationSyntax getAccessor) =
            PropertyGetterClassifier.TryGetPropertyGetterBody(propertyDeclaration);
        if (!hasGetterBody)
        {
            return currentShimType;
        }

        IMethodSymbol getterSymbol = propertySymbol.GetMethod;
        string[] parameterTypeFullNames = Array.Empty<string>();
        // Why the row name: the Editor finds, excludes and reverts the getter by its compiled name,
        // which for an explicit property differs from the source name when the specifier is respelled.
        string getterRowName = explicitImplementations.RowMethodName(compiledType, getterSymbol);
        string methodKey = WorkerMethodKeys.BuildMethodKey(
            CecilTypeNames.ToMetadataName(typeSymbol),
            getterRowName,
            parameterTypeFullNames,
            getterSymbol.Arity);
        if (input.ExcludedMethodKeys.Contains(methodKey))
        {
            return currentShimType;
        }

        if (PropertyGetterClassifier.TryRecordUnchangedPropertyGetter(
            sourceProjectRelativePath,
            hasBaseline,
            snapshotPropertyMap,
            plainCurrentPropertyMap,
            typeMetadataNameFromSyntax,
            propertyDeclaration,
            typeSymbol,
            getterSymbol,
            getterRowName,
            parameterTypeFullNames,
            unchangedMethods))
        {
            return currentShimType;
        }

        WorkerReason unpatchableGetter = PropertyGetterClassifier.DescribeUnpatchableGetterOrNull(
            explicitImplementations,
            propertySymbol,
            compiledType,
            homeAssemblyName);
        if (unpatchableGetter != null)
        {
            skipped.Add(new WorkerSkipped
            {
                SourceProjectRelativePath = sourceProjectRelativePath,
                Method = WorkerMethodKeys.FormatRowLabel(explicitImplementations, compiledType, getterSymbol),
                Reason = unpatchableGetter
            });
            return currentShimType;
        }

        // Why body stays on the property tree: SemanticModel rejects nodes re-parented onto a
        // synthetic MethodDeclaration ("Syntax node is not within syntax tree").
        SyntaxNode getterBodyNode = (SyntaxNode)propertyDeclaration.ExpressionBody
            ?? (SyntaxNode)getAccessor.Body
            ?? getAccessor.ExpressionBody;
        (bool skipGetter, MethodTransformDecision decision) = PropertyGetterClassifier.TrySkipPropertyAccessorByDecision(
            explicitImplementations,
            sourceProjectRelativePath,
            typeDeclaration,
            typeSymbol,
            getterSymbol,
            getterBodyNode,
            semanticModel,
            compiledType,
            addedEvents,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog,
            partialTypeParts,
            targetAssembly,
            patchBinding,
            skipped);
        if (skipGetter)
        {
            return currentShimType;
        }

        // Why the same guard as an ordinary method: a getter of a file pulled back in whose body no
        // longer binds would otherwise reach the shim and fail the run over code the reader never
        // passed. A file the reader passed keeps its errors as Failed.
        if (reappliedSibling)
        {
            WorkerReason siblingSkip = ReappliedSiblingBodyGuard.DescribeSkipOrNull(
                semanticModel,
                getterBodyNode,
                targetAssembly,
                methodDeclarationOrNull: null,
                decision,
                typeSymbol);
            if (siblingSkip != null)
            {
                skipped.Add(new WorkerSkipped
                {
                    SourceProjectRelativePath = sourceProjectRelativePath,
                    Method = WorkerMethodKeys.FormatRowLabel(explicitImplementations, compiledType, getterSymbol),
                    Reason = siblingSkip
                });
                return currentShimType;
            }
        }

        return EmitPropertyGetterShim(
            explicitImplementations,
            propertyDeclaration,
            typeDeclaration,
            typeSymbol,
            getterSymbol,
            getterBodyNode,
            decision,
            methodKey,
            getterRowName,
            parameterTypeFullNames,
            semanticModel,
            root,
            sourceProjectRelativePath,
            entries,
            shimTypes,
            shimNames,
            currentShimType,
            assemblyGlobalUsings,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog,
            homeAssemblyName,
            targetAssembly);
    }

    internal static ShimTypeBuilder EmitPropertyGetterShim(
            ExplicitImplementationMatcher explicitImplementations,
            PropertyDeclarationSyntax propertyDeclaration,
            TypeDeclarationSyntax typeDeclaration,
            INamedTypeSymbol typeSymbol,
            IMethodSymbol getterSymbol,
            SyntaxNode getterBodyNode,
            MethodTransformDecision decision,
            string methodKey,
            string getterRowName,
            string[] parameterTypeFullNames,
            SemanticModel semanticModel,
            CompilationUnitSyntax root,
            string sourceProjectRelativePath,
            List<WorkerEntry> entries,
            List<ShimTypeBuilder> shimTypes,
            ShimNameAllocator shimNames,
            ShimTypeBuilder currentShimType,
            List<UsingDirectiveSyntax> assemblyGlobalUsings,
            AddedMethodCatalog addedMethodCatalog,
            AddedFieldCatalog addedFieldCatalog,
            AddedPropertyCatalog addedPropertyCatalog,
            string homeAssemblyName,
            IAssemblySymbol targetAssembly)
    {
        currentShimType = EnsureShimType(
            currentShimType,
            typeSymbol,
            typeDeclaration,
            root,
            sourceProjectRelativePath,
            shimTypes,
            shimNames,
            assemblyGlobalUsings,
            targetAssembly);

        string shimMethodName = shimNames.NextShimMethodName(ShimBaseName(explicitImplementations, getterSymbol));

        // Why narrow to the get accessor beside a bodied set or init: the setter gets an entry of
        // its own, and a Skipped init body must not resolve to the getter's patch, so the getter
        // entry must not cover either line. Decided from syntax alone so it does not depend on
        // whether the setter was actually patched in this run.
        SyntaxNode spanNode = propertyDeclaration;
        if (PropertySetterClassifier.HasBodiedSetOrInitAccessor(propertyDeclaration))
        {
            AccessorDeclarationSyntax getAccessor = propertyDeclaration.AccessorList.Accessors
                .FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
            if (getAccessor != null)
            {
                spanNode = getAccessor;
            }
        }

        FileLinePositionSpan originalSpan = spanNode.GetLocation().GetLineSpan();
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
            addedFieldCatalog,
            addedPropertyCatalog);
        currentShimType.AddMethod(rewrittenMethod, shimMethodName);

        entries.Add(new WorkerEntry
        {
            SourceProjectRelativePath = sourceProjectRelativePath,
            TypeMetadataName = CecilTypeNames.ToMetadataName(typeSymbol),
            MethodName = getterRowName,
            ParameterTypeFullNames = parameterTypeFullNames,
            GenericArity = getterSymbol.Arity,
            ShimTypeName = currentShimType.ShimTypeName,
            ShimMethodName = shimMethodName,
            PatchKind = decision.PatchKind,
            CalledAddedMethodKeys = AddedCallSiteGuard.CollectCalledAddedMethodKeys(
                getterBodyNode,
                semanticModel,
                addedMethodCatalog,
                addedPropertyCatalog,
                methodKey),
            SourceStartLine = sourceStartLine,
            SourceEndLine = sourceEndLine,
            LifecycleNote = null,
            // Null for a getter of a compiled type, which is patched in the assembly the request
            // named. A getter of a type a retained artifact serves is patched in that artifact,
            // and the row is the only place the Editor learns which assembly to look in.
            HomeAssemblyName = homeAssemblyName
        });

        return currentShimType;
    }

    // What: the name a shim of the accessor is built from. An explicit accessor's own name holds the
    // interface name with dots, which is not a legal identifier, so its shim takes the interface
    // accessor's name.
    internal static string ShimBaseName(ExplicitImplementationMatcher explicitImplementations, IMethodSymbol accessorSymbol)
    {
        return explicitImplementations.IsExplicitImplementation(accessorSymbol)
            ? explicitImplementations.ImplementedMemberName(accessorSymbol)
            : accessorSymbol.Name;
    }

    // What: the shim type this type's accessor shims go into, created on the first one.
    internal static ShimTypeBuilder EnsureShimType(
        ShimTypeBuilder currentShimType,
        INamedTypeSymbol typeSymbol,
        TypeDeclarationSyntax typeDeclaration,
        CompilationUnitSyntax root,
        string sourceProjectRelativePath,
        List<ShimTypeBuilder> shimTypes,
        ShimNameAllocator shimNames,
        List<UsingDirectiveSyntax> assemblyGlobalUsings,
        IAssemblySymbol targetAssembly)
    {
        if (currentShimType != null)
        {
            return currentShimType;
        }

        string shimTypeName = shimNames.NextShimTypeName(typeSymbol.Name);
        string namespaceName = typeSymbol.ContainingNamespace == null
            || typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();
        ShimTypeBuilder shimType = new ShimTypeBuilder(
            shimTypeName,
            namespaceName,
            WorkerUsingCollector.CollectUsingsForType(root, typeDeclaration, assemblyGlobalUsings),
            sourceProjectRelativePath,
            targetAssembly);
        shimTypes.Add(shimType);
        return shimType;
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
        AddedFieldCatalog addedFieldCatalog,
        AddedPropertyCatalog addedPropertyCatalog)
    {
        ShimBodyRewriter rewriter = new ShimBodyRewriter(
            semanticModel,
            targetType,
            accessorPlan,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog);
        SyntaxNode rewrittenBody = rewriter.Visit(getterBodyNode);
        // Why transfer: Visit may rebuild ArrowExpressionClause nodes and drop #line annotations.
        rewrittenBody = TransferUloopLineAnnotations(getterBodyNode, rewrittenBody);

        // Why visit the type with the body's rewriter: a nested type named by its simple name does
        // not resolve in the hoisted shim class, and the rewriter already qualifies such names.
        TypeSyntax returnType = ((TypeSyntax)rewriter.Visit(propertyDeclaration.Type)).WithoutTrivia();
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
