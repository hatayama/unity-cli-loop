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

internal static class UnsupportedMemberSkipCollector
{
    // What: reports each property/indexer accessor that has an explicit body as Skipped.
    // Auto-properties ({ get; set; }) have no body and are not listed.
    // When a verified snapshot declares an equivalent property/indexer, skip rows are omitted
    // (unchanged accessors must not appear as Skipped noise).
    internal static void AppendExplicitAccessorSkips(
        string sourceProjectRelativePath,
        TypeDeclarationSyntax typeDeclaration,
        string typeMetadataNameFromSyntax,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped,
        Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
        Dictionary<string, IndexerDeclarationSyntax> snapshotIndexerMap,
        Dictionary<string, PropertyDeclarationSyntax> plainCurrentPropertyMap,
        Dictionary<string, IndexerDeclarationSyntax> plainCurrentIndexerMap,
        AddedMethodCatalog addedMethodCatalog,
        AddedPropertyCatalog addedPropertyCatalog)
    {
        int firstAppendedIndex = skipped.Count;
        foreach (MemberDeclarationSyntax member in typeDeclaration.Members)
        {
            if (member is PropertyDeclarationSyntax propertyDeclaration)
            {
                IPropertySymbol propertySymbol = semanticModel.GetDeclaredSymbol(propertyDeclaration);
                if (addedPropertyCatalog.FindBySymbolOrNull(propertySymbol) != null)
                {
                    continue;
                }

                string propertyKey = WorkerSyntaxIndex.BuildSyntaxPropertyKey(typeMetadataNameFromSyntax, propertyDeclaration);
                // Why plainCurrentPropertyMap: annotated property nodes break AreEquivalent the
                // same way annotated method bodies do; compare unannotated peers only.
                if (snapshotPropertyMap != null
                    && plainCurrentPropertyMap != null
                    && snapshotPropertyMap.TryGetValue(
                        propertyKey,
                        out PropertyDeclarationSyntax snapshotProperty)
                    && plainCurrentPropertyMap.TryGetValue(
                        propertyKey,
                        out PropertyDeclarationSyntax plainProperty)
                    && SyntaxFactory.AreEquivalent(snapshotProperty, plainProperty, topLevel: false))
                {
                    continue;
                }

                AppendExplicitAccessorSkipsForProperty(
                    propertyDeclaration,
                    propertySymbol,
                    skipped,
                    typeMetadataNameFromSyntax,
                    snapshotPropertyMap,
                    addedMethodCatalog);
                continue;
            }

            if (member is IndexerDeclarationSyntax indexerDeclaration)
            {
                string indexerKey = WorkerSyntaxIndex.BuildSyntaxIndexerKey(typeMetadataNameFromSyntax, indexerDeclaration);
                if (snapshotIndexerMap != null
                    && plainCurrentIndexerMap != null
                    && snapshotIndexerMap.TryGetValue(
                        indexerKey,
                        out IndexerDeclarationSyntax snapshotIndexer)
                    && plainCurrentIndexerMap.TryGetValue(
                        indexerKey,
                        out IndexerDeclarationSyntax plainIndexer)
                    && SyntaxFactory.AreEquivalent(snapshotIndexer, plainIndexer, topLevel: false))
                {
                    continue;
                }

                AppendExplicitAccessorSkipsForProperty(
                    indexerDeclaration,
                    semanticModel.GetDeclaredSymbol(indexerDeclaration),
                    skipped,
                    typeMetadataNameFromSyntax,
                    snapshotPropertyMap,
                    addedMethodCatalog);
            }
        }

        StampSourceProjectRelativePath(skipped, firstAppendedIndex, sourceProjectRelativePath);
    }

