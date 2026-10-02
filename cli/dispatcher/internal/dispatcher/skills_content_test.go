package dispatcher

import (
	"bytes"
	"testing"
)

func TestNormalizeSkillFileContentHandlesUTF16AndBinaryVariants(t *testing.T) {
	// Verifies CR line endings are normalized per encoding while binary-looking content is left untouched.
	cases := []struct {
		name    string
		content []byte
		want    []byte
	}{
		{name: "UTF-16 BE without BOM", content: []byte("\x00a\x00\r\x00\n\x00b"), want: []byte("\x00a\x00\n\x00b")},
		{name: "UTF-16 BE with BOM", content: []byte("\xfe\xff\x00a\x00\r\x00\n"), want: []byte("\xfe\xff\x00a\x00\n")},
		{name: "UTF-16 LE lone CR", content: []byte("a\x00\r\x00b\x00"), want: []byte("a\x00\n\x00b\x00")},
		{name: "UTF-16 LE odd trailing byte", content: []byte("a\x00\r\x00\n\x00z"), want: []byte("a\x00\n\x00z")},
		{name: "NUL without UTF-16 line endings", content: []byte("a\r\x00b"), want: []byte("a\r\x00b")},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got := normalizeSkillFileContent("SKILL.md", testCase.content)
			if !bytes.Equal(got, testCase.want) {
				t.Fatalf("normalized content mismatch: got %q want %q", got, testCase.want)
			}
		})
	}
}
