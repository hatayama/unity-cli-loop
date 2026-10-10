package automation

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

const (
	unityPackageReleaseTestTagRef      = "refs/tags/v3.14.0"
	unityPackageReleaseTestPublished   = `{"tag_name": "v3.14.0", "draft": false, "assets": [{"name": "io.github.hatayama.uloopmcp-3.14.0.tgz", "size": 2048}]}`
	unityPackageReleaseTestUnsigned    = `{"tag_name": "v3.14.0", "draft": false, "assets": [{"name": "notes.txt", "size": 10}]}`
	unityPackageReleaseTestEmptyAsset  = `{"tag_name": "v3.14.0", "draft": false, "assets": [{"name": "io.github.hatayama.uloopmcp-3.14.0.tgz", "size": 0}]}`
	unityPackageReleaseTestPlanVersion = `{"Packages/src": "3.14.0", "cli/dispatcher": "3.8.1"}`
)

func writeUnityPackageReleaseManifest(t *testing.T, content string) string {
	t.Helper()
	repoRoot := t.TempDir()
	if err := os.WriteFile(filepath.Join(repoRoot, ".release-please-manifest.json"), []byte(content), 0o600); err != nil {
		t.Fatalf("write manifest: %v", err)
	}
	return repoRoot
}

func runPlanUnityPackageReleaseForTest(fake *fakeUnityPackageReleaseCommands, repoRoot string, dryRun bool) (int, string, string) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPlanUnityPackageReleaseWithDeps(context.Background(), &stdout, &stderr, unityPackageReleasePlanOptions{
		Repository: unityPackageReleaseTestRepository,
		RepoRoot:   repoRoot,
		DryRun:     dryRun,
	}, unityPackageReleasePlanDeps{runOutput: fake.runOutput, checkRelease: fake.checkRelease})
	return code, stdout.String(), stderr.String()
}

// Verifies the plan creates a release only when none is published and the readiness check names
// its release commit, never touches a published release except to dry-run sign it from its tag,
// and runs the check only for a release that is not published yet.
func TestPlanUnityPackageReleaseDecidesFromReleaseState(t *testing.T) {
	tests := []struct {
		name           string
		release        fakeUnityPackageReleaseResponse
		checkOutput    string
		dryRun         bool
		wantSign       bool
		wantPublish    bool
		wantSourceRef  string
		wantAnnotation string
		wantCheckCalls int
	}{
		{
			name:          "published release with the tarball is skipped",
			release:       fakeUnityPackageReleaseResponse{output: unityPackageReleaseTestPublished},
			wantSourceRef: unityPackageReleaseTestTagRef,
		},
		{
			name:          "dry run signs a published release from its tag",
			release:       fakeUnityPackageReleaseResponse{output: unityPackageReleaseTestPublished},
			dryRun:        true,
			wantSign:      true,
			wantSourceRef: unityPackageReleaseTestTagRef,
		},
		{
			name:           "published release without the tarball warns",
			release:        fakeUnityPackageReleaseResponse{output: unityPackageReleaseTestUnsigned},
			wantSourceRef:  unityPackageReleaseTestTagRef,
			wantAnnotation: "::warning::",
		},
		{
			name:           "published release with an empty tarball warns",
			release:        fakeUnityPackageReleaseResponse{output: unityPackageReleaseTestEmptyAsset},
			wantSourceRef:  unityPackageReleaseTestTagRef,
			wantAnnotation: "::warning::",
		},
		{
			name:          "dry run signs a published release without the tarball from its tag",
			release:       fakeUnityPackageReleaseResponse{output: unityPackageReleaseTestUnsigned},
			dryRun:        true,
			wantSign:      true,
			wantSourceRef: unityPackageReleaseTestTagRef,
		},
		{
			name:           "due release is signed and published from its release commit",
			release:        unityPackageReleaseTestNotFound,
			checkOutput:    unityPackageReleaseTestCommit + "\n",
			wantSign:       true,
			wantPublish:    true,
			wantSourceRef:  unityPackageReleaseTestCommit,
			wantCheckCalls: 1,
		},
		{
			name:           "dry run signs a due release without publishing",
			release:        unityPackageReleaseTestNotFound,
			checkOutput:    unityPackageReleaseTestCommit + "\n",
			dryRun:         true,
			wantSign:       true,
			wantSourceRef:  unityPackageReleaseTestCommit,
			wantCheckCalls: 1,
		},
		{
			name:           "release that is not due yet is skipped",
			release:        unityPackageReleaseTestNotFound,
			wantAnnotation: "::notice::",
			wantCheckCalls: 1,
		},
		{
			name:           "dry run skips a release that is not due yet",
			release:        unityPackageReleaseTestNotFound,
			dryRun:         true,
			wantAnnotation: "::notice::",
			wantCheckCalls: 1,
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			fake := newFakeUnityPackageReleaseCommands(t, map[string][]fakeUnityPackageReleaseResponse{"release": {test.release}})
			fake.checkOutput = test.checkOutput
			code, stdout, stderr := runPlanUnityPackageReleaseForTest(fake, writeUnityPackageReleaseManifest(t, unityPackageReleaseTestPlanVersion), test.dryRun)
			if code != 0 {
				t.Fatalf("exit code = %d, stderr = %s", code, stderr)
			}
			want := fmt.Sprintf("sign=%t\npublish=%t\n", test.wantSign, test.wantPublish) +
				"tag=v3.14.0\n" +
				"version=3.14.0\n" +
				"asset-name=io.github.hatayama.uloopmcp-3.14.0.tgz\n" +
				"source-ref=" + test.wantSourceRef + "\n"
			if stdout != want {
				t.Fatalf("stdout = %q, want %q", stdout, want)
			}
			if strings.TrimSpace(stderr) == "" {
				t.Fatal("stderr has no reason for the decision")
			}
			annotation := ""
			for _, prefix := range []string{"::warning::", "::notice::"} {
				if strings.HasPrefix(stderr, prefix) {
					annotation = prefix
				}
			}
			if annotation != test.wantAnnotation {
				t.Fatalf("stderr = %q, want annotation %q", stderr, test.wantAnnotation)
			}
			if fake.checkCalls != test.wantCheckCalls {
				t.Fatalf("check calls = %d, want %d", fake.checkCalls, test.wantCheckCalls)
			}
		})
	}
}

