package cli

import (
	"bytes"
	"path/filepath"
	"strings"
)

func normalizeSkillFileContent(relativePath string, content []byte) []byte {
	if !shouldNormalizeLineEndings(relativePath) || !bytes.Contains(content, []byte{'\r'}) {
		return content
	}

	if hasUTF16LittleEndianBOM(content) || hasUTF16LittleEndianLineEnding(content) {
		return normalizeUTF16LineEndings(content, true)
	}

	if hasUTF16BigEndianBOM(content) || hasUTF16BigEndianLineEnding(content) {
		return normalizeUTF16LineEndings(content, false)
	}

	if bytes.Contains(content, []byte{0}) {
		return content
	}

	normalizedContent := bytes.ReplaceAll(content, []byte("\r\n"), []byte("\n"))
	return bytes.ReplaceAll(normalizedContent, []byte("\r"), []byte("\n"))
}

func normalizeUTF16LineEndings(content []byte, littleEndian bool) []byte {
	normalized := make([]byte, 0, len(content))
	index := 0
	if hasMatchingUTF16BOM(content, littleEndian) {
		normalized = append(normalized, content[0], content[1])
		index = utf16CodeUnitByteCount
	}

	for index+1 < len(content) {
		codeUnit := readUTF16CodeUnit(content, index, littleEndian)
		if codeUnit == carriageReturnCodeUnit {
			normalized = writeUTF16CodeUnit(normalized, lineFeedCodeUnit, littleEndian)
			nextIndex := index + utf16CodeUnitByteCount
			if nextIndex+1 < len(content) &&
				readUTF16CodeUnit(content, nextIndex, littleEndian) == lineFeedCodeUnit {
				index += utf16CodeUnitByteCount * 2
				continue
			}

			index += utf16CodeUnitByteCount
			continue
		}

		normalized = writeUTF16CodeUnit(normalized, codeUnit, littleEndian)
		index += utf16CodeUnitByteCount
	}

	if index < len(content) {
		normalized = append(normalized, content[index])
	}

	return normalized
}

func hasUTF16LittleEndianBOM(content []byte) bool {
	return len(content) >= utf16CodeUnitByteCount &&
		content[0] == utf16LittleEndianBOMFirstByte &&
		content[1] == utf16LittleEndianBOMSecondByte
}

func hasUTF16BigEndianBOM(content []byte) bool {
	return len(content) >= utf16CodeUnitByteCount &&
		content[0] == utf16BigEndianBOMFirstByte &&
		content[1] == utf16BigEndianBOMSecondByte
}

func hasMatchingUTF16BOM(content []byte, littleEndian bool) bool {
	if littleEndian {
		return hasUTF16LittleEndianBOM(content)
	}
	return hasUTF16BigEndianBOM(content)
}

func hasUTF16LittleEndianLineEnding(content []byte) bool {
	return hasUTF16LineEnding(content, true)
}

func hasUTF16BigEndianLineEnding(content []byte) bool {
	return hasUTF16LineEnding(content, false)
}

func hasUTF16LineEnding(content []byte, littleEndian bool) bool {
	startIndex := 0
	if hasMatchingUTF16BOM(content, littleEndian) {
		startIndex = utf16CodeUnitByteCount
	}
	for index := startIndex; index+1 < len(content); index += utf16CodeUnitByteCount {
		codeUnit := readUTF16CodeUnit(content, index, littleEndian)
		if codeUnit == carriageReturnCodeUnit || codeUnit == lineFeedCodeUnit {
			return true
		}
	}
	return false
}

func readUTF16CodeUnit(content []byte, index int, littleEndian bool) uint16 {
	if littleEndian {
		return uint16(content[index]) | uint16(content[index+1])<<8
	}
	return uint16(content[index])<<8 | uint16(content[index+1])
}

func writeUTF16CodeUnit(output []byte, codeUnit uint16, littleEndian bool) []byte {
	if littleEndian {
		return append(output, byte(codeUnit&0xff), byte(codeUnit>>8))
	}
	return append(output, byte(codeUnit>>8), byte(codeUnit&0xff))
}

func shouldNormalizeLineEndings(relativePath string) bool {
	switch strings.ToLower(filepath.Ext(relativePath)) {
	case ".json", ".md", ".ps1", ".sh", ".txt", ".yaml", ".yml":
		return true
	default:
		return false
	}
}
