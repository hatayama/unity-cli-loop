using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Normalizes line endings in generated text skill files without changing their encoding.
    /// </summary>
    internal static class SkillFileContentNormalizer
    {
        private const byte Utf16LittleEndianBomFirstByte = 0xFF;
        private const byte Utf16LittleEndianBomSecondByte = 0xFE;
        private const byte Utf16BigEndianBomFirstByte = 0xFE;
        private const byte Utf16BigEndianBomSecondByte = 0xFF;
        private const int Utf16CodeUnitByteCount = 2;
        private const ushort CarriageReturnCodeUnit = 0x000D;
        private const ushort LineFeedCodeUnit = 0x000A;
        private static readonly HashSet<string> TextSkillFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".json",
            ".md",
            ".ps1",
            ".sh",
            ".txt",
            ".yaml",
            ".yml"
        };

        internal static byte[] NormalizeSkillFileContent(string relativePath, byte[] content)
        {
            Debug.Assert(!string.IsNullOrEmpty(relativePath), "relativePath must not be null or empty");
            Debug.Assert(content != null, "content must not be null");

            if (!ShouldNormalizeLineEndings(relativePath) || Array.IndexOf(content, (byte)'\r') < 0)
            {
                return content;
            }

            if (HasUtf16LittleEndianBom(content))
            {
                return NormalizeUtf16LineEndings(content, isLittleEndian: true);
            }

            if (HasUtf16BigEndianBom(content))
            {
                return NormalizeUtf16LineEndings(content, isLittleEndian: false);
            }

            if (HasUtf16LittleEndianLineEnding(content))
            {
                return NormalizeUtf16LineEndings(content, isLittleEndian: true);
            }

            if (HasUtf16BigEndianLineEnding(content))
            {
                return NormalizeUtf16LineEndings(content, isLittleEndian: false);
            }

            if (Array.IndexOf(content, (byte)0) >= 0)
            {
                return content;
            }

            return NormalizeSingleByteLineEndings(content);
        }

        private static bool ShouldNormalizeLineEndings(string relativePath)
        {
            string extension = Path.GetExtension(relativePath);
            return TextSkillFileExtensions.Contains(extension);
        }

        private static byte[] NormalizeSingleByteLineEndings(byte[] content)
        {
            List<byte> normalized = new(content.Length);
            for (int index = 0; index < content.Length; index++)
            {
                byte current = content[index];
                if (current != (byte)'\r')
                {
                    normalized.Add(current);
                    continue;
                }

                normalized.Add((byte)'\n');
                if (index + 1 < content.Length && content[index + 1] == (byte)'\n')
                {
                    index++;
                }
            }

            return normalized.ToArray();
        }

        private static byte[] NormalizeUtf16LineEndings(byte[] content, bool isLittleEndian)
        {
            List<byte> normalized = new(content.Length);
            int index = 0;
            if (HasMatchingUtf16Bom(content, isLittleEndian))
            {
                normalized.Add(content[0]);
                normalized.Add(content[1]);
                index = Utf16CodeUnitByteCount;
            }

            while (index + 1 < content.Length)
            {
                ushort codeUnit = ReadUtf16CodeUnit(content, index, isLittleEndian);
                if (codeUnit == CarriageReturnCodeUnit)
                {
                    WriteUtf16CodeUnit(normalized, LineFeedCodeUnit, isLittleEndian);
                    int nextIndex = index + Utf16CodeUnitByteCount;
                    if (nextIndex + 1 < content.Length &&
                        ReadUtf16CodeUnit(content, nextIndex, isLittleEndian) == LineFeedCodeUnit)
                    {
                        index += Utf16CodeUnitByteCount * 2;
                        continue;
                    }

                    index += Utf16CodeUnitByteCount;
                    continue;
                }

                WriteUtf16CodeUnit(normalized, codeUnit, isLittleEndian);
                index += Utf16CodeUnitByteCount;
            }

            if (index < content.Length)
            {
                normalized.Add(content[index]);
            }

            return normalized.ToArray();
        }

        private static bool HasUtf16LittleEndianBom(byte[] content)
        {
            return content.Length >= Utf16CodeUnitByteCount &&
                   content[0] == Utf16LittleEndianBomFirstByte &&
                   content[1] == Utf16LittleEndianBomSecondByte;
        }

        private static bool HasUtf16BigEndianBom(byte[] content)
        {
            return content.Length >= Utf16CodeUnitByteCount &&
                   content[0] == Utf16BigEndianBomFirstByte &&
                   content[1] == Utf16BigEndianBomSecondByte;
        }

        private static bool HasMatchingUtf16Bom(byte[] content, bool isLittleEndian)
        {
            return isLittleEndian ? HasUtf16LittleEndianBom(content) : HasUtf16BigEndianBom(content);
        }

        private static bool HasUtf16LittleEndianLineEnding(byte[] content)
        {
            return HasUtf16LineEnding(content, isLittleEndian: true);
        }

        private static bool HasUtf16BigEndianLineEnding(byte[] content)
        {
            return HasUtf16LineEnding(content, isLittleEndian: false);
        }

        private static bool HasUtf16LineEnding(byte[] content, bool isLittleEndian)
        {
            int startIndex = HasMatchingUtf16Bom(content, isLittleEndian) ? Utf16CodeUnitByteCount : 0;
            for (int index = startIndex; index + 1 < content.Length; index += Utf16CodeUnitByteCount)
            {
                ushort codeUnit = ReadUtf16CodeUnit(content, index, isLittleEndian);
                if (codeUnit == CarriageReturnCodeUnit || codeUnit == LineFeedCodeUnit)
                {
                    return true;
                }
            }

            return false;
        }

        private static ushort ReadUtf16CodeUnit(byte[] content, int index, bool isLittleEndian)
        {
            return isLittleEndian
                ? (ushort)(content[index] | (content[index + 1] << 8))
                : (ushort)((content[index] << 8) | content[index + 1]);
        }

        private static void WriteUtf16CodeUnit(List<byte> output, ushort codeUnit, bool isLittleEndian)
        {
            if (isLittleEndian)
            {
                output.Add((byte)(codeUnit & 0xFF));
                output.Add((byte)(codeUnit >> 8));
                return;
            }

            output.Add((byte)(codeUnit >> 8));
            output.Add((byte)(codeUnit & 0xFF));
        }
    }
}
