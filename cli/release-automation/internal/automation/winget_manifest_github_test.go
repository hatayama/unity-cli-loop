package automation

import (
	"context"
	"strings"
	"testing"
)

// TestPutWingetManifestFileUpdatesExistingFileWithItsSHA verifies an existing fork file is overwritten by passing its blob SHA.
func TestPutWingetManifestFileUpdatesExistingFileWithItsSHA(t *testing.T) {
	var putArgs []string
	deps := wingetManifestUpdateDeps{
		runOutput: func(_ context.Context, extraEnv []string, _ string, args ...string) (string, error) {
			if len(extraEnv) != 1 || extraEnv[0] != "GH_TOKEN=token" {
				t.Fatalf("environment = %v", extraEnv)
			}
			if strings.Contains(strings.Join(args, " "), "-X PUT") {
				putArgs = append([]string{}, args...)
				return `{}`, nil
			}
			return `{"sha":"existing-sha"}`, nil
		},
	}

	err := putWingetManifestFile(context.Background(), deps, "token", "owner/fork", "branch", "manifests/a.yaml", "3.1.0", "content")
	if err != nil {
		t.Fatalf("putWingetManifestFile failed: %v", err)
	}
	if flagValue(putArgs, "sha") != "existing-sha" {
		t.Fatalf("PUT args = %v, want sha=existing-sha", putArgs)
	}
}
