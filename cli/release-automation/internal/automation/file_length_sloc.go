package automation

import (
	"unicode"
	"unicode/utf8"
)

// SourceLanguage selects which comment and string rules CountSLOC applies.
type SourceLanguage int

const (
	// LanguageCSharp counts C# comments and string literals, including verbatim,
	// character, and interpolated forms.
	LanguageCSharp SourceLanguage = iota
	// LanguageGo counts Go comments, interpreted strings, and raw strings.
	LanguageGo
)

const utf8BOM = '\uFEFF'

// CountSLOC returns the number of source lines that are neither blank nor
// comment-only. Comment markers inside string literals are treated as code,
// which is why this counts with a lexer instead of line prefixes.
func CountSLOC(source []byte, language SourceLanguage) int {
	scanner := newSLOCScanner(source, language)
	return scanner.count()
}

type slocScanner struct {
	source      []byte
	offset      int
	language    SourceLanguage
	lineHasCode bool
	sloc        int
}

func newSLOCScanner(source []byte, language SourceLanguage) *slocScanner {
	scanner := &slocScanner{
		source:   source,
		language: language,
	}
	scanner.skipBOM()
	return scanner
}

func (s *slocScanner) count() int {
	s.scanCode(false)
	s.finishLine()
	return s.sloc
}

func (s *slocScanner) skipBOM() {
	if s.peekRune() == utf8BOM {
		s.nextRune()
	}
}

func (s *slocScanner) scanCode(stopOnUnmatchedBrace bool) {
	braceDepth := 0
	for !s.atEnd() {
		current := s.peekRune()
		if isNewline(current) {
			s.consumeNewline()
			continue
		}
		if isHorizontalWhitespace(current) {
			s.nextRune()
			continue
		}
		if s.tryComment() {
			continue
		}
		if stopOnUnmatchedBrace && s.handleInterpolationBrace(current, &braceDepth) {
			return
		}
		if s.tryString() {
			continue
		}
		s.markCode()
		s.nextRune()
	}
}

func (s *slocScanner) handleInterpolationBrace(current rune, braceDepth *int) bool {
	if current != '{' && current != '}' {
		return false
	}
	s.markCode()
	s.nextRune()
	if current == '{' {
		*braceDepth++
		return false
	}
	if *braceDepth == 0 {
		return true
	}
	*braceDepth--
	return false
}

func (s *slocScanner) tryComment() bool {
	if s.peekRune() != '/' {
		return false
	}
	next := s.peekRuneAt(1)
	if next == '/' {
		s.skipLineComment()
		return true
	}
	if next == '*' {
		s.skipBlockComment()
		return true
	}
	return false
}

func (s *slocScanner) skipLineComment() {
	for !s.atEnd() && !isNewline(s.peekRune()) {
		s.nextRune()
	}
}

func (s *slocScanner) skipBlockComment() {
	s.nextRune()
	s.nextRune()
	for !s.atEnd() {
		if isNewline(s.peekRune()) {
			s.consumeNewline()
			continue
		}
		if s.peekRune() == '*' && s.peekRuneAt(1) == '/' {
			s.nextRune()
			s.nextRune()
			return
		}
		s.nextRune()
	}
}

func (s *slocScanner) tryString() bool {
	if s.language == LanguageGo {
		return s.tryGoString()
	}
	return s.tryCSharpString()
}

func (s *slocScanner) tryGoString() bool {
	current := s.peekRune()
	if current == '"' {
		s.markCode()
		s.nextRune()
		s.scanEscapedQuotedString('"')
		return true
	}
	if current == '`' {
		s.markCode()
		s.nextRune()
		s.scanGoRawString()
		return true
	}
	return false
}

func (s *slocScanner) tryCSharpString() bool {
	if s.hasPrefix("$@\"") || s.hasPrefix("@$\"") {
		s.markCode()
		s.skipRunes(3)
		s.scanCSharpInterpolatedString(true)
		return true
	}
	if s.hasPrefix("$\"") {
		s.markCode()
		s.skipRunes(2)
		s.scanCSharpInterpolatedString(false)
		return true
	}
	if s.hasPrefix("@\"") {
		s.markCode()
		s.skipRunes(2)
		s.scanCSharpVerbatimString()
		return true
	}
	current := s.peekRune()
	if current == '"' || current == '\'' {
		s.markCode()
		s.nextRune()
		s.scanEscapedQuotedString(current)
		return true
	}
	return false
}

