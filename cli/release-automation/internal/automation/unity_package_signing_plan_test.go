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

const unityPackageSigningTestRepository = "example-owner/example-repo"

// fakeUnityPackageSigningGh answers gh release view with a fixed response or error and records
// the tag it was asked about.
type fakeUnityPackageSigningGh struct {
	t           *testing.T
	releaseJSON string
	failure     error
	viewedTags  []string
}

func (fake *fakeUnityPackageSigningGh) runOutput(_ context.Context, name string, args ...string) (string, error) {
	fake.t.Helper()
	if name != "gh" || len(args) < 3 || args[0] != "release" || args[1] != "view" {
		fake.t.Fatalf("unexpected command %s %v", name, args)
	}
	fake.viewedTags = append(fake.viewedTags, args[2])
	if fake.failure != nil {
		return "", fake.failure
	}
	return fake.releaseJSON, nil
}

func writeUnityPackageSigningManifest(t *testing.T, content string) string {
	t.Helper()
	repoRoot := t.TempDir()
	if err := os.WriteFile(filepath.Join(repoRoot, ".release-please-manifest.json"), []byte(content), 0o600); err != nil {
		t.Fatalf("write manifest: %v", err)
	}
	return repoRoot
}

func runPlanUnityPackageSigningForTest(t *testing.T, fake *fakeUnityPackageSigningGh, tag string) (int, string, string) {
	t.Helper()
	repoRoot := writeUnityPackageSigningManifest(t, `{"Packages/src": "3.14.0", "cli/dispatcher": "3.8.1"}`)
	return runPlanUnityPackageSigningInRootForTest(fake, repoRoot, tag)
}

func runPlanUnityPackageSigningInRootForTest(fake *fakeUnityPackageSigningGh, repoRoot string, tag string) (int, string, string) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPlanUnityPackageSigningWithDeps(context.Background(), &stdout, &stderr, unityPackageSigningOptions{
		Repository: unityPackageSigningTestRepository,
		RepoRoot:   repoRoot,
		Tag:        tag,
	}, unityPackageSigningDeps{runOutput: fake.runOutput})
	return code, stdout.String(), stderr.String()
}

// Verifies the plan decides from the release state of the package tag, signing only a published
// release that has no non-empty signed tarball yet, and names the asset the publish job uploads.
func TestPlanUnityPackageSigningDecidesFromReleaseState(t *testing.T) {
	tests := []struct {
		name        string
		releaseJSON string
		failure     error
		wantSign    string
	}{
		{
			name:        "published release without assets is signed",
			releaseJSON: `{"isDraft": false, "assets": []}`,
			wantSign:    "true",
		},
		{
			name:        "published release with only unrelated assets is signed",
			releaseJSON: `{"isDraft": false, "assets": [{"name": "uloop-dispatcher-linux-amd64.tar.gz", "size": 10}]}`,
			wantSign:    "true",
		},
		{
			name:        "empty signed tarball left by a failed upload is signed again",
			releaseJSON: `{"isDraft": false, "assets": [{"name": "io.github.hatayama.uloopmcp-3.14.0.tgz", "size": 0}]}`,
			wantSign:    "true",
		},
		{
			name:        "release that already carries the signed tarball is skipped",
			releaseJSON: `{"isDraft": false, "assets": [{"name": "io.github.hatayama.uloopmcp-3.14.0.tgz", "size": 2048}]}`,
			wantSign:    "false",
		},
		{
			name:        "draft release is skipped until the release sync publishes it",
			releaseJSON: `{"isDraft": true, "assets": []}`,
			wantSign:    "false",
		},
		{
			name:     "missing release is skipped until the release sync creates it",
			failure:  errors.New("gh release view v3.14.0 failed: exit status 1\nrelease not found"),
			wantSign: "false",
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			fake := &fakeUnityPackageSigningGh{t: t, releaseJSON: test.releaseJSON, failure: test.failure}
			code, stdout, stderr := runPlanUnityPackageSigningForTest(t, fake, "")
			if code != 0 {
				t.Fatalf("exit code = %d, stderr = %s", code, stderr)
			}
			want := "sign=" + test.wantSign + "\n" +
				"tag=v3.14.0\n" +
				"version=3.14.0\n" +
				"asset-name=io.github.hatayama.uloopmcp-3.14.0.tgz\n"
			if stdout != want {
				t.Fatalf("stdout = %q, want %q", stdout, want)
			}
			if strings.TrimSpace(stderr) == "" {
				t.Fatal("stderr has no reason for the decision")
			}
		})
	}
}

