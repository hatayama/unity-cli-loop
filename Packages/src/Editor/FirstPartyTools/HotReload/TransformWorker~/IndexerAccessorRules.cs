using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// What: one bodied accessor of an indexer declaration. SpanNode is the syntax whose lines the
// accessor's entry covers; BodyNode is the block, arrow clause, or expression body that is patched.
internal sealed class IndexerBodiedAccessor
{
    internal IndexerBodiedAccessor(SyntaxKind kind, SyntaxNode spanNode, SyntaxNode bodyNode)
    {
        Kind = kind;
        SpanNode = spanNode;
        BodyNode = bodyNode;
    }

    internal SyntaxKind Kind { get; }

    internal SyntaxNode SpanNode { get; }

    internal SyntaxNode BodyNode { get; }
}

// What: decides which indexer accessors the indexer emitter reports and which the skip collector
// reports, so every bodied indexer accessor gets exactly one row, and compares one accessor with its
// snapshot peer so an edit to one accessor does not re-report the other.
internal static class IndexerAccessorRules
{
    // What: the bodied get, set, and init accessors of the indexer; an expression-bodied indexer
    // yields its getter.
    internal static List<IndexerBodiedAccessor> FindBodiedAccessors(IndexerDeclarationSyntax indexerDeclaration)
    {
        List<IndexerBodiedAccessor> accessors = new List<IndexerBodiedAccessor>();
        if (indexerDeclaration.ExpressionBody != null)
        {
            accessors.Add(new IndexerBodiedAccessor(
                SyntaxKind.GetAccessorDeclaration,
                indexerDeclaration,
                indexerDeclaration.ExpressionBody));
            return accessors;
        }

        if (indexerDeclaration.AccessorList == null)
        {
            return accessors;
        }

        foreach (AccessorDeclarationSyntax accessor in indexerDeclaration.AccessorList.Accessors)
        {
            SyntaxNode body = (SyntaxNode)accessor.Body ?? accessor.ExpressionBody;
            if (body != null)
            {
                accessors.Add(new IndexerBodiedAccessor(accessor.Kind(), accessor, body));
            }
        }

        return accessors;
    }

    // What: whether the indexer emitter reports this accessor itself (an entry, an unchanged record,
    // or a skip with the reason an ordinary method would get) rather than the skip collector.
    // Why a matching compiled accessor is required: an entry for an accessor the compiled type lacks
    // fails the Editor's signature lookup and turns the run Failed, while the collector's Skipped
    // row keeps pointing the reader at 'uloop compile'.
    // Why not a retained artifact's type: the fingerprint comparison that serves those types has no
    // account of indexer accessors, and the baseline this emitter reads is not theirs.
    internal static bool IsReportedByEmitter(
        IndexerDeclarationSyntax indexerDeclaration,
        IPropertySymbol indexerSymbol,
        SyntaxKind accessorKind,
        INamedTypeSymbol compiledType,
        bool servedByRetainedArtifact)
    {
        if (indexerSymbol == null || compiledType == null || servedByRetainedArtifact)
        {
            return false;
        }

        // Why the emitter takes explicit interface indexers without a peer: metadata does not
        // report them as indexers, and the emitter gives them the explicit-interface reason an
        // explicit interface method gets.
        if (indexerDeclaration.ExplicitInterfaceSpecifier != null)
        {
            return true;
        }

        IPropertySymbol compiledIndexer = FindCompiledPeerOrNull(compiledType, indexerSymbol);
        if (compiledIndexer == null)
        {
            return false;
        }

        if (accessorKind == SyntaxKind.GetAccessorDeclaration)
        {
            return compiledIndexer.GetMethod != null;
        }

        // Why an init accessor on either side does not match: a set body patched into an init-only
        // method, or the reverse, changes who may call it.
        return accessorKind == SyntaxKind.SetAccessorDeclaration
            && compiledIndexer.SetMethod != null
            && !compiledIndexer.SetMethod.IsInitOnly;
    }