    internal static void AppendExplicitAccessorSkipsForProperty(
        BasePropertyDeclarationSyntax propertyDeclaration,
        IPropertySymbol propertySymbol,
        List<WorkerSkipped> skipped,
        string typeMetadataNameFromSyntax,
        Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
        AddedMethodCatalog addedMethodCatalog)
    {
        if (propertySymbol == null)
        {
            return;
        }

        // Indexers: keep reporting every explicit-body accessor (including expression-bodied).
        if (propertyDeclaration is IndexerDeclarationSyntax indexerDeclaration)
        {
            AppendIndexerExplicitAccessorSkips(indexerDeclaration, propertySymbol, skipped);
            return;
        }

        // Properties: getters are patched elsewhere; only setter/init with bodies are Skipped here.
        if (propertyDeclaration.AccessorList == null)
        {
            return;
        }

        bool emittedSkip = false;
        foreach (AccessorDeclarationSyntax accessor in propertyDeclaration.AccessorList.Accessors)
        {
            if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
            {
                continue;
            }

            // Auto-properties emit accessors with neither Body nor ExpressionBody.
            if (accessor.Body == null && accessor.ExpressionBody == null)
            {
                continue;
            }

            IMethodSymbol accessorMethod = ResolveAccessorMethodSymbol(propertySymbol, accessor.Kind());
            if (accessorMethod == null)
            {
                continue;
            }

            skipped.Add(new WorkerSkipped
            {
                Method = WorkerMethodKeys.FormatMethodLabel(accessorMethod),
                Reason = WorkerReason.Of(HotReloadWorkerReasonCode.UnsupportedMemberExplicitAccessor)
            });
            emittedSkip = true;
        }

        PropertyDeclarationSyntax namedProperty = propertyDeclaration as PropertyDeclarationSyntax;
        if (!emittedSkip
            || namedProperty == null
            || snapshotPropertyMap == null
            || addedMethodCatalog == null)
        {
            return;
        }

        string propertyKey = WorkerSyntaxIndex.BuildSyntaxPropertyKey(typeMetadataNameFromSyntax, namedProperty);
        if (!snapshotPropertyMap.ContainsKey(propertyKey))
        {
            addedMethodCatalog.AddAddedPropertySyntaxKey(propertyKey);
        }
    }

    internal static void AppendIndexerExplicitAccessorSkips(
        IndexerDeclarationSyntax indexerDeclaration,
        IPropertySymbol propertySymbol,
        List<WorkerSkipped> skipped)
    {
        if (indexerDeclaration.ExpressionBody != null)
        {
            if (propertySymbol.GetMethod != null)
            {
                skipped.Add(new WorkerSkipped
                {
                    Method = WorkerMethodKeys.FormatMethodLabel(propertySymbol.GetMethod),
                    Reason = WorkerReason.Of(HotReloadWorkerReasonCode.UnsupportedMemberExplicitAccessor)
                });
            }

            return;
        }

        if (indexerDeclaration.AccessorList == null)
        {
            return;
        }

        foreach (AccessorDeclarationSyntax accessor in indexerDeclaration.AccessorList.Accessors)
        {
            if (accessor.Body == null && accessor.ExpressionBody == null)
            {
                continue;
            }

            IMethodSymbol accessorMethod = ResolveAccessorMethodSymbol(propertySymbol, accessor.Kind());
            if (accessorMethod == null)
            {
                continue;
            }

            skipped.Add(new WorkerSkipped
            {
                Method = WorkerMethodKeys.FormatMethodLabel(accessorMethod),
                Reason = WorkerReason.Of(HotReloadWorkerReasonCode.UnsupportedMemberExplicitAccessor)
            });
        }
    }

    internal static IMethodSymbol ResolveAccessorMethodSymbol(
        IPropertySymbol propertySymbol,
        SyntaxKind accessorKind)
    {
        if (accessorKind == SyntaxKind.GetAccessorDeclaration)
        {
            return propertySymbol.GetMethod;
        }

        if (accessorKind == SyntaxKind.SetAccessorDeclaration
            || accessorKind == SyntaxKind.InitAccessorDeclaration)
        {
            return propertySymbol.SetMethod;
        }

        return null;
    }

    // What: reports instance/static constructors, operators, conversion operators, and
    // explicit event accessors as Skipped. Unchanged members matching a verified snapshot
    // are omitted. Field-like events and finalizers are not listed.
    internal static void AppendUnsupportedMemberKindSkips(
        string sourceProjectRelativePath,
        TypeDeclarationSyntax typeDeclaration,
        string typeMetadataNameFromSyntax,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped,
        Dictionary<string, ConstructorDeclarationSyntax> snapshotConstructorMap,
        Dictionary<string, MemberDeclarationSyntax> snapshotOperatorMap,
        Dictionary<string, EventDeclarationSyntax> snapshotEventMap,
        Dictionary<string, ConstructorDeclarationSyntax> plainCurrentConstructorMap,
        Dictionary<string, MemberDeclarationSyntax> plainCurrentOperatorMap,
        Dictionary<string, EventDeclarationSyntax> plainCurrentEventMap,
        bool servedByRetainedArtifact)
    {
        // A type a retained artifact serves runs the constructors, operators and event accessors
        // the artifact was built from, and hot reload only ever applies body edits to such a type,
        // so none of them can have changed. Reporting them would tell the reader to compile a
        // member they never touched, on every reload of the file.
        if (servedByRetainedArtifact)
        {
            return;
        }

        int firstAppendedIndex = skipped.Count;
        AppendConstructorSkips(
            typeDeclaration,
            typeMetadataNameFromSyntax,
            semanticModel,
            skipped,
            snapshotConstructorMap,
            plainCurrentConstructorMap);
        AppendOperatorSkips(
            typeDeclaration,
            typeMetadataNameFromSyntax,
            semanticModel,
            skipped,
            snapshotOperatorMap,
            plainCurrentOperatorMap);
        EventAccessorSkipCollector.AppendEventAccessorSkips(
            typeDeclaration,
            typeMetadataNameFromSyntax,
            semanticModel,
            skipped,
            snapshotEventMap,
            plainCurrentEventMap);
        StampSourceProjectRelativePath(skipped, firstAppendedIndex, sourceProjectRelativePath);
    }