func (s *slocScanner) scanEscapedQuotedString(quote rune) {
	for !s.atEnd() {
		current := s.peekRune()
		if isNewline(current) {
			s.consumeNewline()
			continue
		}
		if current == '\\' {
			s.consumeEscapedRune()
			continue
		}
		if current == quote {
			s.markCode()
			s.nextRune()
			return
		}
		s.consumeStringRune(current)
	}
}

func (s *slocScanner) scanCSharpVerbatimString() {
	for !s.atEnd() {
		current := s.peekRune()
		if isNewline(current) {
			s.consumeNewline()
			continue
		}
		if current == '"' {
			s.markCode()
			s.nextRune()
			if s.peekRune() == '"' {
				s.nextRune()
				continue
			}
			return
		}
		s.consumeStringRune(current)
	}
}

func (s *slocScanner) scanGoRawString() {
	for !s.atEnd() {
		current := s.peekRune()
		if isNewline(current) {
			s.consumeNewline()
			continue
		}
		if current == '`' {
			s.markCode()
			s.nextRune()
			return
		}
		s.consumeStringRune(current)
	}
}

func (s *slocScanner) scanCSharpInterpolatedString(verbatim bool) {
	for !s.atEnd() {
		current := s.peekRune()
		if isNewline(current) {
			s.consumeNewline()
			continue
		}
		if current == '{' {
			s.consumeInterpolationHole()
			continue
		}
		if current == '}' && s.peekRuneAt(1) == '}' {
			s.markCode()
			s.nextRune()
			s.nextRune()
			continue
		}
		if !verbatim && current == '\\' {
			s.consumeEscapedRune()
			continue
		}
		if s.consumeInterpolatedQuote(current, verbatim) {
			return
		}
		s.consumeStringRune(current)
	}
}

func (s *slocScanner) consumeInterpolatedQuote(current rune, verbatim bool) bool {
	if current != '"' {
		return false
	}
	if verbatim && s.peekRuneAt(1) == '"' {
		s.markCode()
		s.nextRune()
		s.nextRune()
		return false
	}
	s.markCode()
	s.nextRune()
	return true
}

func (s *slocScanner) consumeInterpolationHole() {
	if s.peekRuneAt(1) == '{' {
		s.markCode()
		s.nextRune()
		s.nextRune()
		return
	}
	s.markCode()
	s.nextRune()
	s.scanCode(true)
}

func (s *slocScanner) consumeEscapedRune() {
	s.markCode()
	s.nextRune()
	if s.atEnd() || isNewline(s.peekRune()) {
		return
	}
	s.nextRune()
}

func (s *slocScanner) consumeStringRune(current rune) {
	if !isHorizontalWhitespace(current) {
		s.markCode()
	}
	s.nextRune()
}

func (s *slocScanner) consumeNewline() {
	if s.peekRune() == '\r' {
		s.nextRune()
		if s.peekRune() == '\n' {
			s.nextRune()
		}
	} else {
		s.nextRune()
	}
	s.finishLine()
}

func (s *slocScanner) finishLine() {
	if !s.lineHasCode {
		return
	}
	s.sloc++
	s.lineHasCode = false
}

func (s *slocScanner) markCode() {
	s.lineHasCode = true
}

func (s *slocScanner) atEnd() bool {
	return s.offset >= len(s.source)
}

func (s *slocScanner) peekRune() rune {
	return s.peekRuneAt(0)
}

func (s *slocScanner) peekRuneAt(runeOffset int) rune {
	offset := s.offset
	remaining := runeOffset
	for remaining >= 0 {
		if offset >= len(s.source) {
			return 0
		}
		value, width := utf8.DecodeRune(s.source[offset:])
		if remaining == 0 {
			return value
		}
		offset += width
		remaining--
	}
	return 0
}

func (s *slocScanner) nextRune() rune {
	if s.atEnd() {
		return 0
	}
	value, width := utf8.DecodeRune(s.source[s.offset:])
	s.offset += width
	return value
}

func (s *slocScanner) skipRunes(count int) {
	for index := 0; index < count; index++ {
		s.nextRune()
	}
}

func (s *slocScanner) hasPrefix(prefix string) bool {
	offset := s.offset
	for _, expected := range prefix {
		if offset >= len(s.source) {
			return false
		}
		value, width := utf8.DecodeRune(s.source[offset:])
		if value != expected {
			return false
		}
		offset += width
	}
	return true
}

func isNewline(value rune) bool {
	return value == '\n' || value == '\r'
}

func isHorizontalWhitespace(value rune) bool {
	if isNewline(value) {
		return false
	}
	return unicode.IsSpace(value)
}