    // What: the compiled indexer with the same metadata name, index parameter types (in order), and
    // element type as the source indexer, or null. Why not by name alone: every overload shares the
    // name, so the index parameters are what tell overloads apart. Why the name too: the entry
    // carries the source accessor name, so an [IndexerName] added, removed, or changed since the
    // compile would name a method the Editor cannot find. Why the element type: the Editor's lookup
    // ignores return types, so a re-typed getter would be patched into the compiled one.
    internal static IPropertySymbol FindCompiledPeerOrNull(INamedTypeSymbol compiledType, IPropertySymbol sourceIndexer)
    {
        string[] sourceParameterTypes = ToParameterTypeFullNames(sourceIndexer);
        string sourceElementType = CecilTypeNames.ToCecilFullName(sourceIndexer.Type);
        return compiledType.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(candidate => candidate.IsIndexer
                && candidate.MetadataName == sourceIndexer.MetadataName
                && candidate.IsStatic == sourceIndexer.IsStatic
                && CecilTypeNames.ToCecilFullName(candidate.Type) == sourceElementType
                && ToParameterTypeFullNames(candidate).SequenceEqual(sourceParameterTypes));
    }

    // What: whether the accessor of this kind equals its peer in the verified snapshot. False when
    // there is no baseline or either side lacks the accessor, so an uncomparable accessor is
    // treated as changed.
    internal static bool IsAccessorUnchangedFromBaseline(
        string typeMetadataNameFromSyntax,
        IndexerDeclarationSyntax indexerDeclaration,
        SyntaxKind accessorKind,
        Dictionary<string, IndexerDeclarationSyntax> snapshotIndexerMap,
        Dictionary<string, IndexerDeclarationSyntax> plainCurrentIndexerMap)
    {
        if (snapshotIndexerMap == null || plainCurrentIndexerMap == null)
        {
            return false;
        }

        string indexerKey = WorkerSyntaxIndex.BuildSyntaxIndexerKey(typeMetadataNameFromSyntax, indexerDeclaration);
        if (!snapshotIndexerMap.TryGetValue(indexerKey, out IndexerDeclarationSyntax snapshotIndexer)
            || !plainCurrentIndexerMap.TryGetValue(indexerKey, out IndexerDeclarationSyntax plainIndexer))
        {
            return false;
        }

        // Why the declaration header is compared too: a changed parameter list or element type
        // makes the same body bind to another signature.
        if (!SyntaxFactory.AreEquivalent(
                StripAccessors(snapshotIndexer),
                StripAccessors(plainIndexer),
                topLevel: false))
        {
            return false;
        }

        SyntaxNode snapshotBody = FindBodyOrNull(snapshotIndexer, accessorKind);
        SyntaxNode plainBody = FindBodyOrNull(plainIndexer, accessorKind);
        return snapshotBody != null
            && plainBody != null
            && SyntaxFactory.AreEquivalent(snapshotBody, plainBody, topLevel: false);
    }

    internal static string[] ToParameterTypeFullNames(IPropertySymbol indexer)
    {
        return indexer.Parameters.Select(CecilTypeNames.ToParameterTypeFullName).ToArray();
    }

    private static SyntaxNode FindBodyOrNull(IndexerDeclarationSyntax indexerDeclaration, SyntaxKind accessorKind)
    {
        foreach (IndexerBodiedAccessor accessor in FindBodiedAccessors(indexerDeclaration))
        {
            if (accessor.Kind == accessorKind)
            {
                return accessor.SpanNode;
            }
        }

        return null;
    }

    private static IndexerDeclarationSyntax StripAccessors(IndexerDeclarationSyntax indexerDeclaration)
    {
        return indexerDeclaration
            .WithAccessorList(null)
            .WithExpressionBody(null)
            .WithSemicolonToken(default(SyntaxToken));
    }
}
