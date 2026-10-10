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

func runPlanUnityPackageSigningForTest(t *testing.T, fake *fakeUnityPackageSigningGh, tag string, dryRun bool) (int, string, string) {
	t.Helper()
	repoRoot := writeUnityPackageSigningManifest(t, `{"Packages/src": "3.14.0", "cli/dispatcher": "3.8.1"}`)
	return runPlanUnityPackageSigningInRootForTest(fake, repoRoot, tag, dryRun)
}

func runPlanUnityPackageSigningInRootForTest(fake *fakeUnityPackageSigningGh, repoRoot string, tag string, dryRun bool) (int, string, string) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPlanUnityPackageSigningWithDeps(context.Background(), &stdout, &stderr, unityPackageSigningOptions{
		Repository: unityPackageSigningTestRepository,
		RepoRoot:   repoRoot,
		Tag:        tag,
		DryRun:     dryRun,
	}, unityPackageSigningDeps{runOutput: fake.runOutput})
	return code, stdout.String(), stderr.String()
}

const (
	unityPackageSigningTestReleaseCommit = "0123456789abcdef0123456789abcdef01234567"
	unityPackageSigningTestTagRef        = "refs/tags/v3.14.0"
)

// Verifies the plan signs and publishes only a draft release, because an immutable published
// release can no longer take the tarball, and that a dry run signs any existing release without
// publishing it. A draft has no tag yet, so its source is the commit the release targets.
func TestPlanUnityPackageSigningDecidesFromReleaseState(t *testing.T) {
	const draftWithoutTarball = `{"isDraft": true, "targetCommitish": "` + unityPackageSigningTestReleaseCommit + `", "assets": []}`
	const publishedWithTarball = `{"isDraft": false, "targetCommitish": "main", "assets": [{"name": "io.github.hatayama.uloopmcp-3.14.0.tgz", "size": 2048}]}`
	const publishedWithoutTarball = `{"isDraft": false, "targetCommitish": "main", "assets": [{"name": "uloop-dispatcher-linux-amd64.tar.gz", "size": 10}]}`
	tests := []struct {
		name          string
		releaseJSON   string
		failure       error
		dryRun        bool
		wantSign      bool
		wantPublish   bool
		wantSourceRef string
		wantWarning   bool
	}{
		{
			name:          "draft release without the tarball is signed and published",
			releaseJSON:   draftWithoutTarball,
			wantSign:      true,
			wantPublish:   true,
			wantSourceRef: unityPackageSigningTestReleaseCommit,
		},
		{
			name:          "draft carrying a tarball from an interrupted publish is signed and published again",
			releaseJSON:   `{"isDraft": true, "targetCommitish": "` + unityPackageSigningTestReleaseCommit + `", "assets": [{"name": "io.github.hatayama.uloopmcp-3.14.0.tgz", "size": 2048}]}`,
			wantSign:      true,
			wantPublish:   true,
			wantSourceRef: unityPackageSigningTestReleaseCommit,
		},
		{
			name:          "dry run signs a draft without publishing it",
			releaseJSON:   draftWithoutTarball,
			dryRun:        true,
			wantSign:      true,
			wantSourceRef: unityPackageSigningTestReleaseCommit,
		},
		{
			name:          "published release that carries the tarball is skipped",
			releaseJSON:   publishedWithTarball,
			wantSourceRef: unityPackageSigningTestTagRef,
		},
		{
			name:          "published release without the tarball is skipped with a warning",
			releaseJSON:   publishedWithoutTarball,
			wantSourceRef: unityPackageSigningTestTagRef,
			wantWarning:   true,
		},
		{
			name:          "published release with an empty tarball is skipped with a warning",
			releaseJSON:   `{"isDraft": false, "targetCommitish": "main", "assets": [{"name": "io.github.hatayama.uloopmcp-3.14.0.tgz", "size": 0}]}`,
			wantSourceRef: unityPackageSigningTestTagRef,
			wantWarning:   true,
		},
		{
			name:          "dry run signs a published release from its tag",
			releaseJSON:   publishedWithTarball,
			dryRun:        true,
			wantSign:      true,
			wantSourceRef: unityPackageSigningTestTagRef,
		},
		{
			name:    "missing release is skipped until the release sync creates it",
			failure: errors.New("gh release view v3.14.0 failed: exit status 1\nrelease not found"),
		},
		{
			name:    "dry run skips a missing release",
			failure: errors.New("gh release view v3.14.0 failed: exit status 1\nrelease not found"),
			dryRun:  true,
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			fake := &fakeUnityPackageSigningGh{t: t, releaseJSON: test.releaseJSON, failure: test.failure}
			code, stdout, stderr := runPlanUnityPackageSigningForTest(t, fake, "", test.dryRun)
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
			if hasWarning := strings.HasPrefix(stderr, "::warning::"); hasWarning != test.wantWarning {
				t.Fatalf("stderr = %q, want warning annotation = %t", stderr, test.wantWarning)
			}
		})
	}
}

// Verifies a draft whose target is not a full commit SHA fails without GITHUB_OUTPUT lines. A
// draft has no tag yet, so a branch target would sign whatever that branch holds at signing time
// instead of the release commit.
func TestPlanUnityPackageSigningRejectsDraftWithoutCommitTarget(t *testing.T) {
	for _, target := range []string{"main", "", "0123456"} {
		t.Run(target, func(t *testing.T) {
			fake := &fakeUnityPackageSigningGh{t: t, releaseJSON: `{"isDraft": true, "targetCommitish": "` + target + `", "assets": []}`}
			code, stdout, _ := runPlanUnityPackageSigningForTest(t, fake, "", false)
			if code == 0 || stdout != "" {
				t.Fatalf("exit code = %d, stdout = %q; want a failure without GITHUB_OUTPUT lines", code, stdout)
			}
		})
	}
}

// Verifies an explicit tag replaces the manifest version, so a manual run can sign an older release.
func TestPlanUnityPackageSigningUsesExplicitTag(t *testing.T) {
	fake := &fakeUnityPackageSigningGh{t: t, releaseJSON: `{"isDraft": false, "assets": []}`}
	code, stdout, stderr := runPlanUnityPackageSigningForTest(t, fake, "v3.13.0", false)
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

// Verifies tags of the other release components are rejected before any release is read, because
// signing the package source at a CLI tag would publish a mislabeled package.
func TestPlanUnityPackageSigningRejectsNonPackageTags(t *testing.T) {
	for _, tag := range []string{"dispatcher-v3.8.1", "uloop-project-runner-v3.8.0", "3.14.0", "v3.14"} {
		t.Run(tag, func(t *testing.T) {
			fake := &fakeUnityPackageSigningGh{t: t, releaseJSON: `{"isDraft": false, "assets": []}`}
			code, stdout, _ := runPlanUnityPackageSigningForTest(t, fake, tag, false)
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
	code, stdout, _ := runPlanUnityPackageSigningForTest(t, fake, "", false)
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
			code, stdout, _ := runPlanUnityPackageSigningInRootForTest(fake, repoRoot, "", false)
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
