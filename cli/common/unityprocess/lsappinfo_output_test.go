package unityprocess

import "testing"

// Verifies parseLsappinfoFrontASN strips the trailing colon from a valid ASN and rejects empty or malformed input.
func TestParseLsappinfoFrontASN(t *testing.T) {
	cases := []struct {
		name  string
		input string
		want  string
	}{
		{name: "valid", input: "ASN:0x0-0x110e10d:", want: "ASN:0x0-0x110e10d"},
		{name: "trailing LF", input: "ASN:0x0-0x110e10d:\n", want: "ASN:0x0-0x110e10d"},
		{name: "trailing CRLF", input: "ASN:0x0-0x110e10d:\r\n", want: "ASN:0x0-0x110e10d"},
		{name: "empty", input: "", want: ""},
		{name: "garbage", input: "not-an-asn", want: ""},
		{name: "missing trailing colon", input: "ASN:0x0-0x110e10d", want: ""},
		{name: "colon only", input: "ASN:", want: ""},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got := parseLsappinfoFrontASN(testCase.input)
			if got != testCase.want {
				t.Fatalf("parseLsappinfoFrontASN(%q) = %q, want %q", testCase.input, got, testCase.want)
			}
		})
	}
}

// Verifies parseLsappinfoFindASN keeps the quoted app name, uses the first line only, and rejects malformed input.
func TestParseLsappinfoFindASN(t *testing.T) {
	cases := []struct {
		name  string
		input string
		want  string
	}{
		{name: "valid", input: `ASN:0x0-0x110e10d-"cmux":`, want: `ASN:0x0-0x110e10d-"cmux"`},
		{name: "trailing LF", input: "ASN:0x0-0x110e10d-\"cmux\":\n", want: `ASN:0x0-0x110e10d-"cmux"`},
		{name: "trailing CRLF", input: "ASN:0x0-0x110e10d-\"cmux\":\r\n", want: `ASN:0x0-0x110e10d-"cmux"`},
		{name: "first line only", input: "ASN:0x0-0x110e10d-\"cmux\":\nASN:0x0-0x220e20e-\"other\":\n", want: `ASN:0x0-0x110e10d-"cmux"`},
		{name: "empty", input: "", want: ""},
		{name: "garbage", input: "no asn here", want: ""},
		{name: "missing trailing colon", input: `ASN:0x0-0x110e10d-"cmux"`, want: ""},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got := parseLsappinfoFindASN(testCase.input)
			if got != testCase.want {
				t.Fatalf("parseLsappinfoFindASN(%q) = %q, want %q", testCase.input, got, testCase.want)
			}
		})
	}
}

// Verifies parseLsappinfoPID reads the quoted pid field and returns 0 when the value is missing or not an integer.
func TestParseLsappinfoPID(t *testing.T) {
	cases := []struct {
		name  string
		input string
		want  int
	}{
		{name: "valid", input: `"pid"=61295`, want: 61295},
		{name: "trailing LF", input: "\"pid\"=61295\n", want: 61295},
		{name: "trailing CRLF", input: "\"pid\"=61295\r\n", want: 61295},
		{name: "empty", input: "", want: 0},
		{name: "garbage", input: "pid=61295", want: 0},
		{name: "non-integer", input: `"pid"=abc`, want: 0},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got := parseLsappinfoPID(testCase.input)
			if got != testCase.want {
				t.Fatalf("parseLsappinfoPID(%q) = %d, want %d", testCase.input, got, testCase.want)
			}
		})
	}
}

// Verifies parseLsappinfoBundlePath extracts the quoted path and returns empty for empty or malformed input.
func TestParseLsappinfoBundlePath(t *testing.T) {
	cases := []struct {
		name  string
		input string
		want  string
	}{
		{name: "valid", input: `"LSBundlePath"="/Applications/cmux.app"`, want: "/Applications/cmux.app"},
		{name: "trailing LF", input: "\"LSBundlePath\"=\"/Applications/cmux.app\"\n", want: "/Applications/cmux.app"},
		{name: "trailing CRLF", input: "\"LSBundlePath\"=\"/Applications/cmux.app\"\r\n", want: "/Applications/cmux.app"},
		{name: "empty", input: "", want: ""},
		{name: "garbage", input: "LSBundlePath=/Applications/cmux.app", want: ""},
		{name: "missing closing quote", input: `"LSBundlePath"="/Applications/cmux.app`, want: ""},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got := parseLsappinfoBundlePath(testCase.input)
			if got != testCase.want {
				t.Fatalf("parseLsappinfoBundlePath(%q) = %q, want %q", testCase.input, got, testCase.want)
			}
		})
	}
}

// Verifies countLsappinfoBundlePath counts exact trimmed bundle-path lines, including 0/1/2 matches and rejecting prefix-only paths.
func TestCountLsappinfoBundlePath(t *testing.T) {
	listOutput := "" +
		"    bundle path=\"/Applications/Unity.app\"\n" +
		"    bundle path=\"/Applications/cmux.app\"\r\n" +
		"    bundle path=\"/Applications/Unity.app.bak\"\n" +
		"    bundle path=\"/Applications/Unity.app\"\n"

	cases := []struct {
		name       string
		listOutput string
		bundlePath string
		want       int
	}{
		{name: "zero", listOutput: listOutput, bundlePath: "/Applications/Other.app", want: 0},
		{name: "one", listOutput: listOutput, bundlePath: "/Applications/cmux.app", want: 1},
		{name: "two", listOutput: listOutput, bundlePath: "/Applications/Unity.app", want: 2},
		{name: "prefix must not match", listOutput: listOutput, bundlePath: "/Applications/Unity", want: 0},
		{name: "empty list", listOutput: "", bundlePath: "/Applications/Unity.app", want: 0},
		{name: "garbage", listOutput: "not a list", bundlePath: "/Applications/Unity.app", want: 0},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got := countLsappinfoBundlePath(testCase.listOutput, testCase.bundlePath)
			if got != testCase.want {
				t.Fatalf("countLsappinfoBundlePath(..., %q) = %d, want %d", testCase.bundlePath, got, testCase.want)
			}
		})
	}

	bakCount := countLsappinfoBundlePath(listOutput, "/Applications/Unity.app.bak")
	if bakCount != 1 {
		t.Fatalf("expected Unity.app.bak to count as its own exact path, got %d", bakCount)
	}
}
