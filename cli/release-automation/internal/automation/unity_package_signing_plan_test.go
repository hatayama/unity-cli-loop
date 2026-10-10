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
	unityPackageSigningTestRepository    = "example-owner/example-repo"
	unityPackageSigningTestReleaseCommit = "0123456789abcdef0123456789abcdef01234567"
	unityPackageSigningTestTagRef        = "refs/tags/v3.14.0"
	unityPackageSigningTestTarball       = `{"name": "io.github.hatayama.uloopmcp-3.14.0.tgz", "size": 2048}`
)

// fakeUnityPackageSigningGh answers the paginated release listing with fixed pages or an error,
// and counts how often it was asked.
type fakeUnityPackageSigningGh struct {
	t            *testing.T
	releasePages string
	failure      error
	calls        int
}

func (fake *fakeUnityPackageSigningGh) runOutput(_ context.Context, name string, args ...string) (string, error) {
	fake.t.Helper()
	if name != "gh" || len(args) < 3 || args[0] != "api" || args[1] != "--paginate" ||
		args[2] != "repos/"+unityPackageSigningTestRepository+"/releases?per_page=100" {
		fake.t.Fatalf("unexpected command %s %v", name, args)
	}
	fake.calls++
	if fake.failure != nil {
		return "", fake.failure
	}
	return fake.releasePages, nil
}

// unityPackageTestRelease renders one release as the listing's jq filter emits it.
func unityPackageTestRelease(tag string, draft bool, target string, assets ...string) string {
	return fmt.Sprintf(`{"tag_name": %q, "draft": %t, "target_commitish": %q, "assets": [%s]}`, tag, draft, target, strings.Join(assets, ", "))
}

