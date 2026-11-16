using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// What: emits a set_<Name> entry, unchanged row, or skip for each bodied set accessor of a
// compiled property that PropertySetterClassifier hands to it. The skip collector reports every
// other set and init accessor.
internal static class PropertySetterEmitter
{
    internal static void EmitPropertySettersForType(
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
        foreach (PropertyDeclarationSyntax property in typeState.TypeDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            IPropertySymbol propertySymbol = semanticModel.GetDeclaredSymbol(property);
            // Added properties are AddedPropertyEmitter's; the rest the skip collector reported.
            if (addedPropertyCatalog.FindBySymbolOrNull(propertySymbol) != null
                || !PropertySetterClassifier.IsPatchableCompiledSetter(
                    property,
                    propertySymbol,
                    typeState.CompiledType,
                    servedByRetainedArtifact))
            {
                continue;
            }

            EmitPropertySetter(
                typeState,
                property,
                propertySymbol,
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

    private static void EmitPropertySetter(
        TypeEmitState typeState,
        PropertyDeclarationSyntax property,
        IPropertySymbol propertySymbol,
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
        IMethodSymbol setterSymbol = propertySymbol.SetMethod;
        string typeMetadataName = CecilTypeNames.ToMetadataName(typeState.TypeSymbol);
        string[] parameterTypeFullNames = new[] { CecilTypeNames.ToCecilFullName(propertySymbol.Type) };
        // Why the row name: as for the getter, the Editor knows the setter by its compiled name.
        string setterRowName = ExplicitImplementationMatcher.RowMethodName(typeState.CompiledType, setterSymbol);
        string methodKey = WorkerMethodKeys.BuildMethodKey(
            typeMetadataName,
            setterRowName,
            parameterTypeFullNames,
            setterSymbol.Arity);
        if (input.ExcludedMethodKeys.Contains(methodKey))
        {
            return;
        }

        string sourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath;
        // Why before the skips below: an unchanged setter must be recorded so an earlier patch on
        // it is reverted, and an unedited member must not add Skipped noise (the getter's order).
        // An interface default setter takes this path like any other, as its getter does.
        if (IsSetterUnchangedFromBaseline(typeState, property))
        {
            unchangedMethods.Add(new WorkerUnchangedMethod
            {
                SourceProjectRelativePath = sourceProjectRelativePath,
                TypeMetadataName = typeMetadataName,
                MethodName = setterRowName,
                ParameterTypeFullNames = parameterTypeFullNames,
                GenericArity = setterSymbol.Arity
            });
            return;
        }

        // Why body stays on the property tree: SemanticModel rejects nodes re-parented onto a
        // synthetic MethodDeclaration ("Syntax node is not within syntax tree").
        AccessorDeclarationSyntax setAccessor = PropertySetterClassifier.FindBodiedSetOrInitAccessorOrNull(property);
        SyntaxNode setterBodyNode = (SyntaxNode)setAccessor.Body ?? setAccessor.ExpressionBody;
        if (setterSymbol.IsInitOnly
            && InitAccessorWriteRules.WritesInitOnlyMember(setterBodyNode, typeState.SourceUnit.SemanticModel))
        {
            AddSkip(skipped, sourceProjectRelativePath, typeState.CompiledType, setterSymbol, WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformInitAccessorWritesInitOnlyMember));
            return;
        }

        MethodTransformDecision decision = DecideOrSkip(
            typeState,
            setterSymbol,
            setterBodyNode,
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
            assemblyGlobalUsings);
        ShimTypeBuilder shimType = typeState.CurrentShimType;
        string shimMethodName = shimNames.NextShimMethodName(PropertyGetterEmitter.ShimBaseName(setterSymbol));
        MethodDeclarationSyntax rewrittenMethod = RewritePropertySetterBody(
            property,
            setterBodyNode,
            setterSymbol,
            typeState.TypeSymbol,
            typeState.SourceUnit.SemanticModel,
            decision.UsesDelegation ? shimType.AccessorPlan : null,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog);
        shimType.AddMethod(rewrittenMethod, shimMethodName);

        // Why the set accessor's own span: the getter entry of the same property takes the get
        // accessor's span, and overlapping spans leave compile-error attribution and pause-point
        // resolution unable to tell the two apart.
        FileLinePositionSpan span = setAccessor.GetLocation().GetLineSpan();
        entries.Add(new WorkerEntry
        {
            SourceProjectRelativePath = sourceProjectRelativePath,
            TypeMetadataName = typeMetadataName,
            MethodName = setterRowName,
            ParameterTypeFullNames = parameterTypeFullNames,
            GenericArity = setterSymbol.Arity,
            ShimTypeName = shimType.ShimTypeName,
            ShimMethodName = shimMethodName,
            PatchKind = decision.PatchKind,
            CalledAddedMethodKeys = AddedCallSiteGuard.CollectCalledAddedMethodKeys(
                setterBodyNode,
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

    // What: the transform decision for the setter body, or null after adding the skip row when the
    // decision, the name/added-call guards, or the re-applied sibling guard rejects it.
    private static MethodTransformDecision DecideOrSkip(
        TypeEmitState typeState,
        IMethodSymbol setterSymbol,
        SyntaxNode setterBodyNode,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        AddedPropertyCatalog addedPropertyCatalog,
        List<WorkerSkipped> skipped)
    {
        string sourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath;
        (bool skipSetter, MethodTransformDecision decision) = PropertyGetterClassifier.TrySkipPropertyAccessorByDecision(
            sourceProjectRelativePath,
            typeState.TypeDeclaration,
            typeState.TypeSymbol,
            setterSymbol,
            setterBodyNode,
            typeState.SourceUnit.SemanticModel,
            typeState.CompiledType,
            typeState.AddedEvents,
            addedMethodCatalog,
            addedFieldCatalog,
            addedPropertyCatalog,
            typeState.SourceUnit.PartialTypeParts,
            typeState.TargetAssembly,
            skipped);
        if (skipSetter)
        {
            return null;
        }

        // Why the same guard as a getter: a setter of a file pulled back in whose body no longer
        // binds would otherwise reach the shim and fail the run over code the reader never passed.
        if (!typeState.SourceUnit.Input.ReappliedSibling)
        {
            return decision;
        }

        WorkerReason siblingSkip = ReappliedSiblingBodyGuard.DescribeSkipOrNull(
            typeState.SourceUnit.SemanticModel,
            setterBodyNode,
            typeState.TargetAssembly,
            methodDeclarationOrNull: null,
            decision,
            typeState.TypeSymbol);
        if (siblingSkip == null)
        {
            return decision;
        }

        AddSkip(skipped, sourceProjectRelativePath, typeState.CompiledType, setterSymbol, siblingSkip);
        return null;
    }

    // What: whether the set accessor equals its snapshot peer. False when there is no baseline or
    // either side cannot be found, so an uncomparable setter is treated as changed (like a getter).
    private static bool IsSetterUnchangedFromBaseline(TypeEmitState typeState, PropertyDeclarationSyntax property)
    {
        BaselineSnapshotState baseline = typeState.SourceUnit.Baseline;
        if (!baseline.HasBaseline
            || baseline.SnapshotPropertyMap == null
            || baseline.PlainCurrentPropertyMap == null)
        {
            return false;
        }

        string propertyKey = WorkerSyntaxIndex.BuildSyntaxPropertyKey(typeState.TypeMetadataNameFromSyntax, property);
        if (!baseline.SnapshotPropertyMap.TryGetValue(propertyKey, out PropertyDeclarationSyntax snapshotProperty)
            || !baseline.PlainCurrentPropertyMap.TryGetValue(propertyKey, out PropertyDeclarationSyntax plainProperty))
        {
            return false;
        }

        AccessorDeclarationSyntax snapshotSetter = PropertySetterClassifier.FindBodiedSetOrInitAccessorOrNull(snapshotProperty);
        AccessorDeclarationSyntax plainSetter = PropertySetterClassifier.FindBodiedSetOrInitAccessorOrNull(plainProperty);
        return snapshotSetter != null
            && plainSetter != null
            && SyntaxFactory.AreEquivalent(snapshotSetter, plainSetter, topLevel: false);
    }

    // What: rewrite a setter body while it is still in the bound tree, then wrap it as a void shim
    // method whose `value` parameter the body's `value` binds to.
    private static MethodDeclarationSyntax RewritePropertySetterBody(
        PropertyDeclarationSyntax property,
        SyntaxNode setterBodyNode,
        IMethodSymbol setterSymbol,
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
        SyntaxNode rewrittenBody = rewriter.Visit(setterBodyNode);
        // Why transfer: Visit may rebuild ArrowExpressionClause nodes and drop #line annotations.
        rewrittenBody = PropertyGetterEmitter.TransferUloopLineAnnotations(setterBodyNode, rewrittenBody);

        MethodDeclarationSyntax method = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                SyntaxFactory.Identifier(setterSymbol.Name))
            .WithModifiers(
                SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(AddedPropertyEmitter.CreateSetterParameterList(((TypeSyntax)rewriter.Visit(property.Type)).WithoutTrivia()));

        if (rewrittenBody is BlockSyntax blockBody)
        {
            method = method.WithBody(blockBody);
        }
        else
        {
            ArrowExpressionClauseSyntax arrowBody = rewrittenBody as ArrowExpressionClauseSyntax;
            if (arrowBody == null)
            {
                // set => expr rewritten to a bare expression: wrap as arrow.
                arrowBody = (ArrowExpressionClauseSyntax)PropertyGetterEmitter.TransferUloopLineAnnotations(
                    setterBodyNode,
                    SyntaxFactory.ArrowExpressionClause((ExpressionSyntax)rewrittenBody));
            }

            method = method
                .WithExpressionBody(arrowBody)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        // ToShimMethod forces public static and injects the instance parameter for instance setters.
        return ShimMethodFactory.ToShimMethod(method, setterSymbol);
    }

    private static void AddSkip(
        List<WorkerSkipped> skipped,
        string sourceProjectRelativePath,
        INamedTypeSymbol compiledType,
        IMethodSymbol setterSymbol,
        WorkerReason reason)
    {
        skipped.Add(new WorkerSkipped
        {
            SourceProjectRelativePath = sourceProjectRelativePath,
            Method = WorkerMethodKeys.FormatRowLabel(compiledType, setterSymbol),
            Reason = reason
        });
    }
}