    // Why stamp the appended range (not each producer): every row below belongs to the one type
    // this call was made for, so the rows a call appends all came from that type's file. The
    // group-wide rows of other files sit before firstAppendedIndex and are left alone.
    private static void StampSourceProjectRelativePath(
        List<WorkerSkipped> skipped,
        int firstAppendedIndex,
        string sourceProjectRelativePath)
    {
        for (int index = firstAppendedIndex; index < skipped.Count; index++)
        {
            skipped[index].SourceProjectRelativePath = sourceProjectRelativePath;
        }
    }

    internal static void AppendConstructorSkips(
        TypeDeclarationSyntax typeDeclaration,
        string typeMetadataNameFromSyntax,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped,
        Dictionary<string, ConstructorDeclarationSyntax> snapshotConstructorMap,
        Dictionary<string, ConstructorDeclarationSyntax> plainCurrentConstructorMap)
    {
        foreach (ConstructorDeclarationSyntax constructorDeclaration in typeDeclaration.Members
            .OfType<ConstructorDeclarationSyntax>())
        {
            string constructorKey = WorkerSyntaxIndex.BuildSyntaxConstructorKey(
                typeMetadataNameFromSyntax,
                constructorDeclaration);
            if (snapshotConstructorMap != null
                && plainCurrentConstructorMap != null
                && snapshotConstructorMap.TryGetValue(
                    constructorKey,
                    out ConstructorDeclarationSyntax snapshotConstructor)
                && plainCurrentConstructorMap.TryGetValue(
                    constructorKey,
                    out ConstructorDeclarationSyntax plainConstructor)
                && SyntaxFactory.AreEquivalent(snapshotConstructor, plainConstructor, topLevel: false))
            {
                continue;
            }

            IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(constructorDeclaration);
            AppendUnsupportedKindSkip(skipped, methodSymbol);
        }
    }

    internal static void AppendOperatorSkips(
        TypeDeclarationSyntax typeDeclaration,
        string typeMetadataNameFromSyntax,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped,
        Dictionary<string, MemberDeclarationSyntax> snapshotOperatorMap,
        Dictionary<string, MemberDeclarationSyntax> plainCurrentOperatorMap)
    {
        foreach (MemberDeclarationSyntax member in typeDeclaration.Members)
        {
            string operatorKey = WorkerSyntaxIndex.TryBuildSyntaxOperatorMemberKey(typeMetadataNameFromSyntax, member);
            if (operatorKey == null)
            {
                continue;
            }

            if (snapshotOperatorMap != null
                && plainCurrentOperatorMap != null
                && snapshotOperatorMap.TryGetValue(operatorKey, out MemberDeclarationSyntax snapshotOperator)
                && plainCurrentOperatorMap.TryGetValue(operatorKey, out MemberDeclarationSyntax plainOperator)
                && SyntaxFactory.AreEquivalent(snapshotOperator, plainOperator, topLevel: false))
            {
                continue;
            }

            IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(member) as IMethodSymbol;
            AppendUnsupportedKindSkip(skipped, methodSymbol);
        }
    }

    internal static void AppendUnsupportedKindSkip(List<WorkerSkipped> skipped, IMethodSymbol methodSymbol)
    {
        if (methodSymbol == null)
        {
            return;
        }

        skipped.Add(new WorkerSkipped
        {
            Method = WorkerMethodKeys.FormatMethodLabel(methodSymbol),
            Reason = WorkerReason.Of(HotReloadWorkerReasonCode.UnsupportedMemberUnsupportedKind)
        });
    }
}
