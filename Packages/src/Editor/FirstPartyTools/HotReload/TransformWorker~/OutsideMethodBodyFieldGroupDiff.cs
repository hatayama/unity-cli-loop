using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Compares field and event-field declarations as whole declarations rather than one name at a
// time: several names can share one header, so an attribute, modifier or type edit belongs to the
// group while an initializer edit belongs to the single name that carries it.
internal static class OutsideMethodBodyFieldGroupDiff
{
    internal static void AppendBaseFieldChangeLabels(
        Dictionary<string, SyntaxNode> snapshotMap,
        Dictionary<string, SyntaxNode> currentMap,
        HashSet<string> handledSnapshotKeys,
        HashSet<string> handledCurrentKeys,
        OutsideMethodBodyDeclarationDiff.Result result,
        string kindNoun)
    {
        HashSet<BaseFieldDeclarationSyntax> processedParents =
            new HashSet<BaseFieldDeclarationSyntax>();
        foreach (KeyValuePair<string, SyntaxNode> pair in snapshotMap)
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
        Dictionary<string, SyntaxNode> snapshotMap,
        Dictionary<string, SyntaxNode> currentMap,
        HashSet<string> handledSnapshotKeys,
        HashSet<string> handledCurrentKeys,
        OutsideMethodBodyDeclarationDiff.Result result,
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

        VariableDeclaratorSyntax currentFirst = ReadDeclarator(currentMap, comparableKeys[0]);
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
        Dictionary<string, SyntaxNode> snapshotMap,
        Dictionary<string, SyntaxNode> currentMap,
        HashSet<string> handledSnapshotKeys,
        HashSet<string> handledCurrentKeys)
    {
        List<string> comparableKeys = new List<string>();
        foreach (KeyValuePair<string, SyntaxNode> pair in snapshotMap)
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
        Dictionary<string, SyntaxNode> currentMap,
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
        Dictionary<string, SyntaxNode> snapshotMap,
        bool attributesDiffer,
        bool modifiersDiffer,
        bool typeDiffers)
    {
        List<string> names = new List<string>();
        foreach (string key in comparableKeys)
        {
            names.Add(ReadDeclarator(snapshotMap, key).Identifier.Text);
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
        Dictionary<string, SyntaxNode> snapshotMap,
        Dictionary<string, SyntaxNode> currentMap,
        OutsideMethodBodyDeclarationDiff.Result result,
        string kindNoun)
    {
        foreach (string key in comparableKeys)
        {
            VariableDeclaratorSyntax snapshotVariable = ReadDeclarator(snapshotMap, key);
            VariableDeclaratorSyntax currentVariable = ReadDeclarator(currentMap, key);
            if (SyntaxFactory.AreEquivalent(snapshotVariable, currentVariable, topLevel: false))
            {
                continue;
            }

            result.ChangedLabels.Add(kindNoun + " initializer: " + snapshotVariable.Identifier.Text);
        }
    }

    // Both base-field maps are built from declarators, so a key present in one always names a
    // declarator; a different node here would mean the map builder and this file disagree.
    private static VariableDeclaratorSyntax ReadDeclarator(Dictionary<string, SyntaxNode> map, string key)
    {
        return (VariableDeclaratorSyntax)map[key];
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
}
