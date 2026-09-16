using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

// Decides whether the members both files declare appear in a different order in each of them.
// Order is read from the same member maps the declaration diff pairs with, so a key the diff
// paired is always found here too.
internal static class OutsideMethodBodyOrderDrift
{
    /// <summary>
    /// Reports whether the paired keys sit in a different declaration order in the two files.
    /// </summary>
    internal static bool PairedKeysAreReordered(
        HashSet<string> pairedKeys,
        IReadOnlyList<Dictionary<string, SyntaxNode>> snapshotMaps,
        IReadOnlyList<Dictionary<string, SyntaxNode>> currentMaps)
    {
        if (pairedKeys.Count < 2)
        {
            return false;
        }

        List<string> snapshotOrder = OrderKeysBySpanStart(pairedKeys, snapshotMaps);
        List<string> currentOrder = OrderKeysBySpanStart(pairedKeys, currentMaps);
        return !StringListsEqual(snapshotOrder, currentOrder);
    }

    private static List<string> OrderKeysBySpanStart(
        HashSet<string> keys,
        IReadOnlyList<Dictionary<string, SyntaxNode>> maps)
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

    private static int GetMappedNodeSpanStart(
        IReadOnlyList<Dictionary<string, SyntaxNode>> maps,
        string key)
    {
        SyntaxNode node = FindMappedNodeOrNull(maps, key);
        if (node == null)
        {
            return int.MaxValue;
        }

        return node.SpanStart;
    }

    private static SyntaxNode FindMappedNodeOrNull(
        IReadOnlyList<Dictionary<string, SyntaxNode>> maps,
        string key)
    {
        for (int index = 0; index < maps.Count; index++)
        {
            if (maps[index].ContainsKey(key))
            {
                return maps[index][key];
            }
        }

        return null;
    }

    private sealed class SyntaxKeySpanComparer : IComparer<string>
    {
        private readonly IReadOnlyList<Dictionary<string, SyntaxNode>> _maps;

        internal SyntaxKeySpanComparer(IReadOnlyList<Dictionary<string, SyntaxNode>> maps)
        {
            _maps = maps;
        }

        public int Compare(string left, string right)
        {
            return GetMappedNodeSpanStart(_maps, left).CompareTo(GetMappedNodeSpanStart(_maps, right));
        }
    }
}
