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

internal static class OutsideMethodBodyDeclarationDiff
{
    internal sealed class Result
    {
        internal bool DuplicateKeys;
        internal bool OrderDrift;
        internal readonly List<string> ChangedLabels = new List<string>();
        internal readonly HashSet<string> PairedSyntaxKeys = new HashSet<string>(StringComparer.Ordinal);
    }

    private sealed class MemberMaps
    {
        internal Dictionary<string, MethodDeclarationSyntax> Methods;
        internal Dictionary<string, VariableDeclaratorSyntax> Fields;
        internal Dictionary<string, PropertyDeclarationSyntax> Properties;
        internal Dictionary<string, IndexerDeclarationSyntax> Indexers;
        internal Dictionary<string, ConstructorDeclarationSyntax> Constructors;
        internal Dictionary<string, MemberDeclarationSyntax> Operators;
        internal Dictionary<string, EventDeclarationSyntax> Events;
        internal Dictionary<string, VariableDeclaratorSyntax> EventFields;
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
        MemberMaps snapshotMaps = TryBuildMemberMaps(snapshotRoot);
        MemberMaps currentMaps = TryBuildMemberMaps(currentRoot);
        Result result = new Result();
        if (snapshotMaps == null || currentMaps == null)
        {
            result.DuplicateKeys = true;
            return result;
        }

        AppendBaseFieldChangeLabels(
            snapshotMaps.Fields,
            currentMaps.Fields,
            handledSnapshotKeys,
            handledCurrentKeys,
            result,
            "field");
        AppendBaseFieldChangeLabels(
            snapshotMaps.EventFields,
            currentMaps.EventFields,
            handledSnapshotKeys,
            handledCurrentKeys,
            result,
            "event");
        AppendPairedSyntaxNodeLabels(
            snapshotMaps.Methods,
            currentMaps.Methods,
            handledSnapshotKeys,
            handledCurrentKeys,
            result,
            FormatMethodLabel);
        AppendPairedSyntaxNodeLabels(
            snapshotMaps.Properties,
            currentMaps.Properties,
            handledSnapshotKeys,
            handledCurrentKeys,
            result,
            FormatPropertyLabel);
        AppendPairedSyntaxNodeLabels(
            snapshotMaps.Indexers,
            currentMaps.Indexers,
            handledSnapshotKeys,
            handledCurrentKeys,
            result,
            FormatIndexerLabel);
        AppendPairedSyntaxNodeLabels(
            snapshotMaps.Constructors,
            currentMaps.Constructors,
            handledSnapshotKeys,
            handledCurrentKeys,
            result,
            FormatConstructorLabel);
        AppendPairedSyntaxNodeLabels(
            snapshotMaps.Operators,
            currentMaps.Operators,
            handledSnapshotKeys,
            handledCurrentKeys,
            result,
            FormatOperatorLabel);
        AppendPairedSyntaxNodeLabels(
            snapshotMaps.Events,
            currentMaps.Events,
            handledSnapshotKeys,
            handledCurrentKeys,
            result,
            FormatEventLabel);
        MarkOrderDriftIfPairedKeysReordered(snapshotMaps, currentMaps, result);
        return result;
    }

    private static MemberMaps TryBuildMemberMaps(CompilationUnitSyntax root)
    {
        MemberMaps maps = new MemberMaps();
        maps.Methods = WorkerSyntaxIndex.BuildSyntaxMethodMapOrNull(root);
        maps.Fields = WorkerSyntaxIndex.BuildSyntaxFieldMapOrNull(root);
        maps.Properties = WorkerSyntaxIndex.BuildSyntaxPropertyMapOrNull(root);
        maps.Indexers = WorkerSyntaxIndex.BuildSyntaxIndexerMapOrNull(root);
        maps.Constructors = WorkerSyntaxIndex.BuildSyntaxConstructorMapOrNull(root);
        maps.Operators = WorkerSyntaxIndex.BuildSyntaxOperatorMapOrNull(root);
        maps.Events = WorkerSyntaxIndex.BuildSyntaxEventMapOrNull(root);
        maps.EventFields = WorkerSyntaxIndex.BuildSyntaxEventFieldMapOrNull(root);
        if (maps.Methods == null
            || maps.Fields == null
            || maps.Properties == null
            || maps.Indexers == null
            || maps.Constructors == null
            || maps.Operators == null
            || maps.Events == null
            || maps.EventFields == null)
        {
            return null;
        }

        return maps;
    }