// Verifies every input the plan cannot act on fails the run without GITHUB_OUTPUT lines, so the
// workflow never signs a guessed release commit, and that a failed release lookup is never
// mistaken for a release that does not exist yet: the readiness check is not run after it.
func TestPlanUnityPackageReleaseFailsWithoutOutput(t *testing.T) {
	tests := []struct {
		name           string
		manifest       string
		release        []fakeUnityPackageReleaseResponse
		checkOutput    string
		checkErr       error
		wantCheckCalls int
	}{
		{
			name:           "check failure fails the plan",
			release:        []fakeUnityPackageReleaseResponse{unityPackageReleaseTestNotFound},
			checkErr:       errors.New("exit status 1"),
			wantCheckCalls: 1,
		},
		{
			name:           "malformed check output fails the plan",
			release:        []fakeUnityPackageReleaseResponse{unityPackageReleaseTestNotFound},
			checkOutput:    "0123456\n",
			wantCheckCalls: 1,
		},
		{
			name:           "check output with more than the commit fails the plan",
			release:        []fakeUnityPackageReleaseResponse{unityPackageReleaseTestNotFound},
			checkOutput:    unityPackageReleaseTestCommit + "\nverified\n",
			wantCheckCalls: 1,
		},
		{
			name:    "lookup failure fails the plan",
			release: []fakeUnityPackageReleaseResponse{{err: errors.New("gh api failed: exit status 1\nHTTP 401: Bad credentials")}},
		},
		{
			name:    "release lookup that is not JSON",
			release: []fakeUnityPackageReleaseResponse{{output: "Not JSON"}},
		},
		{
			name:    "release lookup that answers with a draft",
			release: []fakeUnityPackageReleaseResponse{{output: `{"tag_name": "v3.14.0", "draft": true, "assets": []}`}},
		},
		{
			name:    "release lookup that answers with another tag",
			release: []fakeUnityPackageReleaseResponse{{output: `{"tag_name": "v3.13.0", "draft": false, "assets": []}`}},
		},
		{name: "manifest without the package entry", manifest: `{"cli/dispatcher": "3.8.1"}`},
		{name: "manifest that is not JSON", manifest: `Packages/src: 3.14.0`},
		{name: "manifest version that is not a release tag", manifest: `{"Packages/src": "3.14"}`},
		{name: "missing manifest"},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			repoRoot := t.TempDir()
			if test.release != nil {
				repoRoot = writeUnityPackageReleaseManifest(t, unityPackageReleaseTestPlanVersion)
			}
			if test.manifest != "" {
				repoRoot = writeUnityPackageReleaseManifest(t, test.manifest)
			}
			fake := newFakeUnityPackageReleaseCommands(t, map[string][]fakeUnityPackageReleaseResponse{"release": test.release})
			fake.checkOutput = test.checkOutput
			fake.checkErr = test.checkErr
			code, stdout, _ := runPlanUnityPackageReleaseForTest(fake, repoRoot, false)
			if code == 0 || stdout != "" {
				t.Fatalf("exit code = %d, stdout = %q; want a failure without GITHUB_OUTPUT lines", code, stdout)
			}
			if fake.checkCalls != test.wantCheckCalls {
				t.Fatalf("check calls = %d, want %d", fake.checkCalls, test.wantCheckCalls)
			}
			if test.release == nil && len(fake.calls) != 0 {
				t.Fatalf("calls = %v, want none before the manifest names a release", fake.calls)
			}
		})
	}
}