// unityPackageTestPage renders one page of the listing, which the paginated call prints back to
// back with the other pages.
func unityPackageTestPage(releases ...string) string {
	return "[" + strings.Join(releases, ", ") + "]\n"
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

// Verifies the plan signs and publishes only a draft release, because an immutable published
// release can no longer take the tarball, and that a dry run signs any existing release without
// publishing it. A draft has no tag yet, so its source is the commit the release targets.
func TestPlanUnityPackageSigningDecidesFromReleaseState(t *testing.T) {
	draftWithoutTarball := unityPackageTestRelease("v3.14.0", true, unityPackageSigningTestReleaseCommit)
	publishedWithTarball := unityPackageTestRelease("v3.14.0", false, "main", unityPackageSigningTestTarball)
	otherComponent := unityPackageTestRelease("dispatcher-v3.8.1", false, "main", `{"name": "uloop-dispatcher-linux-amd64.tar.gz", "size": 10}`)
	tests := []struct {
		name          string
		releasePages  string
		dryRun        bool
		wantSign      bool
		wantPublish   bool
		wantSourceRef string
		wantWarning   bool
	}{
		{
			name:          "draft release without the tarball is signed and published",
			releasePages:  unityPackageTestPage(otherComponent, draftWithoutTarball),
			wantSign:      true,
			wantPublish:   true,
			wantSourceRef: unityPackageSigningTestReleaseCommit,
		},
		{
			name:          "draft carrying a tarball from an interrupted publish is signed and published again",
			releasePages:  unityPackageTestPage(unityPackageTestRelease("v3.14.0", true, unityPackageSigningTestReleaseCommit, unityPackageSigningTestTarball)),
			wantSign:      true,
			wantPublish:   true,
			wantSourceRef: unityPackageSigningTestReleaseCommit,
		},
		{
			name:          "draft on a later page is found",
			releasePages:  unityPackageTestPage(otherComponent) + unityPackageTestPage(draftWithoutTarball),
			wantSign:      true,
			wantPublish:   true,
			wantSourceRef: unityPackageSigningTestReleaseCommit,
		},
		{
			name:          "dry run signs a draft without publishing it",
			releasePages:  unityPackageTestPage(draftWithoutTarball),
			dryRun:        true,
			wantSign:      true,
			wantSourceRef: unityPackageSigningTestReleaseCommit,
		},
		{
			name:          "published release that carries the tarball is skipped",
			releasePages:  unityPackageTestPage(publishedWithTarball),
			wantSourceRef: unityPackageSigningTestTagRef,
		},
		{
			name:          "published release without the tarball is skipped with a warning",
			releasePages:  unityPackageTestPage(unityPackageTestRelease("v3.14.0", false, "main", `{"name": "uloop-dispatcher-linux-amd64.tar.gz", "size": 10}`)),
			wantSourceRef: unityPackageSigningTestTagRef,
			wantWarning:   true,
		},
		{
			name:          "published release with an empty tarball is skipped with a warning",
			releasePages:  unityPackageTestPage(unityPackageTestRelease("v3.14.0", false, "main", `{"name": "io.github.hatayama.uloopmcp-3.14.0.tgz", "size": 0}`)),
			wantSourceRef: unityPackageSigningTestTagRef,
			wantWarning:   true,
		},
		{
			name:          "published release outranks a leftover draft of the same tag",
			releasePages:  unityPackageTestPage(draftWithoutTarball, publishedWithTarball),
			wantSourceRef: unityPackageSigningTestTagRef,
		},
		{
			name:          "dry run signs a published release from its tag",
			releasePages:  unityPackageTestPage(publishedWithTarball),
			dryRun:        true,
			wantSign:      true,
			wantSourceRef: unityPackageSigningTestTagRef,
		},
		{
			name:         "missing release is skipped until the release sync creates it",
			releasePages: unityPackageTestPage(otherComponent),
		},
		{
			name:         "dry run skips a missing release",
			releasePages: unityPackageTestPage(),
			dryRun:       true,
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			fake := &fakeUnityPackageSigningGh{t: t, releasePages: test.releasePages}
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

// Verifies a draft the plan cannot sign safely fails without GITHUB_OUTPUT lines. A draft has no
// tag yet, so a branch target would sign whatever that branch holds at signing time, and with two
// drafts of one tag the upload and the publish could each pick a different one.
func TestPlanUnityPackageSigningRejectsUnsafeDrafts(t *testing.T) {
	tests := []struct {
		name         string
		releasePages string
	}{
		{name: "draft targeting a branch", releasePages: unityPackageTestPage(unityPackageTestRelease("v3.14.0", true, "main"))},
		{name: "draft without a target", releasePages: unityPackageTestPage(unityPackageTestRelease("v3.14.0", true, ""))},
		{name: "draft targeting an abbreviated commit", releasePages: unityPackageTestPage(unityPackageTestRelease("v3.14.0", true, "0123456"))},
		{
			name: "two drafts of the same tag",
			releasePages: unityPackageTestPage(
				unityPackageTestRelease("v3.14.0", true, unityPackageSigningTestReleaseCommit),
				unityPackageTestRelease("v3.14.0", true, unityPackageSigningTestReleaseCommit),
			),
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			fake := &fakeUnityPackageSigningGh{t: t, releasePages: test.releasePages}
			code, stdout, _ := runPlanUnityPackageSigningForTest(t, fake, "", false)
			if code == 0 || stdout != "" {
				t.Fatalf("exit code = %d, stdout = %q; want a failure without GITHUB_OUTPUT lines", code, stdout)
			}
		})
	}
}

// Verifies an explicit tag replaces the manifest version, so a manual run can sign an older release.
func TestPlanUnityPackageSigningUsesExplicitTag(t *testing.T) {
	fake := &fakeUnityPackageSigningGh{t: t, releasePages: unityPackageTestPage(
		unityPackageTestRelease("v3.14.0", false, "main", unityPackageSigningTestTarball),
		unityPackageTestRelease("v3.13.0", true, unityPackageSigningTestReleaseCommit),
	)}
	code, stdout, stderr := runPlanUnityPackageSigningForTest(t, fake, "v3.13.0", false)
	if code != 0 {
		t.Fatalf("exit code = %d, stderr = %s", code, stderr)
	}
	if !strings.HasPrefix(stdout, "sign=true\n") ||
		!strings.Contains(stdout, "version=3.13.0\n") ||
		!strings.Contains(stdout, "asset-name=io.github.hatayama.uloopmcp-3.13.0.tgz\n") {
		t.Fatalf("stdout = %q, want the v3.13.0 draft signed under its own version and asset name", stdout)
	}
}

// Verifies tags of the other release components are rejected before any release is read, because
// signing the package source at a CLI tag would publish a mislabeled package.
func TestPlanUnityPackageSigningRejectsNonPackageTags(t *testing.T) {
	for _, tag := range []string{"dispatcher-v3.8.1", "uloop-project-runner-v3.8.0", "3.14.0", "v3.14"} {
		t.Run(tag, func(t *testing.T) {
			fake := &fakeUnityPackageSigningGh{t: t, releasePages: unityPackageTestPage()}
			code, stdout, _ := runPlanUnityPackageSigningForTest(t, fake, tag, false)
			if code == 0 {
				t.Fatalf("exit code = 0, stdout = %q; want a failure", stdout)
			}
			if fake.calls != 0 {
				t.Fatalf("release listing calls = %d, want none", fake.calls)
			}
		})
	}
}

// Verifies a failed release listing fails the run instead of skipping, so an authentication or API
// outage cannot be mistaken for a release that does not exist yet.
func TestPlanUnityPackageSigningFailsOnGhError(t *testing.T) {
	fake := &fakeUnityPackageSigningGh{t: t, failure: errors.New("gh api failed: exit status 1\nHTTP 401: Bad credentials")}
	code, stdout, _ := runPlanUnityPackageSigningForTest(t, fake, "", false)
	if code == 0 {
		t.Fatalf("exit code = 0, stdout = %q; want a failure", stdout)
	}
	if stdout != "" {
		t.Fatalf("stdout = %q, want no GITHUB_OUTPUT lines", stdout)
	}
}

// Verifies an unusable manifest or release listing fails the run without GITHUB_OUTPUT lines,
// so the workflow never signs a guessed tag or acts on releases it could not read.
func TestPlanUnityPackageSigningFailsOnUnusableInputs(t *testing.T) {
	tests := []struct {
		name         string
		manifest     string
		releasePages string
		wantCalls    int
	}{
		{name: "manifest without the package entry", manifest: `{"cli/dispatcher": "3.8.1"}`},
		{name: "manifest that is not JSON", manifest: `Packages/src: 3.14.0`},
		{name: "missing manifest"},
		{
			name:         "release listing that is not JSON",
			manifest:     `{"Packages/src": "3.14.0"}`,
			releasePages: "Not JSON",
			wantCalls:    1,
		},
		{
			name:         "release listing cut off mid-page",
			manifest:     `{"Packages/src": "3.14.0"}`,
			releasePages: `[{"tag_name": "v3.14.0", "draft": true`,
			wantCalls:    1,
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			repoRoot := t.TempDir()
			if test.manifest != "" {
				repoRoot = writeUnityPackageSigningManifest(t, test.manifest)
			}
			fake := &fakeUnityPackageSigningGh{t: t, releasePages: test.releasePages}
			code, stdout, _ := runPlanUnityPackageSigningInRootForTest(fake, repoRoot, "", false)
			if code == 0 || stdout != "" {
				t.Fatalf("exit code = %d, stdout = %q; want a failure without GITHUB_OUTPUT lines", code, stdout)
			}
			if fake.calls != test.wantCalls {
				t.Fatalf("release listing calls = %d, want %d", fake.calls, test.wantCalls)
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
