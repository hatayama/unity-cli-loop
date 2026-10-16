package compilecheck

import (
	"unicode/utf8"
)

const (
	shortUnicodeEscapeDigits = 4 // \uXXXX
	longUnicodeEscapeDigits  = 8 // \UXXXXXXXX
	surrogateFirst           = 0xD800
	surrogateLast            = 0xDFFF
)

// decodeUnicodeEscape decodes the \uXXXX or \UXXXXXXXX escape at position, the way C# reads one in
// an identifier, and reports the character and the length of the whole escape. ok is false when
// there is no escape there, when a digit is missing or not hexadecimal, or when the value is a
// surrogate or beyond the last code point, which the compiler does not take as an identifier
// character either.
func decodeUnicodeEscape(content []byte, position int) (rune, int, bool) {
	if position+1 >= len(content) || content[position] != '\\' {
		return 0, 0, false
	}
	digits := 0
	switch content[position+1] {
	case 'u':
		digits = shortUnicodeEscapeDigits
	case 'U':
		digits = longUnicodeEscapeDigits
	default:
		return 0, 0, false
	}
	size := 2 + digits
	if position+size > len(content) {
		return 0, 0, false
	}

	// Why the value is gathered unsigned: eight digits from 8 up overflow a rune into a negative
	// value, which would slip past the upper bound.
	value := uint32(0)
	for _, digit := range content[position+2 : position+size] {
		nibble, isHex := hexValue(digit)
		if !isHex {
			return 0, 0, false
		}
		value = value<<4 | uint32(nibble)
	}
	if (value >= surrogateFirst && value <= surrogateLast) || value > utf8.MaxRune {
		return 0, 0, false
	}

	return rune(value), size, true
}

// hexValue is the value of one hexadecimal digit.
func hexValue(digit byte) (rune, bool) {
	switch {
	case digit >= '0' && digit <= '9':
		return rune(digit - '0'), true
	case digit >= 'a' && digit <= 'f':
		return rune(digit-'a') + 10, true
	case digit >= 'A' && digit <= 'F':
		return rune(digit-'A') + 10, true
	}

	return 0, false
}

// runeAt is the character at position and how many bytes it takes: a \u or \U escape decoded, and
// otherwise the UTF-8 character there.
func runeAt(content []byte, position int) (rune, int, bool) {
	// Why ASCII is answered first: nearly every byte of a source is ASCII and no escape, and the scan
	// asks for every one of them.
	if first := content[position]; first < utf8.RuneSelf && first != '\\' {
		return rune(first), 1, false
	}
	if character, size, ok := decodeUnicodeEscape(content, position); ok {
		return character, size, true
	}
	character, size := utf8.DecodeRune(content[position:])

	return character, size, false
}

// decodedText spells an identifier's text with its escapes decoded.
func decodedText(text []byte) []byte {
	decoded := make([]byte, 0, len(text))
	for position := 0; position < len(text); {
		character, size, _ := runeAt(text, position)
		decoded = utf8.AppendRune(decoded, character)
		position += size
	}

	return decoded
}
