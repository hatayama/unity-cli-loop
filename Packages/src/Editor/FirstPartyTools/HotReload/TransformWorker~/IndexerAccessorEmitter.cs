using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// What: emits an entry, an unchanged record, or a skip for each bodied accessor of a compiled
// indexer that IndexerAccessorRules hands to this emitter. The entry names the compiled accessor
// (get_Item, set_Item, or the [IndexerName] name) with the index parameter types, followed by the
// value type for a setter, and the shim takes the instance, the index parameters, and the value.
internal static class IndexerAccessorEmitter
{
    internal static void EmitIndexerAccessorsForType(
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
        bool servedByRetainedArtifact = typeState.HomeAssemblyName != null;
        foreach (IndexerDeclarationSyntax indexer in typeState.TypeDeclaration.Members.OfType<IndexerDeclarationSyntax>())
        {
            IPropertySymbol indexerSymbol = semanticModel.GetDeclaredSymbol(indexer);
            foreach (IndexerBodiedAccessor accessor in IndexerAccessorRules.FindBodiedAccessors(indexer))
            {
                if (!IndexerAccessorRules.IsReportedByEmitter(
                        indexer,
                        indexerSymbol,
                        accessor.Kind,
                        typeState.CompiledType,
                        servedByRetainedArtifact))
                {
                    continue;
                }

                IndexerAccessorTarget target = new IndexerAccessorTarget(
                    indexer,
                    accessor,
                    UnsupportedMemberSkipCollector.ResolveAccessorMethodSymbol(indexerSymbol, accessor.Kind));
                if (target.AccessorSymbol == null)
                {
                    continue;
                }

                EmitAccessor(
                    typeState,
                    target,
                    addedMethodCatalog,
                    addedFieldCatalog,
                    addedPropertyCatalog,
                    input,
                    entries,
                    skipped,
                    unchangedMethods,
                    shimTypes,
                    assemblyGlobalUsings,
                    shimNames);
            }
        }
    }

    private static void EmitAccessor(
        TypeEmitState typeState,
        IndexerAccessorTarget target,
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
        IMethodSymbol accessorSymbol = target.AccessorSymbol;
        string typeMetadataName = CecilTypeNames.ToMetadataName(typeState.TypeSymbol);
        string[] parameterTypeFullNames = accessorSymbol.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .ToArray();
        string methodKey = WorkerMethodKeys.BuildMethodKey(
            typeMetadataName,
            accessorSymbol.Name,
            parameterTypeFullNames,
            accessorSymbol.Arity);
        if (input.ExcludedMethodKeys.Contains(methodKey))
        {
            return;
        }

        string sourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath;
        // Why before the skips below: an unchanged accessor must be recorded so an earlier patch on
        // it is reverted, and an unedited accessor must not add Skipped noise.
        BaselineSnapshotState baseline = typeState.SourceUnit.Baseline;
        if (baseline.HasBaseline
            && IndexerAccessorRules.IsAccessorUnchangedFromBaseline(
                typeState.TypeMetadataNameFromSyntax,
                target.Indexer,
                target.Accessor.Kind,
                baseline.SnapshotIndexerMap,
                baseline.PlainCurrentIndexerMap))
        {
            unchangedMethods.Add(new WorkerUnchangedMethod
            {
                SourceProjectRelativePath = sourceProjectRelativePath,
                TypeMetadataName = typeMetadataName,
                MethodName = accessorSymbol.Name,
                ParameterTypeFullNames = parameterTypeFullNames,
                GenericArity = accessorSymbol.Arity
            });
            return;
        }

        if (target.Indexer.ExplicitInterfaceSpecifier != null)
        {
            AddSkip(skipped, sourceProjectRelativePath, typeState.SourceUnit.ExplicitImplementations, accessorSymbol, WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformExplicitInterfaceImplementation));
            return;
        }

        MethodTransformDecision decision = DecideOrSkip(
            typeState,
            accessorSymbol,
            target.Accessor.BodyNode,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog,
            skipped);
        if (decision == null)
        {
            return;
        }