// Verifies an explicit tag replaces the manifest version, so a manual run can sign an older release.
func TestPlanUnityPackageSigningUsesExplicitTag(t *testing.T) {
	fake := &fakeUnityPackageSigningGh{t: t, releaseJSON: `{"isDraft": false, "assets": []}`}
	code, stdout, stderr := runPlanUnityPackageSigningForTest(t, fake, "v3.13.0")
	if code != 0 {
		t.Fatalf("exit code = %d, stderr = %s", code, stderr)
	}
	if len(fake.viewedTags) != 1 || fake.viewedTags[0] != "v3.13.0" {
		t.Fatalf("viewed tags = %v, want [v3.13.0]", fake.viewedTags)
	}
	if !strings.Contains(stdout, "version=3.13.0\n") || !strings.Contains(stdout, "asset-name=io.github.hatayama.uloopmcp-3.13.0.tgz\n") {
		t.Fatalf("stdout = %q, want version and asset name of v3.13.0", stdout)
	}
}

// Verifies a dry run signs a published release even when it already carries the signed tarball,
// so a manual run can validate a new UPM CLI pin after every release is signed, while a draft or
// missing release is still skipped.
func TestPlanUnityPackageSigningDryRunIgnoresExistingTarball(t *testing.T) {
	tests := []struct {
		name        string
		releaseJSON string
		failure     error
		wantSign    bool
	}{
		{
			name:        "signed published release",
			releaseJSON: `{"isDraft": false, "assets": [{"name": "io.github.hatayama.uloopmcp-3.14.0.tgz", "size": 2048}]}`,
			wantSign:    true,
		},
		{name: "draft release", releaseJSON: `{"isDraft": true, "assets": []}`},
		{name: "missing release", failure: errors.New("gh release view v3.14.0 failed: exit status 1\nrelease not found")},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			repoRoot := writeUnityPackageSigningManifest(t, `{"Packages/src": "3.14.0"}`)
			fake := &fakeUnityPackageSigningGh{t: t, releaseJSON: test.releaseJSON, failure: test.failure}
			var stdout bytes.Buffer
			var stderr bytes.Buffer
			code := runPlanUnityPackageSigningWithDeps(context.Background(), &stdout, &stderr, unityPackageSigningOptions{
				Repository: unityPackageSigningTestRepository,
				RepoRoot:   repoRoot,
				DryRun:     true,
			}, unityPackageSigningDeps{runOutput: fake.runOutput})
			if code != 0 {
				t.Fatalf("exit code = %d, stderr = %s", code, stderr.String())
			}
			wantLine := fmt.Sprintf("sign=%t\n", test.wantSign)
			if !strings.HasPrefix(stdout.String(), wantLine) {
				t.Fatalf("stdout = %q, want it to start with %q", stdout.String(), wantLine)
			}
		})
	}
}

// Verifies tags of the other release components are rejected before any release is read, because
// signing the package source at a CLI tag would publish a mislabeled package.
func TestPlanUnityPackageSigningRejectsNonPackageTags(t *testing.T) {
	for _, tag := range []string{"dispatcher-v3.8.1", "uloop-project-runner-v3.8.0", "3.14.0", "v3.14"} {
		t.Run(tag, func(t *testing.T) {
			fake := &fakeUnityPackageSigningGh{t: t, releaseJSON: `{"isDraft": false, "assets": []}`}
			code, stdout, _ := runPlanUnityPackageSigningForTest(t, fake, tag)
			if code == 0 {
				t.Fatalf("exit code = 0, stdout = %q; want a failure", stdout)
			}
			if len(fake.viewedTags) != 0 {
				t.Fatalf("viewed tags = %v, want none", fake.viewedTags)
			}
		})
	}
}

