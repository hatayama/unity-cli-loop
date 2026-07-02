package dispatcher

import (
	"bytes"
	"testing"
)

func TestDispatcherVersionChangedNormalizesVersionPrefix(t *testing.T) {
	// Verifies v-prefixed and unprefixed dispatcher versions are treated as the same release.
	if dispatcherVersionChanged("v3.0.1-beta.3", "3.0.1-beta.3") {
		t.Fatal("expected equivalent dispatcher versions to be unchanged")
	}
}

func TestWriteOptionalDispatcherUpdateCompletionReportsNormalizedVersions(t *testing.T) {
	// Verifies dispatcher update messages report canonical versions after prefix normalization.
	var stderr bytes.Buffer

	writeOptionalDispatcherUpdateCompletion(&stderr, "v3.0.1-beta.2", "3.0.1-beta.3")

	expected := "uloop: dispatcher updated from 3.0.1-beta.2 to 3.0.1-beta.3"
	if !bytes.Contains(stderr.Bytes(), []byte(expected)) {
		t.Fatalf("update output mismatch: %s", stderr.String())
	}
}
