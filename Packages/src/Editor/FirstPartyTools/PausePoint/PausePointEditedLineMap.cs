using System;
using System.Linq;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Maps lines between the last compiled source snapshot and the edited file on disk, so a
    /// --line the caller read from the editor can be resolved against the compiled PDB lines.
    /// Lines are matched by their trimmed text through a longest common subsequence, so the map
    /// is monotonic: a later edited line never maps to an earlier compiled line.
    /// </summary>
    internal sealed class PausePointEditedLineMap
    {
        // Bounds the LCS table (one int per cell) so a rewrite of a huge file cannot stall the
        // Editor; the caller falls back to compiled line numbers instead.
        internal const int MaxDiffCells = 4_000_000;

        private const string TrivialLineCharacters = "{}();";

        private readonly string[] _compiledLines;
        private readonly string[] _editedLines;
        // Indexed by 1-based line number; 0 means the line has no counterpart.
        private readonly int[] _editedToCompiled;
        private readonly int[] _compiledToEdited;

        internal int CompiledLineCount => _compiledLines.Length;
        internal int EditedLineCount => _editedLines.Length;
        internal bool IsIdentity { get; }

        private PausePointEditedLineMap(
            string[] compiledLines,
            string[] editedLines,
            int[] editedToCompiled,
            int[] compiledToEdited,
            bool isIdentity)
        {
            _compiledLines = compiledLines;
            _editedLines = editedLines;
            _editedToCompiled = editedToCompiled;
            _compiledToEdited = compiledToEdited;
            IsIdentity = isIdentity;
        }

        /// <summary>
        /// Builds the map, or returns null when the changed middle section is too large to diff.
        /// </summary>
        internal static PausePointEditedLineMap BuildOrNull(string compiledSource, string editedSource)
        {
            string[] compiledLines = DropTrailingEmptyElement(SourcePausePointSourceLineReader.SplitSourceLines(compiledSource));
            string[] editedLines = DropTrailingEmptyElement(SourcePausePointSourceLineReader.SplitSourceLines(editedSource));
            string[] compiledKeys = compiledLines.Select(line => line.Trim()).ToArray();
            string[] editedKeys = editedLines.Select(line => line.Trim()).ToArray();
            int compiledCount = compiledKeys.Length;
            int editedCount = editedKeys.Length;

            int prefix = CountCommonPrefix(compiledKeys, editedKeys);
            int suffix = CountCommonSuffix(compiledKeys, editedKeys, prefix);
            int middleCompiledCount = compiledCount - prefix - suffix;
            int middleEditedCount = editedCount - prefix - suffix;
            if ((long)middleCompiledCount * middleEditedCount > MaxDiffCells)
            {
                return null;
            }

            int[] editedToCompiled = new int[editedCount + 1];
            int[] compiledToEdited = new int[compiledCount + 1];
            for (int line = 1; line <= prefix; line++)
            {
                Pair(editedToCompiled, compiledToEdited, line, line);
            }

            PairMiddleByLongestCommonSubsequence(
                compiledKeys, editedKeys, prefix, middleCompiledCount, middleEditedCount, editedToCompiled, compiledToEdited);

            for (int offset = 0; offset < suffix; offset++)
            {
                Pair(editedToCompiled, compiledToEdited, compiledCount - offset, editedCount - offset);
            }

            bool isIdentity = compiledCount == editedCount && prefix == compiledCount;
            return new PausePointEditedLineMap(compiledLines, editedLines, editedToCompiled, compiledToEdited, isIdentity);
        }

        internal int ToCompiledLineOrZero(int editedLine)
        {
            return editedLine < 1 || editedLine > EditedLineCount ? 0 : _editedToCompiled[editedLine];
        }

        internal int ToEditedLineOrZero(int compiledLine)
        {
            return compiledLine < 1 || compiledLine > CompiledLineCount ? 0 : _compiledToEdited[compiledLine];
        }

        /// <summary>
        /// Returns the smallest mapped edited line at or after editedLine, or 0 when none follows.
        /// </summary>
        internal int NextMappedEditedLineOrZero(int editedLine)
        {
            for (int line = editedLine < 1 ? 1 : editedLine; line <= EditedLineCount; line++)
            {
                if (_editedToCompiled[line] != 0)
                {
                    return line;
                }
            }

            return 0;
        }

        /// <summary>
        /// Returns the smallest edited line in [fromInclusive, toExclusive) that is unmapped and
        /// holds code, or 0. Unmapped trivial lines are skipped because they carry no statement.
        /// </summary>
        internal int FirstUnmappedStatementLineOrZero(int fromInclusive, int toExclusive)
        {
            int lastLine = toExclusive - 1 < EditedLineCount ? toExclusive - 1 : EditedLineCount;
            for (int line = fromInclusive < 1 ? 1 : fromInclusive; line <= lastLine; line++)
            {
                if (_editedToCompiled[line] == 0 && !IsTrivialLine(_editedLines[line - 1]))
                {
                    return line;
                }
            }

            return 0;
        }

        /// <summary>
        /// Returns the trimmed text of an edited line, or empty when the line is out of range.
        /// </summary>
        internal string EditedLineTextOrEmpty(int editedLine)
        {
            return editedLine < 1 || editedLine > EditedLineCount ? string.Empty : _editedLines[editedLine - 1].Trim();
        }

        /// <summary>
        /// Returns the trimmed text of a compiled line, or empty when the line is out of range.
        /// </summary>
        internal string CompiledLineTextOrEmpty(int compiledLine)
        {
            return compiledLine < 1 || compiledLine > CompiledLineCount ? string.Empty : _compiledLines[compiledLine - 1].Trim();
        }

        /// <summary>
        /// A trivial line is blank, a line comment, or only braces, parentheses, and semicolons.
        /// Such lines match almost anywhere, so they never count as an uncompiled statement.
        /// </summary>
        internal static bool IsTrivialLine(string text)
        {
            string trimmed = text == null ? string.Empty : text.Trim();
            return trimmed.Length == 0
                || trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.All(character => TrivialLineCharacters.IndexOf(character) >= 0);
        }

        // SplitSourceLines leaves an empty element after a final newline ("a\nb\n" gives three
        // elements); dropping it keeps the counts equal to the physical line count.
        private static string[] DropTrailingEmptyElement(string[] lines)
        {
            if (lines.Length > 1 && lines[lines.Length - 1].Length == 0)
            {
                return lines.Take(lines.Length - 1).ToArray();
            }

            return lines;
        }

        private static int CountCommonPrefix(string[] compiledKeys, string[] editedKeys)
        {
            int limit = compiledKeys.Length < editedKeys.Length ? compiledKeys.Length : editedKeys.Length;
            int prefix = 0;
            while (prefix < limit && compiledKeys[prefix] == editedKeys[prefix])
            {
                prefix++;
            }

            return prefix;
        }

        // The suffix must not overlap the prefix on either side, or a line would be paired twice.
        private static int CountCommonSuffix(string[] compiledKeys, string[] editedKeys, int prefix)
        {
            int compiledCount = compiledKeys.Length;
            int editedCount = editedKeys.Length;
            int suffix = 0;
            while (suffix < compiledCount - prefix
                && suffix < editedCount - prefix
                && compiledKeys[compiledCount - 1 - suffix] == editedKeys[editedCount - 1 - suffix])
            {
                suffix++;
            }

            return suffix;
        }

        private static void PairMiddleByLongestCommonSubsequence(
            string[] compiledKeys,
            string[] editedKeys,
            int prefix,
            int middleCompiledCount,
            int middleEditedCount,
            int[] editedToCompiled,
            int[] compiledToEdited)
        {
            if (middleCompiledCount == 0 || middleEditedCount == 0)
            {
                return;
            }

            int[,] lengths = new int[middleCompiledCount + 1, middleEditedCount + 1];
            for (int i = 1; i <= middleCompiledCount; i++)
            {
                for (int j = 1; j <= middleEditedCount; j++)
                {
                    lengths[i, j] = compiledKeys[prefix + i - 1] == editedKeys[prefix + j - 1]
                        ? lengths[i - 1, j - 1] + 1
                        : System.Math.Max(lengths[i - 1, j], lengths[i, j - 1]);
                }
            }

            int compiledIndex = middleCompiledCount;
            int editedIndex = middleEditedCount;
            while (compiledIndex > 0 && editedIndex > 0)
            {
                if (compiledKeys[prefix + compiledIndex - 1] == editedKeys[prefix + editedIndex - 1])
                {
                    Pair(editedToCompiled, compiledToEdited, prefix + compiledIndex, prefix + editedIndex);
                    compiledIndex--;
                    editedIndex--;
                }
                else if (lengths[compiledIndex - 1, editedIndex] >= lengths[compiledIndex, editedIndex - 1])
                {
                    compiledIndex--;
                }
                else
                {
                    editedIndex--;
                }
            }
        }

        private static void Pair(int[] editedToCompiled, int[] compiledToEdited, int compiledLine, int editedLine)
        {
            editedToCompiled[editedLine] = compiledLine;
            compiledToEdited[compiledLine] = editedLine;
        }
    }
}