// Verifies a gh failure other than a missing release fails the run instead of skipping, so an
// authentication or API outage cannot silently leave a release unsigned.
func TestPlanUnityPackageSigningFailsOnUnexpectedGhError(t *testing.T) {
	fake := &fakeUnityPackageSigningGh{t: t, failure: errors.New("gh release view v3.14.0 failed: exit status 1\nHTTP 401: Bad credentials")}
	code, stdout, _ := runPlanUnityPackageSigningForTest(t, fake, "")
	if code == 0 {
		t.Fatalf("exit code = 0, stdout = %q; want a failure", stdout)
	}
	if stdout != "" {
		t.Fatalf("stdout = %q, want no GITHUB_OUTPUT lines", stdout)
	}
}

// Verifies an unusable manifest or release response fails the run without GITHUB_OUTPUT lines,
// so the workflow never signs a guessed tag or acts on a release it could not read.
func TestPlanUnityPackageSigningFailsOnUnusableInputs(t *testing.T) {
	tests := []struct {
		name           string
		manifest       string
		releaseJSON    string
		wantViewedTags int
	}{
		{name: "manifest without the package entry", manifest: `{"cli/dispatcher": "3.8.1"}`},
		{name: "manifest that is not JSON", manifest: `Packages/src: 3.14.0`},
		{name: "missing manifest"},
		{
			name:           "release response that is not JSON",
			manifest:       `{"Packages/src": "3.14.0"}`,
			releaseJSON:    "Not JSON",
			wantViewedTags: 1,
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			repoRoot := t.TempDir()
			if test.manifest != "" {
				repoRoot = writeUnityPackageSigningManifest(t, test.manifest)
			}
			fake := &fakeUnityPackageSigningGh{t: t, releaseJSON: test.releaseJSON}
			code, stdout, _ := runPlanUnityPackageSigningInRootForTest(fake, repoRoot, "")
			if code == 0 || stdout != "" {
				t.Fatalf("exit code = %d, stdout = %q; want a failure without GITHUB_OUTPUT lines", code, stdout)
			}
			if len(fake.viewedTags) != test.wantViewedTags {
				t.Fatalf("viewed tags = %v, want %d", fake.viewedTags, test.wantViewedTags)
			}
		})
	}
}

// Verifies the repository comes from --repo or GITHUB_REPOSITORY, and that the default repository
// root is the git root holding the release-please manifest, which the workflow relies on when it
// runs the command from cli/release-automation without --repo-root.
func TestParseUnityPackageSigningOptions(t *testing.T) {
	noEnv := func(string) (string, bool) { return "", false }
	repositoryEnv := func(name string) (string, bool) {
		if name == "GITHUB_REPOSITORY" {
			return unityPackageSigningTestRepository, true
		}
		return "", false
	}

	options, err := parseUnityPackageSigningOptions(context.Background(), []string{"--tag", "v3.13.0", "--dry-run=true"}, repositoryEnv)
	if err != nil {
		t.Fatalf("parse with GITHUB_REPOSITORY: %v", err)
	}
	if options.Repository != unityPackageSigningTestRepository || options.Tag != "v3.13.0" || !options.DryRun {
		t.Fatalf("options = %+v, want repository from GITHUB_REPOSITORY, tag v3.13.0, and a dry run", options)
	}
	if _, err := os.Stat(filepath.Join(options.RepoRoot, ".release-please-manifest.json")); err != nil {
		t.Fatalf("default repository root %q has no release-please manifest: %v", options.RepoRoot, err)
	}

	if _, err := parseUnityPackageSigningOptions(context.Background(), []string{"--repo-root", t.TempDir()}, noEnv); err == nil {
		t.Fatal("parse without --repo or GITHUB_REPOSITORY succeeded, want an error")
	}
}

// Verifies malformed arguments exit with the usage status before any release is read.
func TestRunPlanUnityPackageSigningRejectsUnknownFlag(t *testing.T) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunPlanUnityPackageSigning(context.Background(), &stdout, &stderr, []string{"--unknown"})
	if code != 2 || stdout.Len() != 0 {
		t.Fatalf("exit code = %d, stdout = %q; want 2 without output", code, stdout.String())
	}
}