// Verifies the readiness check runs from the repository root, hands back only its stdout, and
// streams its stderr into the job log, because that stderr is the only place that says why a
// release is not due yet.
func TestRunUnityPackageReleaseCheckStreamsStderr(t *testing.T) {
	repoRoot := t.TempDir()
	scriptPath := filepath.Join(repoRoot, filepath.FromSlash(unityPackageReleaseCheckScript))
	if err := os.MkdirAll(filepath.Dir(scriptPath), 0o700); err != nil {
		t.Fatalf("create scripts directory: %v", err)
	}
	script := "#!/bin/sh\necho '::notice::runner release is not published yet' >&2\n" +
		"[ -f .release-please-manifest.json ] || exit 3\n" +
		"printf '%s\\n' " + unityPackageReleaseTestCommit + "\n"
	if err := os.WriteFile(scriptPath, []byte(script), 0o600); err != nil {
		t.Fatalf("write check script: %v", err)
	}

	var stderr bytes.Buffer
	if _, err := runUnityPackageReleaseCheck(context.Background(), &stderr, repoRoot); err == nil {
		t.Fatal("check succeeded without the manifest in its working directory, want the script's failure")
	}
	if !strings.Contains(stderr.String(), "::notice::runner release is not published yet") {
		t.Fatalf("stderr of the failed check = %q, want the script's stderr", stderr.String())
	}

	writeUnityPackageReleaseManifestInRoot(t, repoRoot)
	stderr.Reset()
	output, err := runUnityPackageReleaseCheck(context.Background(), &stderr, repoRoot)
	if err != nil {
		t.Fatalf("check failed: %v", err)
	}
	if output != unityPackageReleaseTestCommit+"\n" {
		t.Fatalf("output = %q, want only the commit the script printed to stdout", output)
	}
	if !strings.Contains(stderr.String(), "::notice::runner release is not published yet") {
		t.Fatalf("stderr = %q, want the script's stderr", stderr.String())
	}
}

func writeUnityPackageReleaseManifestInRoot(t *testing.T, repoRoot string) {
	t.Helper()
	if err := os.WriteFile(filepath.Join(repoRoot, ".release-please-manifest.json"), []byte(unityPackageReleaseTestPlanVersion), 0o600); err != nil {
		t.Fatalf("write manifest: %v", err)
	}
}

// Verifies the repository comes from --repo or GITHUB_REPOSITORY, that the default repository
// root is the git root holding the release-please manifest, which the workflow relies on when it
// runs the command from cli/release-automation, and that the removed --tag flag is rejected.
func TestParseUnityPackageReleasePlanOptions(t *testing.T) {
	noEnv := func(string) (string, bool) { return "", false }
	repositoryEnv := func(name string) (string, bool) {
		if name == "GITHUB_REPOSITORY" {
			return unityPackageReleaseTestRepository, true
		}
		return "", false
	}

	options, err := parseUnityPackageReleasePlanOptions(context.Background(), []string{"--dry-run=true"}, repositoryEnv)
	if err != nil {
		t.Fatalf("parse with GITHUB_REPOSITORY: %v", err)
	}
	if options.Repository != unityPackageReleaseTestRepository || !options.DryRun {
		t.Fatalf("options = %+v, want repository from GITHUB_REPOSITORY and a dry run", options)
	}
	if _, err := os.Stat(filepath.Join(options.RepoRoot, ".release-please-manifest.json")); err != nil {
		t.Fatalf("default repository root %q has no release-please manifest: %v", options.RepoRoot, err)
	}

	if _, err := parseUnityPackageReleasePlanOptions(context.Background(), []string{"--repo-root", t.TempDir()}, noEnv); err == nil {
		t.Fatal("parse without --repo or GITHUB_REPOSITORY succeeded, want an error")
	}
	if _, err := parseUnityPackageReleasePlanOptions(context.Background(), []string{"--tag", "v3.13.0"}, repositoryEnv); err == nil {
		t.Fatal("parse with --tag succeeded, want an error")
	}
}

// Verifies the command entry point fails without GITHUB_OUTPUT lines when the repository root has
// no release-please manifest to name the release.
func TestRunPlanUnityPackageReleaseFailsWithoutManifest(t *testing.T) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunPlanUnityPackageRelease(context.Background(), &stdout, &stderr, []string{"--repo", unityPackageReleaseTestRepository, "--repo-root", t.TempDir()})
	if code != 1 || stdout.Len() != 0 {
		t.Fatalf("exit code = %d, stdout = %q; want 1 without output", code, stdout.String())
	}
}

// Verifies malformed arguments exit with the usage status before any release is read.
func TestRunPlanUnityPackageReleaseRejectsUnknownFlag(t *testing.T) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunPlanUnityPackageRelease(context.Background(), &stdout, &stderr, []string{"--unknown"})
	if code != 2 || stdout.Len() != 0 {
		t.Fatalf("exit code = %d, stdout = %q; want 2 without output", code, stdout.String())
	}
}
