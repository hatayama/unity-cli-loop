package unityprocess

import "testing"

// Verifies open -a is used only for a known unique bundle path; count 0 (unknown or none) falls back to osascript.
func TestShouldActivateViaOsascriptMac(t *testing.T) {
	cases := []struct {
		name    string
		bundle  string
		count   int
		wantOSA bool
	}{
		{name: "unique bundle", bundle: "/Applications/Unity.app", count: 1, wantOSA: false},
		{name: "unknown count is zero", bundle: "/Applications/Unity.app", count: 0, wantOSA: true},
		{name: "two instances", bundle: "/Applications/Unity.app", count: 2, wantOSA: true},
		{name: "empty bundle path", bundle: "", count: 1, wantOSA: true},
		{name: "empty bundle and zero count", bundle: "", count: 0, wantOSA: true},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got := shouldActivateViaOsascriptMac(testCase.bundle, testCase.count)
			if got != testCase.wantOSA {
				t.Fatalf("shouldActivateViaOsascriptMac(%q, %d) = %t, want %t", testCase.bundle, testCase.count, got, testCase.wantOSA)
			}
		})
	}
}
