package dispatcher

import (
	"bytes"
	"encoding/json"
	"strings"
	"testing"
)

// Verifies a bare dependencies-only manifest gains the OpenUPM registry and package dependency.
func TestMergePackageManifestAddsRegistryAndDependencyToBareManifest(t *testing.T) {
	input := []byte(`{
  "dependencies": {
    "com.unity.modules.ai": "1.0.0"
  }
}
`)

	result, err := mergePackageManifest(input, "1.2.3")
	if err != nil {
		t.Fatalf("mergePackageManifest failed: %v", err)
	}
	if !result.Changed {
		t.Fatal("expected Changed=true")
	}
	if !result.RegistryAdded || !result.DependencyAdded {
		t.Fatalf("flags mismatch: %#v", result)
	}

	assertManifestHasDependency(t, result.Content, dispatcherUnityPackageName, "1.2.3")
	assertManifestHasOpenUPMRegistry(t, result.Content)
}

// Verifies foreign scopedRegistries entries stay intact while OpenUPM is appended.
func TestMergePackageManifestKeepsExistingForeignRegistry(t *testing.T) {
	input := []byte(`{
  "dependencies": {
    "com.unity.modules.ai": "1.0.0"
  },
  "scopedRegistries": [
    {
      "name": "Other",
      "url": "https://example.invalid/registry",
      "scopes": [
        "com.other.package"
      ]
    }
  ]
}
`)

	result, err := mergePackageManifest(input, "1.2.3")
	if err != nil {
		t.Fatalf("mergePackageManifest failed: %v", err)
	}

	content := string(result.Content)
	if !strings.Contains(content, `"url": "https://example.invalid/registry"`) {
		t.Fatalf("foreign registry was altered:\n%s", content)
	}
	if !strings.Contains(content, `"com.other.package"`) {
		t.Fatalf("foreign scope was altered:\n%s", content)
	}
	assertManifestHasOpenUPMRegistry(t, result.Content)
	assertManifestHasDependency(t, result.Content, dispatcherUnityPackageName, "1.2.3")
}

// Verifies an existing OpenUPM registry entry gets the scope appended without a duplicate entry.
func TestMergePackageManifestAppendsScopeToExistingOpenUPMRegistry(t *testing.T) {
	input := []byte(`{
  "dependencies": {
    "com.unity.modules.ai": "1.0.0"
  },
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.other.openupm"
      ]
    }
  ]
}
`)

	result, err := mergePackageManifest(input, "1.2.3")
	if err != nil {
		t.Fatalf("mergePackageManifest failed: %v", err)
	}
	if !result.ScopeAdded {
		t.Fatal("expected ScopeAdded=true")
	}
	if result.RegistryAdded {
		t.Fatal("expected RegistryAdded=false when OpenUPM already exists")
	}

	openUPMCount := strings.Count(string(result.Content), `"url": "https://package.openupm.com"`)
	if openUPMCount != 1 {
		t.Fatalf("expected one OpenUPM registry entry, got %d:\n%s", openUPMCount, result.Content)
	}
	if !strings.Contains(string(result.Content), `"com.other.openupm"`) {
		t.Fatalf("existing scope missing:\n%s", result.Content)
	}
	assertManifestHasOpenUPMRegistry(t, result.Content)
	assertManifestHasDependency(t, result.Content, dispatcherUnityPackageName, "1.2.3")
}

// Verifies no rewrite when registry, scope, and the same dependency version are already present.
func TestMergePackageManifestNoChangeWhenAlreadyInstalled(t *testing.T) {
	input := []byte(`{
  "dependencies": {
    "com.unity.modules.ai": "1.0.0",
    "io.github.hatayama.uloopmcp": "1.2.3"
  },
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": [
        "io.github.hatayama.uloopmcp"
      ]
    }
  ]
}
`)

	result, err := mergePackageManifest(input, "1.2.3")
	if err != nil {
		t.Fatalf("mergePackageManifest failed: %v", err)
	}
	if result.Changed {
		t.Fatalf("expected Changed=false, got content:\n%s", result.Content)
	}
	if !bytes.Equal(result.Content, input) {
		t.Fatalf("content changed unexpectedly:\n%s", result.Content)
	}
}

