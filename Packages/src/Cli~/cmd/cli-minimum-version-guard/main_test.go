package main

import (
	"os"
	"os/exec"
	"path"
	"strings"
	"testing"
)

func TestCheckMinimumVersionCompatibility(t *testing.T) {
	// Verifies version ordering, sensitive-change decisions, and minimum-version updates.
	const olderMinimumVersion = "3.0.0-beta.13"
	const latestVersion = "3.0.0-beta.14"

	cases := []struct {
		name        string
		minimum     string
		mutate      func(t *testing.T, repositoryRoot string)
		expectedErr string
	}{
		{
			name: "lower minimum without sensitive change",
			mutate: func(t *testing.T, repositoryRoot string) {
				writeFile(t, repositoryRoot, "README.md", "documentation\n")
			},
		},
		{
			name:    "minimum greater than latest",
			minimum: "3.0.0-beta.15",
			mutate: func(t *testing.T, repositoryRoot string) {
				writeFile(t, repositoryRoot, "README.md", "documentation\n")
			},
			expectedErr: "must not be greater",
		},
		{
			name:        "sensitive change without decision",
			mutate:      writeSensitiveCLIChange,
			expectedErr: "compatibility-sensitive",
		},
		{
			name: "guard command change",
			mutate: func(t *testing.T, repositoryRoot string) {
				writeFile(t, repositoryRoot, "Packages/src/Cli~/cmd/cli-minimum-version-guard/main.go", "package main\n")
			},
		},
		{
			name: "keep decision without reason",
			mutate: func(t *testing.T, repositoryRoot string) {
				writeSensitiveCLIChange(t, repositoryRoot)
				writeKeepDecision(t, repositoryRoot, "3.0.0-beta.13", "")
			},
			expectedErr: "reason must not be empty",
		},
		{
			name: "keep decision with reason",
			mutate: func(t *testing.T, repositoryRoot string) {
				writeSensitiveCLIChange(t, repositoryRoot)
				writeKeepDecision(t, repositoryRoot, "3.0.0-beta.13", "The changed CLI behavior is additive.")
			},
		},
		{
			name: "minimum version updated",
			mutate: func(t *testing.T, repositoryRoot string) {
				writeSensitiveCLIChange(t, repositoryRoot)
				writeMinimumVersion(t, repositoryRoot, "3.0.0-beta.14")
			},
		},
	}

	for _, tt := range cases {
		t.Run(tt.name, func(t *testing.T) {
			minimumVersion := olderMinimumVersion
			if tt.minimum != "" {
				minimumVersion = tt.minimum
			}
			repositoryRoot := createRepository(t, minimumVersion, latestVersion)
			tt.mutate(t, repositoryRoot)
			commitAll(t, repositoryRoot, tt.name)
			t.Setenv("CLI_MINIMUM_VERSION_BASE_REF", "HEAD^")

			err := check(repositoryRoot)
			if tt.expectedErr == "" {
				if err != nil {
					t.Fatal(err)
				}
				return
			}
			if err == nil || !strings.Contains(err.Error(), tt.expectedErr) {
				t.Fatalf("expected %q error, got %v", tt.expectedErr, err)
			}
		})
	}
}

func createRepository(t *testing.T, minimumVersion string, latestVersion string) string {
	t.Helper()

	repositoryRoot := t.TempDir()
	runGit(t, repositoryRoot, "init", "-q")
	runGit(t, repositoryRoot, "config", "user.email", "test@example.invalid")
	runGit(t, repositoryRoot, "config", "user.name", "Test User")
	writeMinimumVersion(t, repositoryRoot, minimumVersion)
	writeContractVersion(t, repositoryRoot, latestVersion)
	commitAll(t, repositoryRoot, "base")
	return repositoryRoot
}

func writeMinimumVersion(t *testing.T, repositoryRoot string, value string) {
	t.Helper()

	writeFile(t, repositoryRoot, minimumVersionFile, `namespace Test { public static class CliConstants { public const string MINIMUM_REQUIRED_CLI_VERSION = "`+value+`"; } }`+"\n")
}

func writeContractVersion(t *testing.T, repositoryRoot string, value string) {
	t.Helper()

	writeFile(t, repositoryRoot, contractFile, `{ "schemaVersion": 1, "cliVersion": "`+value+`" }`+"\n")
}

func writeKeepDecision(t *testing.T, repositoryRoot string, value string, reason string) {
	t.Helper()

	writeFile(t, repositoryRoot, decisionFile, `{ "minimumRequiredCliVersion": "`+value+`", "decision": "keep", "reason": "`+reason+`" }`+"\n")
}

func writeSensitiveCLIChange(t *testing.T, repositoryRoot string) {
	t.Helper()

	writeFile(t, repositoryRoot, "Packages/src/Cli~/internal/cli/tool_readiness.go", "package cli\n")
}

func writeFile(t *testing.T, repositoryRoot string, file string, content string) {
	t.Helper()

	fullPath := path.Join(repositoryRoot, file)
	if err := os.MkdirAll(path.Dir(fullPath), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(fullPath, []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}
}

func commitAll(t *testing.T, repositoryRoot string, message string) {
	t.Helper()

	runGit(t, repositoryRoot, "add", ".")
	runGit(t, repositoryRoot, "commit", "-q", "-m", message)
}

func runGit(t *testing.T, repositoryRoot string, args ...string) {
	t.Helper()

	command := exec.Command("git", args...)
	command.Dir = repositoryRoot
	output, err := command.CombinedOutput()
	if err != nil {
		t.Fatalf("git %s failed: %s", strings.Join(args, " "), strings.TrimSpace(string(output)))
	}
}