    private static void AppendPairedSyntaxNodeLabels<TNode>(
        Dictionary<string, TNode> snapshotMap,
        Dictionary<string, TNode> currentMap,
        HashSet<string> handledSnapshotKeys,
        HashSet<string> handledCurrentKeys,
        Result result,
        Func<TNode, string> formatLabel)
        where TNode : SyntaxNode
    {
        foreach (KeyValuePair<string, TNode> pair in snapshotMap)
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

            TNode currentNode = currentMap[pair.Key];
            result.PairedSyntaxKeys.Add(pair.Key);

            if (SyntaxFactory.AreEquivalent(pair.Value, currentNode, topLevel: false))
            {
                continue;
            }

            result.ChangedLabels.Add(formatLabel(pair.Value));
        }
    }

    private static void AppendBaseFieldChangeLabels(
        Dictionary<string, VariableDeclaratorSyntax> snapshotMap,
        Dictionary<string, VariableDeclaratorSyntax> currentMap,
        HashSet<string> handledSnapshotKeys,
        HashSet<string> handledCurrentKeys,
        Result result,
        string kindNoun)
    {
        HashSet<BaseFieldDeclarationSyntax> processedParents =
            new HashSet<BaseFieldDeclarationSyntax>();
        foreach (KeyValuePair<string, VariableDeclaratorSyntax> pair in snapshotMap)
        {
            if (!currentMap.ContainsKey(pair.Key))
            {
                continue;
            }

            if (handledSnapshotKeys.Contains(pair.Key) || handledCurrentKeys.Contains(pair.Key))
            {
                continue;
            }

            BaseFieldDeclarationSyntax snapshotParent =
                pair.Value.Parent?.Parent as BaseFieldDeclarationSyntax;
            if (snapshotParent == null || !processedParents.Add(snapshotParent))
            {
                continue;
            }

            AppendLabelsForBaseFieldDeclaration(
                snapshotParent,
                snapshotMap,
                currentMap,
                handledSnapshotKeys,
                handledCurrentKeys,
                result,
                kindNoun);
        }
    }

    private static void AppendLabelsForBaseFieldDeclaration(
        BaseFieldDeclarationSyntax snapshotParent,
        Dictionary<string, VariableDeclaratorSyntax> snapshotMap,
        Dictionary<string, VariableDeclaratorSyntax> currentMap,
        HashSet<string> handledSnapshotKeys,
        HashSet<string> handledCurrentKeys,
        Result result,
        string kindNoun)
    {
        List<string> comparableKeys = CollectComparableSiblingKeys(
            snapshotParent,
            snapshotMap,
            currentMap,
            handledSnapshotKeys,
            handledCurrentKeys);
        if (comparableKeys.Count == 0)
        {
            return;
        }

        VariableDeclaratorSyntax currentFirst = currentMap[comparableKeys[0]];
        BaseFieldDeclarationSyntax currentParent =
            currentFirst.Parent?.Parent as BaseFieldDeclarationSyntax;
        if (currentParent == null)
        {
            return;
        }

        // Why fail-open: splitting a multi-declarator across declarations makes the shared
        // header ambiguous. Pairing here would strip siblings whose own header was never
        // compared and hide the residual warning.
        if (!CurrentDeclaratorsShareParent(comparableKeys, currentMap, currentParent))
        {
            return;
        }

        foreach (string key in comparableKeys)
        {
            result.PairedSyntaxKeys.Add(key);
        }

        bool attributesDiffer = AttributeListsDiffer(
            snapshotParent.AttributeLists,
            currentParent.AttributeLists);
        bool modifiersDiffer = TokenListsDiffer(snapshotParent.Modifiers, currentParent.Modifiers);
        bool typeDiffers = !SyntaxFactory.AreEquivalent(
            snapshotParent.Declaration.Type,
            currentParent.Declaration.Type,
            topLevel: false);
        if (attributesDiffer || modifiersDiffer || typeDiffers)
        {
            result.ChangedLabels.Add(
                FormatSharedHeaderLabel(
                    kindNoun,
                    comparableKeys,
                    snapshotMap,
                    attributesDiffer,
                    modifiersDiffer,
                    typeDiffers));
        }

        AppendInitializerLabels(
            comparableKeys,
            snapshotMap,
            currentMap,
            result,
            kindNoun);
    }

    private static List<string> CollectComparableSiblingKeys(
        BaseFieldDeclarationSyntax snapshotParent,
        Dictionary<string, VariableDeclaratorSyntax> snapshotMap,
        Dictionary<string, VariableDeclaratorSyntax> currentMap,
        HashSet<string> handledSnapshotKeys,
        HashSet<string> handledCurrentKeys)
    {
        List<string> comparableKeys = new List<string>();
        foreach (KeyValuePair<string, VariableDeclaratorSyntax> pair in snapshotMap)
        {
            if (pair.Value.Parent?.Parent != snapshotParent)
            {
                continue;
            }

            if (!currentMap.ContainsKey(pair.Key))
            {
                continue;
            }

            if (handledSnapshotKeys.Contains(pair.Key) || handledCurrentKeys.Contains(pair.Key))
            {
                continue;
            }

            comparableKeys.Add(pair.Key);
        }

        return comparableKeys;
    }

    private static bool CurrentDeclaratorsShareParent(
        List<string> comparableKeys,
        Dictionary<string, VariableDeclaratorSyntax> currentMap,
        BaseFieldDeclarationSyntax currentParent)
    {
        foreach (string key in comparableKeys)
        {
            if (currentMap[key].Parent?.Parent != currentParent)
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatSharedHeaderLabel(
        string kindNoun,
        List<string> comparableKeys,
        Dictionary<string, VariableDeclaratorSyntax> snapshotMap,
        bool attributesDiffer,
        bool modifiersDiffer,
        bool typeDiffers)
    {
        List<string> names = new List<string>();
        foreach (string key in comparableKeys)
        {
            names.Add(snapshotMap[key].Identifier.Text);
        }

        names.Sort(StringComparer.Ordinal);
        string joinedNames = string.Join(", ", names);
        if (attributesDiffer && !modifiersDiffer && !typeDiffers)
        {
            return kindNoun + " attributes: " + joinedNames;
        }

        return kindNoun + ": " + joinedNames;
    }

    private static void AppendInitializerLabels(
        List<string> comparableKeys,
        Dictionary<string, VariableDeclaratorSyntax> snapshotMap,
        Dictionary<string, VariableDeclaratorSyntax> currentMap,
        Result result,
        string kindNoun)
    {
        foreach (string key in comparableKeys)
        {
            VariableDeclaratorSyntax snapshotVariable = snapshotMap[key];
            VariableDeclaratorSyntax currentVariable = currentMap[key];
            if (SyntaxFactory.AreEquivalent(snapshotVariable, currentVariable, topLevel: false))
            {
                continue;
            }

            result.ChangedLabels.Add(kindNoun + " initializer: " + snapshotVariable.Identifier.Text);
        }
    }

    private static bool AttributeListsDiffer(
        SyntaxList<AttributeListSyntax> left,
        SyntaxList<AttributeListSyntax> right)
    {
        if (left.Count != right.Count)
        {
            return true;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!SyntaxFactory.AreEquivalent(left[index], right[index], topLevel: false))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TokenListsDiffer(SyntaxTokenList left, SyntaxTokenList right)
    {
        if (left.Count != right.Count)
        {
            return true;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!SyntaxFactory.AreEquivalent(left[index], right[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatMethodLabel(MethodDeclarationSyntax method)
    {
        string name = method.Identifier.Text;
        if (method.ExplicitInterfaceSpecifier != null)
        {
            name = method.ExplicitInterfaceSpecifier.Name.NormalizeWhitespace().ToString()
                + "." + name;
        }

        return "method: " + name;
    }

    private static string FormatPropertyLabel(PropertyDeclarationSyntax property)
    {
        return "property: " + property.Identifier.Text;
    }

    private static string FormatIndexerLabel(IndexerDeclarationSyntax indexer)
    {
        return "indexer: this";
    }

    private static string FormatConstructorLabel(ConstructorDeclarationSyntax constructor)
    {
        string name = constructor.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : ".ctor";
        return "constructor: " + name;
    }

    private static string FormatOperatorLabel(MemberDeclarationSyntax member)
    {
        if (member is OperatorDeclarationSyntax operatorDeclaration)
        {
            return "operator: " + operatorDeclaration.OperatorToken.ValueText;
        }

        ConversionOperatorDeclarationSyntax conversion =
            (ConversionOperatorDeclarationSyntax)member;
        string targetType = conversion.Type != null
            ? conversion.Type.NormalizeWhitespace().ToString()
            : string.Empty;
        return "conversion: " + conversion.ImplicitOrExplicitKeyword.ValueText + "->" + targetType;
    }

    private static string FormatEventLabel(EventDeclarationSyntax eventDeclaration)
    {
        return "event: " + eventDeclaration.Identifier.Text;
    }

    private static void MarkOrderDriftIfPairedKeysReordered(
        MemberMaps snapshotMaps,
        MemberMaps currentMaps,
        Result result)
    {
        if (result.PairedSyntaxKeys.Count < 2)
        {
            return;
        }

        List<string> snapshotOrder = OrderKeysBySpanStart(result.PairedSyntaxKeys, snapshotMaps);
        List<string> currentOrder = OrderKeysBySpanStart(result.PairedSyntaxKeys, currentMaps);
        result.OrderDrift = !StringListsEqual(snapshotOrder, currentOrder);
    }

    private static List<string> OrderKeysBySpanStart(HashSet<string> keys, MemberMaps maps)
    {
        List<string> ordered = new List<string>(keys);
        ordered.Sort(new SyntaxKeySpanComparer(maps));
        return ordered;
    }

    private static bool StringListsEqual(List<string> left, List<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetMappedNodeSpanStart(MemberMaps maps, string key)
    {
        SyntaxNode node = FindMappedNodeOrNull(maps, key);
        if (node == null)
        {
            return int.MaxValue;
        }

        return node.SpanStart;
    }

    private static SyntaxNode FindMappedNodeOrNull(MemberMaps maps, string key)
    {
        if (maps.Fields.ContainsKey(key))
        {
            return maps.Fields[key];
        }

        if (maps.EventFields.ContainsKey(key))
        {
            return maps.EventFields[key];
        }

        if (maps.Methods.ContainsKey(key))
        {
            return maps.Methods[key];
        }

        if (maps.Properties.ContainsKey(key))
        {
            return maps.Properties[key];
        }

        if (maps.Indexers.ContainsKey(key))
        {
            return maps.Indexers[key];
        }

        if (maps.Constructors.ContainsKey(key))
        {
            return maps.Constructors[key];
        }

        if (maps.Operators.ContainsKey(key))
        {
            return maps.Operators[key];
        }

        if (maps.Events.ContainsKey(key))
        {
            return maps.Events[key];
        }

        return null;
    }

    private sealed class SyntaxKeySpanComparer : IComparer<string>
    {
        private readonly MemberMaps _maps;

        internal SyntaxKeySpanComparer(MemberMaps maps)
        {
            _maps = maps;
        }

        public int Compare(string left, string right)
        {
            return GetMappedNodeSpanStart(_maps, left).CompareTo(GetMappedNodeSpanStart(_maps, right));
        }
    }
}