// Verifies an existing dependency version is updated in place without reordering keys.
func TestMergePackageManifestUpdatesDependencyVersionInPlace(t *testing.T) {
	input := []byte(`{
  "dependencies": {
    "com.unity.modules.ai": "1.0.0",
    "io.github.hatayama.uloopmcp": "1.2.2",
    "com.unity.modules.animation": "1.0.0"
  }
}
`)

	result, err := mergePackageManifest(input, "1.2.3")
	if err != nil {
		t.Fatalf("mergePackageManifest failed: %v", err)
	}
	if !result.Changed {
		t.Fatal("expected Changed=true")
	}
	if result.PreviousVersion != "1.2.2" {
		t.Fatalf("PreviousVersion mismatch: %q", result.PreviousVersion)
	}
	if result.DependencyAdded {
		t.Fatal("expected DependencyAdded=false for version update")
	}

	deps := decodeOrderedObjectField(t, result.Content, "dependencies")
	keys := deps.keys
	expectedKeys := []string{"com.unity.modules.ai", "io.github.hatayama.uloopmcp", "com.unity.modules.animation"}
	if len(keys) != len(expectedKeys) {
		t.Fatalf("dependency key count mismatch: %#v", keys)
	}
	for index, expected := range expectedKeys {
		if keys[index] != expected {
			t.Fatalf("dependency key order mismatch at %d: %#v", index, keys)
		}
	}
	assertManifestHasDependency(t, result.Content, dispatcherUnityPackageName, "1.2.3")
}

// Verifies non-alphabetical top-level key order is preserved across a rewrite.
func TestMergePackageManifestPreservesTopLevelKeyOrder(t *testing.T) {
	input := []byte(`{
  "testables": [
    "com.unity.testtools.codecoverage"
  ],
  "dependencies": {
    "com.unity.modules.ai": "1.0.0"
  }
}
`)

	result, err := mergePackageManifest(input, "1.2.3")
	if err != nil {
		t.Fatalf("mergePackageManifest failed: %v", err)
	}

	top := parseOrderedJSONObject(t, result.Content)
	if len(top.keys) < 2 || top.keys[0] != "testables" {
		t.Fatalf("top-level key order not preserved: %#v", top.keys)
	}
	if top.keys[1] != "dependencies" {
		t.Fatalf("dependencies should remain second: %#v", top.keys)
	}
}

// Verifies CRLF input is rewritten with CRLF line endings (Windows regression).
func TestMergePackageManifestPreservesCRLFLineEndings(t *testing.T) {
	input := []byte("{\r\n  \"dependencies\": {\r\n    \"com.unity.modules.ai\": \"1.0.0\"\r\n  }\r\n}\r\n")

	result, err := mergePackageManifest(input, "1.2.3")
	if err != nil {
		t.Fatalf("mergePackageManifest failed: %v", err)
	}
	if !bytes.Contains(result.Content, []byte("\r\n")) {
		t.Fatalf("expected CRLF output:\n%q", result.Content)
	}
	if bytes.Contains(bytes.ReplaceAll(result.Content, []byte("\r\n"), nil), []byte("\n")) {
		t.Fatalf("found bare LF in CRLF output:\n%q", result.Content)
	}
}

// Verifies trailing-newline presence or absence matches the input.
func TestMergePackageManifestPreservesMissingTrailingNewline(t *testing.T) {
	withoutNewline := []byte(`{
  "dependencies": {
    "com.unity.modules.ai": "1.0.0"
  }
}`)
	withNewline := append(append([]byte{}, withoutNewline...), '\n')

	resultWithout, err := mergePackageManifest(withoutNewline, "1.2.3")
	if err != nil {
		t.Fatalf("merge without newline failed: %v", err)
	}
	if bytes.HasSuffix(resultWithout.Content, []byte("\n")) {
		t.Fatalf("unexpected trailing newline:\n%q", resultWithout.Content)
	}

	resultWith, err := mergePackageManifest(withNewline, "1.2.3")
	if err != nil {
		t.Fatalf("merge with newline failed: %v", err)
	}
	if !bytes.HasSuffix(resultWith.Content, []byte("\n")) {
		t.Fatalf("expected trailing newline:\n%q", resultWith.Content)
	}
}

// Verifies malformed JSON is rejected.
func TestMergePackageManifestRejectsMalformedJSON(t *testing.T) {
	_, err := mergePackageManifest([]byte(`{not json`), "1.2.3")
	if err == nil {
		t.Fatal("expected error for malformed JSON")
	}
}

