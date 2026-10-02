package dispatcher

import (
	"bytes"
	"context"
	"errors"
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

func TestDispatcherInstalledVersionOrEmptyHidesReadFailure(t *testing.T) {
	// Verifies a failed installed-version read yields an empty version instead of an error.
	previous := dispatcherReadInstalledVersion
	t.Cleanup(func() {
		dispatcherReadInstalledVersion = previous
	})
	// The version would leak through if the error were ignored.
	dispatcherReadInstalledVersion = func(context.Context) (string, error) {
		return "9.9.9", errors.New("version probe failed")
	}

	if version := dispatcherInstalledVersionOrEmpty(context.Background()); version != "" {
		t.Fatalf("expected an empty version, got %q", version)
	}
}

func TestWriteManualDispatcherUpdateCompletion(t *testing.T) {
	// Verifies the manual update summary for an unknown, unchanged, and changed installed version.
	cases := []struct {
		toVersion string
		want      string
	}{
		{toVersion: "", want: "uloop dispatcher update completed.\n"},
		{toVersion: "v3.0.0", want: "uloop dispatcher is already up to date at 3.0.0.\n"},
		{toVersion: "3.1.0", want: "uloop dispatcher updated from 3.0.0 to 3.1.0.\n"},
	}
	for _, testCase := range cases {
		var stdout bytes.Buffer

		writeManualDispatcherUpdateCompletion(&stdout, "3.0.0", testCase.toVersion)

		if stdout.String() != testCase.want {
			t.Fatalf("toVersion %q: got %q want %q", testCase.toVersion, stdout.String(), testCase.want)
		}
	}
}
