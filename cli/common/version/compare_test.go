package version

import (
	"encoding/json"
	"os"
	"testing"
)

type compareCaseCatalog struct {
	Cases []compareCase `json:"cases"`
}

type compareCase struct {
	Name       string `json:"name"`
	Left       string `json:"left"`
	Right      string `json:"right"`
	OK         bool   `json:"ok"`
	Comparison int    `json:"comparison"`
}

func TestCompareMatchesSharedCases(t *testing.T) {
	// Verifies that Go comparison behavior matches the shared cross-language contract table.
	catalog := readCompareCaseCatalog(t)

	for _, tt := range catalog.Cases {
		t.Run(tt.Name, func(t *testing.T) {
			result, ok := Compare(tt.Left, tt.Right)
			if ok != tt.OK {
				t.Fatalf("Compare(%q, %q) ok = %v, want %v", tt.Left, tt.Right, ok, tt.OK)
			}
			if result != tt.Comparison {
				t.Fatalf("Compare(%q, %q) = %d, want %d", tt.Left, tt.Right, result, tt.Comparison)
			}
		})
	}
}

func TestIsLessThanHandlesPrereleaseVersions(t *testing.T) {
	// Verifies that CLI version checks follow npm-style prerelease ordering.
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

func TestIsValidMatchesCompareValidity(t *testing.T) {
	// Verifies callers can validate semver strings without using self-comparison as a proxy.
	cases := []struct {
		value    string
		expected bool
	}{
		{value: "3.0.0", expected: true},
		{value: "v3.0.0-beta.1", expected: true},
		{value: "V3.0.0-beta.1+build.7", expected: true},
		{value: "not-a-version", expected: false},
		{value: "3.00.1", expected: false},
		{value: "3.0.0-01", expected: false},
		{value: "3.0.0+../../payload", expected: false},
		{value: "3.0.0+", expected: false},
		{value: "3.0.0+build..7", expected: false},
	}

	for _, tt := range cases {
		result := IsValid(tt.value)
		if result != tt.expected {
			t.Fatalf("IsValid(%q) = %v, want %v", tt.value, result, tt.expected)
		}
	}
}

func TestCompareRejectsInvalidVersion(t *testing.T) {
	// Verifies that malformed CLI versions do not pass compatibility checks.
	cases := []string{
		"not-a-version",
		"1.2.3-",
		"1.2.3-alpha..1",
		"01.2.3",
		"1.02.3",
		"1.2.03",
		"1.2.3-alpha.01",
		"1.2.3+../../payload",
	}

	for _, value := range cases {
		_, ok := Compare(value, "3.0.0-beta.1")
		if ok {
			t.Fatalf("invalid version %q should not compare successfully", value)
		}
	}
}

func readCompareCaseCatalog(t *testing.T) compareCaseCatalog {
	t.Helper()

	data, err := os.ReadFile("compare_cases.json")
	if err != nil {
		t.Fatalf("failed to read shared compare cases: %v", err)
	}

	var catalog compareCaseCatalog
	if err := json.Unmarshal(data, &catalog); err != nil {
		t.Fatalf("failed to parse shared compare cases: %v", err)
	}
	if len(catalog.Cases) == 0 {
		t.Fatal("shared compare cases must not be empty")
	}
	return catalog
}