        typeState.CurrentShimType = PropertyGetterEmitter.EnsureShimType(
            typeState.CurrentShimType,
            typeState.TypeSymbol,
            typeState.TypeDeclaration,
            typeState.SourceUnit.BindingRoot,
            sourceProjectRelativePath,
            shimTypes,
            shimNames,
            assemblyGlobalUsings,
            typeState.TargetAssembly);
        ShimTypeBuilder shimType = typeState.CurrentShimType;
        string shimMethodName = shimNames.NextShimMethodName(accessorSymbol.Name);
        MethodDeclarationSyntax rewrittenMethod = RewriteAccessorBody(
            target,
            typeState.TypeSymbol,
            typeState.SourceUnit.SemanticModel,
            decision.UsesDelegation ? shimType.AccessorPlan : null,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog);
        shimType.AddMethod(rewrittenMethod, shimMethodName);

        // Why the accessor's own span: the other accessor of the same indexer gets an entry of its
        // own, and overlapping spans leave compile-error attribution and pause-point resolution
        // unable to tell the two apart.
        FileLinePositionSpan span = target.Accessor.SpanNode.GetLocation().GetLineSpan();
        entries.Add(new WorkerEntry
        {
            SourceProjectRelativePath = sourceProjectRelativePath,
            TypeMetadataName = typeMetadataName,
            MethodName = accessorSymbol.Name,
            ParameterTypeFullNames = parameterTypeFullNames,
            GenericArity = accessorSymbol.Arity,
            ShimTypeName = shimType.ShimTypeName,
            ShimMethodName = shimMethodName,
            PatchKind = decision.PatchKind,
            CalledAddedMethodKeys = AddedCallSiteGuard.CollectCalledAddedMethodKeys(
                target.Accessor.BodyNode,
                typeState.SourceUnit.SemanticModel,
                addedMethodCatalog,
                addedPropertyCatalog,
                methodKey),
            SourceStartLine = span.StartLinePosition.Line + 1,
            SourceEndLine = span.EndLinePosition.Line + 1,
            LifecycleNote = null,
            HomeAssemblyName = null
        });
    }

    // What: the transform decision for the accessor body, or null after adding the skip row when
    // the decision, the name/added-call guards, or the re-applied sibling guard rejects it. The same
    // checks a property accessor gets, so base-member use, a struct host, and a generic host stay
    // refused with their existing reasons.
    private static MethodTransformDecision DecideOrSkip(
        TypeEmitState typeState,
        IMethodSymbol accessorSymbol,
        SyntaxNode bodyNode,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        AddedPropertyCatalog addedPropertyCatalog,
        List<WorkerSkipped> skipped)
    {
        string sourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath;
        (bool skipAccessor, MethodTransformDecision decision) = PropertyGetterClassifier.TrySkipPropertyAccessorByDecision(
            typeState.SourceUnit.ExplicitImplementations,
            sourceProjectRelativePath,
            typeState.TypeDeclaration,
            typeState.TypeSymbol,
            accessorSymbol,
            bodyNode,
            typeState.SourceUnit.SemanticModel,
            typeState.CompiledType,
            typeState.AddedEvents,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog,
            typeState.SourceUnit.PartialTypeParts,
            typeState.TargetAssembly,
            typeState.SourceUnit.PatchBinding,
            skipped);
        if (skipAccessor)
        {
            return null;
        }

        // Why the same guard as a property accessor: an accessor of a file pulled back in whose body
        // no longer binds would otherwise reach the shim and fail the run over code the reader never
        // passed.
        if (!typeState.SourceUnit.Input.ReappliedSibling)
        {
            return decision;
        }

        WorkerReason siblingSkip = ReappliedSiblingBodyGuard.DescribeSkipOrNull(
            typeState.SourceUnit.SemanticModel,
            bodyNode,
            typeState.TargetAssembly,
            methodDeclarationOrNull: null,
            decision,
            typeState.TypeSymbol);
        if (siblingSkip == null)
        {
            return decision;
        }

        AddSkip(skipped, sourceProjectRelativePath, typeState.SourceUnit.ExplicitImplementations, accessorSymbol, siblingSkip);
        return null;
    }

    // What: rewrite the accessor body while it is still in the bound tree, then wrap it as a static
    // shim whose index parameters (and `value` for a setter) are the ones the body binds to.
    private static MethodDeclarationSyntax RewriteAccessorBody(
        IndexerAccessorTarget target,
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
        SyntaxNode bodyNode = target.Accessor.BodyNode;
        SyntaxNode rewrittenBody = rewriter.Visit(bodyNode);
        // Why transfer: Visit may rebuild ArrowExpressionClause nodes and drop #line annotations.
        rewrittenBody = PropertyGetterEmitter.TransferUloopLineAnnotations(bodyNode, rewrittenBody);

        // Why visit the types with the body's rewriter: a nested type named by its simple name does
        // not resolve in the hoisted shim class, and the rewriter already qualifies such names.
        TypeSyntax elementType = ((TypeSyntax)rewriter.Visit(target.Indexer.Type)).WithoutTrivia();
        bool isGetter = target.Accessor.Kind == SyntaxKind.GetAccessorDeclaration;
        List<ParameterSyntax> parameters = new List<ParameterSyntax>();
        foreach (ParameterSyntax parameter in target.Indexer.ParameterList.Parameters)
        {
            // Why drop params and default values: the setter shim appends value after the index
            // parameters, where neither may precede it (CS0231, CS1737), and the patch, not the
            // user, calls the shim with every argument. The in modifier stays, so the signature
            // still matches the compiled accessor.
            parameters.Add(parameter
                .WithAttributeLists(default)
                .WithModifiers(SyntaxFactory.TokenList(
                    parameter.Modifiers.Where(modifier => !modifier.IsKind(SyntaxKind.ParamsKeyword))))
                .WithDefault(null)
                .WithType(((TypeSyntax)rewriter.Visit(parameter.Type)).WithoutTrivia())
                .WithoutTrivia());
        }

        if (!isGetter)
        {
            parameters.Add(AddedPropertyEmitter.CreateSetterParameterList(elementType).Parameters[0]);
        }

        MethodDeclarationSyntax method = SyntaxFactory.MethodDeclaration(
                isGetter ? elementType : SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                SyntaxFactory.Identifier(target.AccessorSymbol.Name))
            .WithModifiers(
                SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters)));
        method = ApplyBody(method, bodyNode, rewrittenBody);

        // ToShimMethod forces public static and injects the instance parameter for instance accessors.
        return ShimMethodFactory.ToShimMethod(method, target.AccessorSymbol);
    }

    private static MethodDeclarationSyntax ApplyBody(
        MethodDeclarationSyntax method,
        SyntaxNode originalBody,
        SyntaxNode rewrittenBody)
    {
        if (rewrittenBody is BlockSyntax blockBody)
        {
            return method.WithBody(blockBody);
        }

        ArrowExpressionClauseSyntax arrowBody = rewrittenBody as ArrowExpressionClauseSyntax;
        if (arrowBody == null)
        {
            // An arrow body rewritten to a bare expression: wrap it as an arrow again.
            arrowBody = (ArrowExpressionClauseSyntax)PropertyGetterEmitter.TransferUloopLineAnnotations(
                originalBody,
                SyntaxFactory.ArrowExpressionClause((ExpressionSyntax)rewrittenBody));
        }

        return method
            .WithExpressionBody(arrowBody)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
    }

    private static void AddSkip(
        List<WorkerSkipped> skipped,
        string sourceProjectRelativePath,
        ExplicitImplementationMatcher explicitImplementations,
        IMethodSymbol accessorSymbol,
        WorkerReason reason)
    {
        skipped.Add(new WorkerSkipped
        {
            SourceProjectRelativePath = sourceProjectRelativePath,
            Method = WorkerMethodKeys.FormatSourceLabel(explicitImplementations, accessorSymbol),
            Reason = reason
        });
    }

    // What: one indexer accessor this emitter reports, with the symbols it is emitted under.
    private sealed class IndexerAccessorTarget
    {
        internal IndexerAccessorTarget(
            IndexerDeclarationSyntax indexer,
            IndexerBodiedAccessor accessor,
            IMethodSymbol accessorSymbol)
        {
            Indexer = indexer;
            Accessor = accessor;
            AccessorSymbol = accessorSymbol;
        }

        internal IndexerDeclarationSyntax Indexer { get; }

        internal IndexerBodiedAccessor Accessor { get; }

        internal IMethodSymbol AccessorSymbol { get; }
    }
}
