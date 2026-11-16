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
    // What: reports each property/indexer accessor that has an explicit body and that hot reload
    // does not patch as Skipped. Auto-properties ({ get; set; }) have no body and are not listed;
    // getters and the setters PropertySetterClassifier hands to the setter emitter are left out.
    // When a verified snapshot declares an equivalent property/indexer, skip rows are omitted
    // (unchanged accessors must not appear as Skipped noise). A property is also compared one
    // accessor at a time, so a set or init body equal to the snapshot's is omitted even when
    // another part of the property changed.
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
        AddedPropertyCatalog addedPropertyCatalog,
        INamedTypeSymbol compiledType,
        bool servedByRetainedArtifact)
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
                PropertyDeclarationSyntax snapshotProperty = null;
                PropertyDeclarationSyntax plainProperty = null;
                if (snapshotPropertyMap != null
                    && plainCurrentPropertyMap != null
                    && snapshotPropertyMap.TryGetValue(propertyKey, out snapshotProperty)
                    && plainCurrentPropertyMap.TryGetValue(propertyKey, out plainProperty)
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
                    addedMethodCatalog,
                    snapshotProperty,
                    plainProperty,
                    compiledType,
                    servedByRetainedArtifact);
                continue;
            }

            if (member is IndexerDeclarationSyntax indexerDeclaration)
            {
                AppendIndexerAccessorSkips(
                    indexerDeclaration,
                    semanticModel.GetDeclaredSymbol(indexerDeclaration),
                    skipped,
                    typeMetadataNameFromSyntax,
                    snapshotIndexerMap,
                    plainCurrentIndexerMap,
                    compiledType,
                    servedByRetainedArtifact);
            }
        }

        StampSourceProjectRelativePath(skipped, firstAppendedIndex, sourceProjectRelativePath);
    }

    // snapshotProperty and plainCurrentProperty are the snapshot's and the unannotated current
    // tree's declarations of this property, or null when either side has none; an accessor is
    // compared against the snapshot only when both are present.
    internal static void AppendExplicitAccessorSkipsForProperty(
        BasePropertyDeclarationSyntax propertyDeclaration,
        IPropertySymbol propertySymbol,
        List<WorkerSkipped> skipped,
        string typeMetadataNameFromSyntax,
        Dictionary<string, PropertyDeclarationSyntax> snapshotPropertyMap,
        AddedMethodCatalog addedMethodCatalog,
        PropertyDeclarationSyntax snapshotProperty,
        PropertyDeclarationSyntax plainCurrentProperty,
        INamedTypeSymbol compiledType,
        bool servedByRetainedArtifact)
    {
        if (propertySymbol == null)
        {
            return;
        }

        // Properties: getters and patchable setters are reported by the emitters; only init and
        // setters without a matching compiled setter are Skipped here.
        if (propertyDeclaration.AccessorList == null)
        {
            return;
        }

        bool emittedSkip = AppendChangedSetOrInitAccessorSkips(
            propertyDeclaration,
            propertySymbol,
            skipped,
            snapshotProperty,
            plainCurrentProperty,
            compiledType,
            servedByRetainedArtifact);

        PropertyDeclarationSyntax namedProperty = propertyDeclaration as PropertyDeclarationSyntax;
        // Why also for an emitter-reported setter: a property the snapshot does not declare has to be
        // marked as added to the current tree whichever path reports its setter, or the drift check
        // reads the whole property as an edit outside method bodies.
        bool setterReportedByEmitter = namedProperty != null
            && PropertySetterClassifier.IsPatchableCompiledSetter(
                namedProperty,
                propertySymbol,
                compiledType,
                servedByRetainedArtifact);
        if ((!emittedSkip && !setterReportedByEmitter)
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

    // Reports each set or init accessor that has a body, differs from the snapshot, and is not
    // reported by the setter emitter, and returns whether it reported any.
    private static bool AppendChangedSetOrInitAccessorSkips(
        BasePropertyDeclarationSyntax propertyDeclaration,
        IPropertySymbol propertySymbol,
        List<WorkerSkipped> skipped,
        PropertyDeclarationSyntax snapshotProperty,
        PropertyDeclarationSyntax plainCurrentProperty,
        INamedTypeSymbol compiledType,
        bool servedByRetainedArtifact)
    {
        bool emittedSkip = false;
        foreach (AccessorDeclarationSyntax accessor in propertyDeclaration.AccessorList.Accessors)
        {
            if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
            {
                continue;
            }

            // The setter emitter reports this accessor (entry, unchanged row, or its own skip).
            if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration)
                && propertyDeclaration is PropertyDeclarationSyntax namedProperty
                && PropertySetterClassifier.IsPatchableCompiledSetter(
                    namedProperty,
                    propertySymbol,
                    compiledType,
                    servedByRetainedArtifact))
            {
                continue;
            }

            // Auto-properties emit accessors with neither Body nor ExpressionBody.
            if (accessor.Body == null && accessor.ExpressionBody == null)
            {
                continue;
            }

            // Why per accessor: a getter-only edit used to skip the setter as well, because the
            // caller compared the whole property declaration.
            if (snapshotProperty != null
                && plainCurrentProperty != null
                && PropertyAccessorUnchanged(snapshotProperty, plainCurrentProperty, accessor.Kind()))
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

        return emittedSkip;
    }

    // Why match by kind: a set accessor that became init changed its declaration, so it finds no
    // peer of its kind in the snapshot and stays Skipped.
    private static bool PropertyAccessorUnchanged(
        PropertyDeclarationSyntax snapshotProperty,
        PropertyDeclarationSyntax currentProperty,
        SyntaxKind accessorKind)
    {
        AccessorDeclarationSyntax snapshotAccessor = FindPropertyAccessor(snapshotProperty, accessorKind);
        AccessorDeclarationSyntax currentAccessor = FindPropertyAccessor(currentProperty, accessorKind);
        return snapshotAccessor != null
            && currentAccessor != null
            && SyntaxFactory.AreEquivalent(snapshotAccessor, currentAccessor, topLevel: false);
    }

    private static AccessorDeclarationSyntax FindPropertyAccessor(
        PropertyDeclarationSyntax propertyDeclaration,
        SyntaxKind accessorKind)
    {
        if (propertyDeclaration.AccessorList == null)
        {
            return null;
        }

        foreach (AccessorDeclarationSyntax accessor in propertyDeclaration.AccessorList.Accessors)
        {
            if (accessor.Kind() == accessorKind)
            {
                return accessor;
            }
        }

        return null;
    }

    // What: a Skipped row for each bodied indexer accessor that changed since the verified snapshot
    // and that the indexer emitter does not report itself (no matching compiled accessor, an init
    // accessor, or a type a retained artifact serves).
    private static void AppendIndexerAccessorSkips(
        IndexerDeclarationSyntax indexerDeclaration,
        IPropertySymbol indexerSymbol,
        List<WorkerSkipped> skipped,
        string typeMetadataNameFromSyntax,
        Dictionary<string, IndexerDeclarationSyntax> snapshotIndexerMap,
        Dictionary<string, IndexerDeclarationSyntax> plainCurrentIndexerMap,
        INamedTypeSymbol compiledType,
        bool servedByRetainedArtifact)
    {
        if (indexerSymbol == null)
        {
            return;
        }

        foreach (IndexerBodiedAccessor accessor in IndexerAccessorRules.FindBodiedAccessors(indexerDeclaration))
        {
            if (IndexerAccessorRules.IsAccessorUnchangedFromBaseline(
                    typeMetadataNameFromSyntax,
                    indexerDeclaration,
                    accessor.Kind,
                    snapshotIndexerMap,
                    plainCurrentIndexerMap)
                || IndexerAccessorRules.IsReportedByEmitter(
                    indexerDeclaration,
                    indexerSymbol,
                    accessor.Kind,
                    compiledType,
                    servedByRetainedArtifact))
            {
                continue;
            }

            IMethodSymbol accessorMethod = ResolveAccessorMethodSymbol(indexerSymbol, accessor.Kind);
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
    // are omitted. Field-like events are not listed.
    internal static void AppendUnsupportedMemberKindSkips(
        string sourceProjectRelativePath,
        TypeDeclarationSyntax typeDeclaration,
        string typeMetadataNameFromSyntax,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped,
        Dictionary<string, ConstructorDeclarationSyntax> snapshotConstructorMap,
        Dictionary<string, DestructorDeclarationSyntax> snapshotDestructorMap,
        Dictionary<string, MemberDeclarationSyntax> snapshotOperatorMap,
        Dictionary<string, EventDeclarationSyntax> snapshotEventMap,
        Dictionary<string, ConstructorDeclarationSyntax> plainCurrentConstructorMap,
        Dictionary<string, DestructorDeclarationSyntax> plainCurrentDestructorMap,
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
        AppendFinalizerSkips(
            typeDeclaration,
            typeMetadataNameFromSyntax,
            semanticModel,
            skipped,
            snapshotDestructorMap,
            plainCurrentDestructorMap);
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

    // Why the same branches as AppendConstructorSkips: a finalizer row must appear under exactly
    // the conditions a constructor row does (edited, added, or no verified baseline).
    internal static void AppendFinalizerSkips(
        TypeDeclarationSyntax typeDeclaration,
        string typeMetadataNameFromSyntax,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped,
        Dictionary<string, DestructorDeclarationSyntax> snapshotDestructorMap,
        Dictionary<string, DestructorDeclarationSyntax> plainCurrentDestructorMap)
    {
        foreach (DestructorDeclarationSyntax destructorDeclaration in typeDeclaration.Members
            .OfType<DestructorDeclarationSyntax>())
        {
            string destructorKey = WorkerSyntaxIndex.BuildSyntaxDestructorKey(typeMetadataNameFromSyntax);
            if (snapshotDestructorMap != null
                && plainCurrentDestructorMap != null
                && snapshotDestructorMap.TryGetValue(
                    destructorKey,
                    out DestructorDeclarationSyntax snapshotDestructor)
                && plainCurrentDestructorMap.TryGetValue(
                    destructorKey,
                    out DestructorDeclarationSyntax plainDestructor)
                && SyntaxFactory.AreEquivalent(snapshotDestructor, plainDestructor, topLevel: false))
            {
                continue;
            }

            IMethodSymbol methodSymbol = semanticModel.GetDeclaredSymbol(destructorDeclaration);
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
