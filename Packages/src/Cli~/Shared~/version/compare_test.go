package version

import "testing"

func TestIsLessThanHandlesPrereleaseVersions(t *testing.T) {
	// Verifies that dispatcher compatibility checks follow npm-style prerelease ordering.
	cases := []struct {
		left     string
		right    string
		expected bool
	}{
		{left: "3.0.0-beta.0", right: "3.0.0-beta.1", expected: true},
		{left: "3.0.0-beta.1", right: "3.0.0-beta.1", expected: false},
		{left: "3.0.0", right: "3.0.0-beta.1", expected: false},
		{left: "v3.0.0-beta.0", right: "3.0.0-beta.1", expected: true},
		{left: "V3.0.0-beta.0", right: "3.0.0-beta.1", expected: true},
	}

	for _, tt := range cases {
		result := IsLessThan(tt.left, tt.right)
		if result != tt.expected {
			t.Fatalf("IsLessThan(%q, %q) = %v", tt.left, tt.right, result)
		}
	}
}

func TestCompareRejectsInvalidVersion(t *testing.T) {
	// Verifies that malformed dispatcher versions do not pass compatibility checks.
	cases := []string{
		"not-a-version",
		"1.2.3-",
		"1.2.3-alpha..1",
		"01.2.3",
		"1.02.3",
		"1.2.03",
		"1.2.3-alpha.01",
	}

	for _, value := range cases {
		_, ok := Compare(value, "3.0.0-beta.1")
		if ok {
			t.Fatalf("invalid version %q should not compare successfully", value)
		}
	}
}
