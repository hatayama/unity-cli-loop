using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class OutsideMethodBodyDeclarationDiff
{
    internal sealed class Result
    {
        internal bool DuplicateKeys;
        internal bool OrderDrift;
        internal readonly List<string> ChangedLabels = new List<string>();
        internal readonly HashSet<string> PairedSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);
    }

    // One member kind, with the declarations both files hold for it. Subclasses differ in what a
    // pair means: most kinds compare one declaration against one declaration, while fields and
    // event fields compare whole declarations because several names can share one header.
    private abstract class MemberMapEntry
    {
        internal readonly Dictionary<string, SyntaxNode> SnapshotMap;
        internal readonly Dictionary<string, SyntaxNode> CurrentMap;

        internal MemberMapEntry(
            Dictionary<string, SyntaxNode> snapshotMap,
            Dictionary<string, SyntaxNode> currentMap)
        {
            SnapshotMap = snapshotMap;
            CurrentMap = currentMap;
        }

        internal abstract void AppendChangeLabels(
            HashSet<string> handledSnapshotKeys,
            HashSet<string> handledCurrentKeys,
            Result result);
    }

    private sealed class SingleNodeMemberMapEntry : MemberMapEntry
    {
        private readonly Func<SyntaxNode, string> _describeLabel;

        internal SingleNodeMemberMapEntry(
            Dictionary<string, SyntaxNode> snapshotMap,
            Dictionary<string, SyntaxNode> currentMap,
            Func<SyntaxNode, string> describeLabel)
            : base(snapshotMap, currentMap)
        {
            _describeLabel = describeLabel;
        }

        internal override void AppendChangeLabels(
            HashSet<string> handledSnapshotKeys,
            HashSet<string> handledCurrentKeys,
            Result result)
        {
            AppendPairedSyntaxNodeLabels(
                SnapshotMap,
                CurrentMap,
                handledSnapshotKeys,
                handledCurrentKeys,
                result,
                _describeLabel);
        }
    }

    private sealed class BaseFieldMemberMapEntry : MemberMapEntry
    {
        private readonly string _kindNoun;

        internal BaseFieldMemberMapEntry(
            Dictionary<string, SyntaxNode> snapshotMap,
            Dictionary<string, SyntaxNode> currentMap,
            string kindNoun)
            : base(snapshotMap, currentMap)
        {
            _kindNoun = kindNoun;
        }

        internal override void AppendChangeLabels(
            HashSet<string> handledSnapshotKeys,
            HashSet<string> handledCurrentKeys,
            Result result)
        {
            OutsideMethodBodyFieldGroupDiff.AppendBaseFieldChangeLabels(
                SnapshotMap,
                CurrentMap,
                handledSnapshotKeys,
                handledCurrentKeys,
                result,
                _kindNoun);
        }
    }

    /// <summary>
    /// Pairs declarations by syntax key and records changed labels plus intersection keys
    /// so the residual tree compare can peel members that already have a named warning.
    /// </summary>
    internal static Result Diff(
        CompilationUnitSyntax snapshotRoot,
        CompilationUnitSyntax currentRoot,
        HashSet<string> handledSnapshotKeys,
        HashSet<string> handledCurrentKeys)
    {
        Result result = new Result();
        List<MemberMapEntry> entries = TryBuildMemberMapEntriesOrNull(snapshotRoot, currentRoot);
        if (entries == null)
        {
            result.DuplicateKeys = true;
            return result;
        }

        foreach (MemberMapEntry entry in entries)
        {
            entry.AppendChangeLabels(handledSnapshotKeys, handledCurrentKeys, result);
        }

        result.OrderDrift = OutsideMethodBodyOrderDrift.PairedKeysAreReordered(
            result.PairedSyntaxKeys,
            CollectSnapshotMaps(entries),
            CollectCurrentMaps(entries));
        return result;
    }

    // The kinds in the order they are compared, which is also the order a key is looked up in
    // when the order-drift check asks where a member sits.
    private static List<MemberMapEntry> TryBuildMemberMapEntriesOrNull(
        CompilationUnitSyntax snapshotRoot,
        CompilationUnitSyntax currentRoot)
    {
        List<MemberMapEntry> entries = new List<MemberMapEntry>();
        entries.Add(new BaseFieldMemberMapEntry(
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxFieldMapOrNull(snapshotRoot)),
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxFieldMapOrNull(currentRoot)),
            "field"));
        entries.Add(new BaseFieldMemberMapEntry(
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxEventFieldMapOrNull(snapshotRoot)),
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxEventFieldMapOrNull(currentRoot)),
            "event"));
        entries.Add(new SingleNodeMemberMapEntry(
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxMethodMapOrNull(snapshotRoot)),
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxMethodMapOrNull(currentRoot)),
            FormatMethodLabel));
        entries.Add(new SingleNodeMemberMapEntry(
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxPropertyMapOrNull(snapshotRoot)),
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxPropertyMapOrNull(currentRoot)),
            FormatPropertyLabel));
        entries.Add(new SingleNodeMemberMapEntry(
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxIndexerMapOrNull(snapshotRoot)),
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxIndexerMapOrNull(currentRoot)),
            FormatIndexerLabel));
        entries.Add(new SingleNodeMemberMapEntry(
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxConstructorMapOrNull(snapshotRoot)),
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxConstructorMapOrNull(currentRoot)),
            FormatConstructorLabel));
        entries.Add(new SingleNodeMemberMapEntry(
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxOperatorMapOrNull(snapshotRoot)),
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxOperatorMapOrNull(currentRoot)),
            FormatOperatorLabel));
        entries.Add(new SingleNodeMemberMapEntry(
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxEventMapOrNull(snapshotRoot)),
            RaiseToSyntaxNodes(WorkerSyntaxMemberMaps.BuildSyntaxEventMapOrNull(currentRoot)),
            FormatEventLabel));
        foreach (MemberMapEntry entry in entries)
        {
            // A null map means a duplicate key made that kind unusable for pairing.
            if (entry.SnapshotMap == null || entry.CurrentMap == null)
            {
                return null;
            }
        }

        return entries;
    }

    private static List<Dictionary<string, SyntaxNode>> CollectSnapshotMaps(List<MemberMapEntry> entries)
    {
        List<Dictionary<string, SyntaxNode>> maps = new List<Dictionary<string, SyntaxNode>>();
        foreach (MemberMapEntry entry in entries)
        {
            maps.Add(entry.SnapshotMap);
        }

        return maps;
    }

    private static List<Dictionary<string, SyntaxNode>> CollectCurrentMaps(List<MemberMapEntry> entries)
    {
        List<Dictionary<string, SyntaxNode>> maps = new List<Dictionary<string, SyntaxNode>>();
        foreach (MemberMapEntry entry in entries)
        {
            maps.Add(entry.CurrentMap);
        }

        return maps;
    }

    // The maps arrive typed per kind, but pairing and ordering only ever read them as nodes, and a
    // dictionary of a derived value type cannot be read as one of the base type.
    private static Dictionary<string, SyntaxNode> RaiseToSyntaxNodes<TNode>(Dictionary<string, TNode> map)
        where TNode : SyntaxNode
    {
        if (map == null)
        {
            return null;
        }

        Dictionary<string, SyntaxNode> raised =
            new Dictionary<string, SyntaxNode>(map.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, TNode> pair in map)
        {
            raised[pair.Key] = pair.Value;
        }

        return raised;
    }

    private static void AppendPairedSyntaxNodeLabels(
        Dictionary<string, SyntaxNode> snapshotMap,
        Dictionary<string, SyntaxNode> currentMap,
        HashSet<string> handledSnapshotKeys,
        HashSet<string> handledCurrentKeys,
        Result result,
        Func<SyntaxNode, string> formatLabel)
    {
        foreach (KeyValuePair<string, SyntaxNode> pair in snapshotMap)
        {
            if (!currentMap.ContainsKey(pair.Key))
            {
                continue;
            }

            // Why not pair handled keys: return-type replacements strip the current
            // declaration always and the snapshot only when the rest of the signature
            // matches. Peeling both via intersection would hide attribute drift.
            if (handledSnapshotKeys.Contains(pair.Key) || handledCurrentKeys.Contains(pair.Key))
            {
                continue;
            }

            SyntaxNode currentNode = currentMap[pair.Key];
            result.PairedSyntaxKeys.Add(pair.Key);

            if (SyntaxFactory.AreEquivalent(pair.Value, currentNode, topLevel: false))
            {
                continue;
            }

            result.ChangedLabels.Add(formatLabel(pair.Value));
        }
    }

    private static string FormatMethodLabel(SyntaxNode node)
    {
        MethodDeclarationSyntax method = (MethodDeclarationSyntax)node;
        string name = method.Identifier.Text;
        if (method.ExplicitInterfaceSpecifier != null)
        {
            name = method.ExplicitInterfaceSpecifier.Name.NormalizeWhitespace().ToString()
                + "." + name;
        }

        return "method: " + name;
    }

    private static string FormatPropertyLabel(SyntaxNode node)
    {
        PropertyDeclarationSyntax property = (PropertyDeclarationSyntax)node;
        return "property: " + property.Identifier.Text;
    }

    private static string FormatIndexerLabel(SyntaxNode node)
    {
        return "indexer: this";
    }

    private static string FormatConstructorLabel(SyntaxNode node)
    {
        ConstructorDeclarationSyntax constructor = (ConstructorDeclarationSyntax)node;
        string name = constructor.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : ".ctor";
        return "constructor: " + name;
    }

    private static string FormatOperatorLabel(SyntaxNode node)
    {
        if (node is OperatorDeclarationSyntax operatorDeclaration)
        {
            return "operator: " + operatorDeclaration.OperatorToken.ValueText;
        }

        ConversionOperatorDeclarationSyntax conversion =
            (ConversionOperatorDeclarationSyntax)node;
        string targetType = conversion.Type != null
            ? conversion.Type.NormalizeWhitespace().ToString()
            : string.Empty;
        return "conversion: " + conversion.ImplicitOrExplicitKeyword.ValueText + "->" + targetType;
    }

    private static string FormatEventLabel(SyntaxNode node)
    {
        EventDeclarationSyntax eventDeclaration = (EventDeclarationSyntax)node;
        return "event: " + eventDeclaration.Identifier.Text;
    }
}