// Verifies trailing garbage after a complete JSON object is rejected.
func TestMergePackageManifestRejectsTrailingGarbage(t *testing.T) {
	_, err := mergePackageManifest([]byte("{\n  \"dependencies\": {}\n}\n trailing"), "1.2.3")
	if err == nil {
		t.Fatal("expected error for trailing garbage after JSON object")
	}
}

// Verifies scopedRegistries is inserted immediately after dependencies when absent.
func TestMergePackageManifestInsertsScopedRegistriesAfterDependencies(t *testing.T) {
	input := []byte(`{
  "dependencies": {
    "com.unity.modules.ai": "1.0.0"
  },
  "testables": [
    "com.unity.testtools.codecoverage"
  ]
}
`)

	result, err := mergePackageManifest(input, "1.2.3")
	if err != nil {
		t.Fatalf("mergePackageManifest failed: %v", err)
	}

	top := parseOrderedJSONObject(t, result.Content)
	dependenciesIndex := -1
	scopedIndex := -1
	for index, key := range top.keys {
		if key == "dependencies" {
			dependenciesIndex = index
		}
		if key == "scopedRegistries" {
			scopedIndex = index
		}
	}
	if dependenciesIndex < 0 || scopedIndex < 0 {
		t.Fatalf("missing keys: %#v", top.keys)
	}
	if scopedIndex != dependenciesIndex+1 {
		t.Fatalf("scopedRegistries not after dependencies: %#v", top.keys)
	}
}

func assertManifestHasDependency(t *testing.T, content []byte, name string, version string) {
	t.Helper()
	deps := decodeOrderedObjectField(t, content, "dependencies")
	raw, ok := deps.values[name]
	if !ok {
		t.Fatalf("dependency %s missing from:\n%s", name, content)
	}
	var decoded string
	if err := json.Unmarshal(raw, &decoded); err != nil {
		t.Fatalf("dependency value decode failed: %v", err)
	}
	if decoded != version {
		t.Fatalf("dependency version mismatch: got %q want %q", decoded, version)
	}
}

func assertManifestHasOpenUPMRegistry(t *testing.T, content []byte) {
	t.Helper()
	top := parseOrderedJSONObject(t, content)
	rawRegistries, ok := top.values["scopedRegistries"]
	if !ok {
		t.Fatalf("scopedRegistries missing:\n%s", content)
	}
	elements, err := parseJSONRawArray(rawRegistries)
	if err != nil {
		t.Fatalf("parse scopedRegistries failed: %v", err)
	}
	for _, element := range elements {
		entry, parseErr := parseOrderedJSONObjectBytes(element)
		if parseErr != nil {
			t.Fatalf("parse registry entry failed: %v", parseErr)
		}
		urlRaw, hasURL := entry.values["url"]
		if !hasURL {
			continue
		}
		var url string
		if unmarshalErr := json.Unmarshal(urlRaw, &url); unmarshalErr != nil {
			t.Fatalf("url decode failed: %v", unmarshalErr)
		}
		if url != openUPMRegistryURL {
			continue
		}
		rawScopes, hasScopes := entry.values["scopes"]
		if !hasScopes {
			t.Fatalf("OpenUPM registry missing scopes:\n%s", content)
		}
		var scopes []string
		if unmarshalErr := json.Unmarshal(rawScopes, &scopes); unmarshalErr != nil {
			t.Fatalf("scopes decode failed: %v", unmarshalErr)
		}
		for _, scope := range scopes {
			if scope == dispatcherUnityPackageName {
				return
			}
		}
		t.Fatalf("OpenUPM scopes missing %s: %#v\n%s", dispatcherUnityPackageName, scopes, content)
	}
	t.Fatalf("OpenUPM registry missing:\n%s", content)
}

func decodeOrderedObjectField(t *testing.T, content []byte, field string) orderedJSONObject {
	t.Helper()
	top := parseOrderedJSONObject(t, content)
	raw, ok := top.values[field]
	if !ok {
		t.Fatalf("field %s missing from:\n%s", field, content)
	}
	object, err := parseOrderedJSONObjectBytes(raw)
	if err != nil {
		t.Fatalf("parse field %s failed: %v", field, err)
	}
	return object
}

func parseOrderedJSONObject(t *testing.T, content []byte) orderedJSONObject {
	t.Helper()
	object, err := parseOrderedJSONObjectBytes(content)
	if err != nil {
		t.Fatalf("parseOrderedJSONObjectBytes failed: %v", err)
	}
	return object
}
