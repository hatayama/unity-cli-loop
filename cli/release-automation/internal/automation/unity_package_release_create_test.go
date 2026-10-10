package automation

import (
	"bytes"
	"context"
	"errors"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
)

const (
	unityPackageReleaseTestConfig    = `{"packages": {"Packages/src": {"changelog-path": "CHANGELOG.md"}, "cli/dispatcher": {"changelog-path": "CHANGELOG.md"}}}`
	unityPackageReleaseTestChangelog = "# Changelog\n\n" +
		"## [3.15.0](https://example.com/compare/v3.14.0...v3.15.0) (2026-10-12)\n\n### Features\n\n* later\n\n" +
		"## [3.14.0](https://example.com/compare/v3.13.0...v3.14.0) (2026-10-01)\n\n### Bug Fixes\n\n* signed\n\n" +
		"## [3.13.0](https://example.com/compare/v3.12.0...v3.13.0) (2026-09-20)\n\n* earlier\n"
	unityPackageReleaseTestNotes = "## [3.14.0](https://example.com/compare/v3.13.0...v3.14.0) (2026-10-01)\n\n### Bug Fixes\n\n* signed\n"
)

// unityPackageReleaseTestTag renders the git ref lookup of a lightweight tag at the commit.
func unityPackageReleaseTestTag(commit string) fakeUnityPackageReleaseResponse {
	return fakeUnityPackageReleaseResponse{output: `{"ref": "refs/tags/v3.14.0", "object": {"sha": "` + commit + `", "type": "commit"}}`}
}

// unityPackageReleaseTestCreateResponses answers every call of a creation that starts from
// nothing: no release, no tag, no leftover draft. Tests override the kinds they exercise.
func unityPackageReleaseTestCreateResponses(overrides map[string][]fakeUnityPackageReleaseResponse) map[string][]fakeUnityPackageReleaseResponse {
	responses := map[string][]fakeUnityPackageReleaseResponse{
		"release":   {unityPackageReleaseTestNotFound, {output: unityPackageReleaseTestPublished}},
		"tag":       {unityPackageReleaseTestNotFound, unityPackageReleaseTestTag(unityPackageReleaseTestCommit)},
		"drafts":    {{output: "[]\n"}},
		"config":    {{output: unityPackageReleaseTestConfig}},
		"changelog": {{output: unityPackageReleaseTestChangelog}},
		"delete":    {{}},
		"ref":       {{}},
		"create":    {{}},
	}
	for kind, queue := range overrides {
		responses[kind] = queue
	}
	return responses
}

// writeUnityPackageReleaseTestAsset writes a signed tarball stand-in under the asset name.
func writeUnityPackageReleaseTestAsset(t *testing.T, name string, content string) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), name)
	if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
		t.Fatalf("write asset: %v", err)
	}
	return path
}

func unityPackageReleaseTestCreateOptions(t *testing.T) unityPackageReleaseCreateOptions {
	return unityPackageReleaseCreateOptions{
		Repository: unityPackageReleaseTestRepository,
		RepoRoot:   t.TempDir(),
		Tag:        "v3.14.0",
		Commit:     unityPackageReleaseTestCommit,
		Asset:      writeUnityPackageReleaseTestAsset(t, "io.github.hatayama.uloopmcp-3.14.0.tgz", "signed"),
	}
}

func runCreateUnityPackageReleaseForTest(fake *fakeUnityPackageReleaseCommands, options unityPackageReleaseCreateOptions) (int, string) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runCreateUnityPackageReleaseWithDeps(context.Background(), &stdout, &stderr, options,
		unityPackageReleaseCreateDeps{runOutput: fake.runOutput, sleep: fake.sleep})
	return code, stdout.String() + stderr.String()
}

func unityPackageReleaseTestDraftPage(drafts ...string) string {
	return "[" + strings.Join(drafts, ", ") + "]\n"
}

// Verifies the release is created only after every leftover draft of its tag is deleted and its
// tag exists at the release commit, that a tag already at that commit is reused, and that reads
// lagging the writes are retried instead of failing a release that was created.
func TestCreateUnityPackageReleaseWritesInOrder(t *testing.T) {
	leftoverDraft := `{"id": 11, "tag_name": "v3.14.0", "draft": true}`
	tests := []struct {
		name        string
		overrides   map[string][]fakeUnityPackageReleaseResponse
		wantWrites  []string
		wantDeleted []string
		wantSleeps  int
	}{
		{
			name:       "missing tag is created before the release",
			wantWrites: []string{"ref", "create"},
		},
		{
			name:       "existing matching tag is reused",
			overrides:  map[string][]fakeUnityPackageReleaseResponse{"tag": {unityPackageReleaseTestTag(unityPackageReleaseTestCommit)}},
			wantWrites: []string{"create"},
		},
		{
			name: "leftover drafts are deleted first",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"drafts": {{output: unityPackageReleaseTestDraftPage(
				leftoverDraft,
				`{"id": 12, "tag_name": "v3.13.0", "draft": true}`,
				`{"id": 13, "tag_name": "v3.14.0", "draft": true}`,
			)}}},
			wantWrites:  []string{"delete", "delete", "ref", "create"},
			wantDeleted: []string{"11", "13"},
		},
		{
			name: "leftover drafts are deleted when the tag already exists",
			overrides: map[string][]fakeUnityPackageReleaseResponse{
				"tag":    {unityPackageReleaseTestTag(unityPackageReleaseTestCommit)},
				"drafts": {{output: unityPackageReleaseTestDraftPage(leftoverDraft)}},
			},
			wantWrites:  []string{"delete", "create"},
			wantDeleted: []string{"11"},
		},
		{
			name: "leftover draft on a later page is deleted",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"drafts": {{output: unityPackageReleaseTestDraftPage(
				`{"id": 12, "tag_name": "v3.13.0", "draft": false}`,
			) + unityPackageReleaseTestDraftPage(leftoverDraft)}}},
			wantWrites:  []string{"delete", "ref", "create"},
			wantDeleted: []string{"11"},
		},
		{
			name: "postcondition rereads a lagging release",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"release": {
				unityPackageReleaseTestNotFound,
				unityPackageReleaseTestNotFound,
				{output: unityPackageReleaseTestPublished},
			}},
			wantWrites: []string{"ref", "create"},
			wantSleeps: 1,
		},
		{
			name: "created tag is reread until it is visible",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"tag": {
				unityPackageReleaseTestNotFound,
				unityPackageReleaseTestNotFound,
				unityPackageReleaseTestTag(unityPackageReleaseTestCommit),
			}},
			wantWrites: []string{"ref", "create"},
			wantSleeps: 1,
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			fake := newFakeUnityPackageReleaseCommands(t, unityPackageReleaseTestCreateResponses(test.overrides))
			options := unityPackageReleaseTestCreateOptions(t)
			code, output := runCreateUnityPackageReleaseForTest(fake, options)
			if code != 0 {
				t.Fatalf("exit code = %d, output = %s", code, output)
			}
			if writes := fake.writes(); !reflect.DeepEqual(writes, test.wantWrites) {
				t.Fatalf("writes = %v, want %v", writes, test.wantWrites)
			}
			var deleted []string
			for index, kind := range fake.calls {
				if kind == "delete" {
					command := fake.commands[index]
					deleted = append(deleted, command[len(command)-1][strings.LastIndex(command[len(command)-1], "/")+1:])
				}
			}
			if !reflect.DeepEqual(deleted, test.wantDeleted) {
				t.Fatalf("deleted drafts = %v, want %v", deleted, test.wantDeleted)
			}
			if fake.sleeps != test.wantSleeps {
				t.Fatalf("sleeps = %d, want %d", fake.sleeps, test.wantSleeps)
			}
			wantCreate := []string{
				"gh", "release", "create", "v3.14.0", options.Asset, "--repo", unityPackageReleaseTestRepository,
				"--verify-tag", "--title", "v3.14.0", "--notes=" + unityPackageReleaseTestNotes, "--target", unityPackageReleaseTestCommit,
			}
			if create := fake.command("create"); !reflect.DeepEqual(create, wantCreate) {
				t.Fatalf("create command = %q, want %q", create, wantCreate)
			}
		})
	}
}

// Verifies a pre-release version is created as a pre-release, as the release sync did.
func TestCreateUnityPackageReleaseMarksPreReleases(t *testing.T) {
	fake := newFakeUnityPackageReleaseCommands(t, unityPackageReleaseTestCreateResponses(map[string][]fakeUnityPackageReleaseResponse{
		"release":   {unityPackageReleaseTestNotFound, {output: `{"tag_name": "v3.15.0-beta.1", "draft": false, "assets": [{"name": "io.github.hatayama.uloopmcp-3.15.0-beta.1.tgz", "size": 2048}]}`}},
		"tag":       {{output: `{"ref": "refs/tags/v3.15.0-beta.1", "object": {"sha": "` + unityPackageReleaseTestCommit + `", "type": "commit"}}`}},
		"changelog": {{output: "## [3.15.0-beta.1](https://example.com) (2026-10-12)\n\n* beta\n"}},
	}))
	fake.tag = "v3.15.0-beta.1"
	options := unityPackageReleaseTestCreateOptions(t)
	options.Tag = "v3.15.0-beta.1"
	options.Asset = writeUnityPackageReleaseTestAsset(t, "io.github.hatayama.uloopmcp-3.15.0-beta.1.tgz", "signed")
	code, output := runCreateUnityPackageReleaseForTest(fake, options)
	if code != 0 {
		t.Fatalf("exit code = %d, output = %s", code, output)
	}
	if create := fake.command("create"); create[len(create)-1] != "--prerelease" {
		t.Fatalf("create command = %v, want --prerelease", create)
	}
}

// Verifies the release notes come from the changelog path the release-please config names at the
// release commit, resolved the way release-please resolves it: from the repository root when it
// starts with "/" and from the package directory otherwise.
func TestCreateUnityPackageReleaseReadsTheConfiguredChangelog(t *testing.T) {
	tests := []struct {
		name          string
		changelogPath string
		wantPath      string
	}{
		{name: "package-relative path", changelogPath: "CHANGELOG.md", wantPath: "Packages/src/CHANGELOG.md"},
		{name: "repository-relative path", changelogPath: "/docs/CHANGELOG.md", wantPath: "docs/CHANGELOG.md"},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			fake := newFakeUnityPackageReleaseCommands(t, unityPackageReleaseTestCreateResponses(map[string][]fakeUnityPackageReleaseResponse{
				"config": {{output: `{"packages": {"Packages/src": {"changelog-path": "` + test.changelogPath + `"}}}`}},
			}))
			options := unityPackageReleaseTestCreateOptions(t)
			code, output := runCreateUnityPackageReleaseForTest(fake, options)
			if code != 0 {
				t.Fatalf("exit code = %d, output = %s", code, output)
			}
			want := []string{"git", "-C", options.RepoRoot, "show", unityPackageReleaseTestCommit + ":" + test.wantPath}
			if changelog := fake.command("changelog"); !reflect.DeepEqual(changelog, want) {
				t.Fatalf("changelog read = %q, want %q", changelog, want)
			}
		})
	}
}

// Verifies every reason not to create the release is found before the first write, so a failed
// run leaves neither a tag nor a deleted draft behind, and that a lookup failure is never read as
// "absent": each lookup is checked on its own so one cannot hide another.
func TestCreateUnityPackageReleaseStopsBeforeAnyWrite(t *testing.T) {
	failure := fakeUnityPackageReleaseResponse{err: errors.New("gh api failed: exit status 1\nHTTP 502: Bad Gateway")}
	leftoverDraft := map[string][]fakeUnityPackageReleaseResponse{"drafts": {{output: unityPackageReleaseTestDraftPage(`{"id": 11, "tag_name": "v3.14.0", "draft": true}`)}}}
	tests := []struct {
		name      string
		overrides map[string][]fakeUnityPackageReleaseResponse
		wantCode  int
	}{
		{
			name:      "rerun after the release exists is a no-op",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"release": {{output: unityPackageReleaseTestPublished}}},
		},
		{
			name:      "published release without the tarball is never recreated",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"release": {{output: unityPackageReleaseTestUnsigned}}},
			wantCode:  1,
		},
		{
			name:      "published release with an empty tarball is never recreated",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"release": {{output: unityPackageReleaseTestEmptyAsset}}},
			wantCode:  1,
		},
		{
			name: "mismatched tag fails before any write",
			overrides: map[string][]fakeUnityPackageReleaseResponse{
				"tag":    {unityPackageReleaseTestTag(unityPackageReleaseTestOtherCommit)},
				"drafts": leftoverDraft["drafts"],
			},
			wantCode: 1,
		},
		{
			name: "annotated tag fails before any write",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"tag": {{
				output: `{"ref": "refs/tags/v3.14.0", "object": {"sha": "` + unityPackageReleaseTestCommit + `", "type": "tag"}}`,
			}}},
			wantCode: 1,
		},
		{
			name:      "release lookup failure stops before any write",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"release": {failure}},
			wantCode:  1,
		},
		{
			name:      "tag lookup failure stops before any write",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"tag": {failure}, "drafts": leftoverDraft["drafts"]},
			wantCode:  1,
		},
		{
			name:      "draft listing failure stops before any write",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"drafts": {failure}},
			wantCode:  1,
		},
		{
			name:      "draft listing cut off mid-page stops before any write",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"drafts": {{output: `[{"id": 11, "tag_name": "v3.14.0"`}}},
			wantCode:  1,
		},
		{
			name: "changelog without the version heading fails before any write",
			overrides: map[string][]fakeUnityPackageReleaseResponse{
				"changelog": {{output: "# Changelog\n\n## [3.13.0](https://example.com) (2026-09-20)\n"}},
				"drafts":    leftoverDraft["drafts"],
			},
			wantCode: 1,
		},
		{
			name:      "changelog heading is matched literally",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"changelog": {{output: "## [3a14b0](https://example.com) (2026-10-01)\n\n* lookalike\n"}}},
			wantCode:  1,
		},
		{
			name:      "config without the package changelog path fails before any write",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"config": {{output: `{"packages": {"Packages/src": {}}}`}}},
			wantCode:  1,
		},
		{
			name:      "tag lookup that is not JSON stops before any write",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"tag": {{output: "Not JSON"}}},
			wantCode:  1,
		},
		{
			name:      "config that is not JSON fails before any write",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"config": {{output: "Not JSON"}}},
			wantCode:  1,
		},
		{
			name:      "unreadable changelog at the release commit fails before any write",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"changelog": {{err: errors.New("git show failed: exit status 128")}}},
			wantCode:  1,
		},
		{
			name:      "unreadable config at the release commit fails before any write",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"config": {{err: errors.New("git show failed: exit status 128")}}},
			wantCode:  1,
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			fake := newFakeUnityPackageReleaseCommands(t, unityPackageReleaseTestCreateResponses(test.overrides))
			code, output := runCreateUnityPackageReleaseForTest(fake, unityPackageReleaseTestCreateOptions(t))
			if code != test.wantCode {
				t.Fatalf("exit code = %d, want %d; output = %s", code, test.wantCode, output)
			}
			if writes := fake.writes(); len(writes) != 0 {
				t.Fatalf("writes = %v, want none", writes)
			}
		})
	}
}

// Verifies a failure after the first write fails the run instead of reporting a release that
// OpenUPM cannot install: a release creation that fails, a tag that never becomes visible, and a
// release that never shows up published with its tarball.
func TestCreateUnityPackageReleaseFailsAfterWrites(t *testing.T) {
	tests := []struct {
		name       string
		overrides  map[string][]fakeUnityPackageReleaseResponse
		wantWrites []string
		wantSleeps int
	}{
		{
			name:       "failed creation fails",
			overrides:  map[string][]fakeUnityPackageReleaseResponse{"create": {{err: errors.New("gh release create failed: exit status 1")}}},
			wantWrites: []string{"ref", "create"},
		},
		{
			name: "failed draft deletion fails before the tag is created",
			overrides: map[string][]fakeUnityPackageReleaseResponse{
				"drafts": {{output: unityPackageReleaseTestDraftPage(`{"id": 11, "tag_name": "v3.14.0", "draft": true}`)}},
				"delete": {{err: errors.New("gh api failed: exit status 1\nHTTP 502: Bad Gateway")}},
			},
			wantWrites: []string{"delete"},
		},
		{
			name:       "failed tag creation fails before the release is created",
			overrides:  map[string][]fakeUnityPackageReleaseResponse{"ref": {{err: errors.New("gh api failed: exit status 1\nHTTP 422: Reference already exists")}}},
			wantWrites: []string{"ref"},
		},
		{
			name: "tag that appears at another commit stops before the release is created",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"tag": {
				unityPackageReleaseTestNotFound,
				unityPackageReleaseTestTag(unityPackageReleaseTestOtherCommit),
			}},
			wantWrites: []string{"ref"},
		},
		{
			name: "failed tag reread stops before the release is created",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"tag": {
				unityPackageReleaseTestNotFound,
				{err: errors.New("gh api failed: exit status 1\nHTTP 502: Bad Gateway")},
			}},
			wantWrites: []string{"ref"},
		},
		{
			name:       "tag that never becomes visible stops before the release is created",
			overrides:  map[string][]fakeUnityPackageReleaseResponse{"tag": {unityPackageReleaseTestNotFound}},
			wantWrites: []string{"ref"},
			wantSleeps: 2,
		},
		{
			name:       "release without its tarball fails",
			overrides:  map[string][]fakeUnityPackageReleaseResponse{"release": {unityPackageReleaseTestNotFound, {output: unityPackageReleaseTestUnsigned}}},
			wantWrites: []string{"ref", "create"},
			wantSleeps: 2,
		},
		{
			name:       "release that never appears fails",
			overrides:  map[string][]fakeUnityPackageReleaseResponse{"release": {unityPackageReleaseTestNotFound}},
			wantWrites: []string{"ref", "create"},
			wantSleeps: 2,
		},
		{
			name: "postcondition lookup failure fails",
			overrides: map[string][]fakeUnityPackageReleaseResponse{"release": {
				unityPackageReleaseTestNotFound,
				{err: errors.New("gh api failed: exit status 1\nHTTP 502: Bad Gateway")},
			}},
			wantWrites: []string{"ref", "create"},
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			fake := newFakeUnityPackageReleaseCommands(t, unityPackageReleaseTestCreateResponses(test.overrides))
			code, output := runCreateUnityPackageReleaseForTest(fake, unityPackageReleaseTestCreateOptions(t))
			if code != 1 {
				t.Fatalf("exit code = %d, want 1; output = %s", code, output)
			}
			if writes := fake.writes(); !reflect.DeepEqual(writes, test.wantWrites) {
				t.Fatalf("writes = %v, want %v", writes, test.wantWrites)
			}
			if fake.sleeps != test.wantSleeps {
				t.Fatalf("sleeps = %d, want %d", fake.sleeps, test.wantSleeps)
			}
		})
	}
}

// Verifies each malformed input exits with the usage status before gh or git is called, so the
// workflow never creates a release from a tag, commit, or tarball that does not belong to it.
func TestCreateUnityPackageReleaseRejectsInvalidInputs(t *testing.T) {
	tests := []struct {
		name   string
		mutate func(t *testing.T, options *unityPackageReleaseCreateOptions)
	}{
		{name: "tag of another component", mutate: func(_ *testing.T, options *unityPackageReleaseCreateOptions) {
			options.Tag = "dispatcher-v3.8.1"
		}},
		{name: "abbreviated commit", mutate: func(_ *testing.T, options *unityPackageReleaseCreateOptions) {
			options.Commit = "0123456"
		}},
		{name: "branch instead of a commit", mutate: func(_ *testing.T, options *unityPackageReleaseCreateOptions) {
			options.Commit = "main"
		}},
		{name: "missing asset", mutate: func(_ *testing.T, options *unityPackageReleaseCreateOptions) {
			options.Asset = filepath.Join(filepath.Dir(options.Asset), "missing", "io.github.hatayama.uloopmcp-3.14.0.tgz")
		}},
		{name: "empty asset", mutate: func(t *testing.T, options *unityPackageReleaseCreateOptions) {
			options.Asset = writeUnityPackageReleaseTestAsset(t, "io.github.hatayama.uloopmcp-3.14.0.tgz", "")
		}},
		{name: "asset of another version", mutate: func(t *testing.T, options *unityPackageReleaseCreateOptions) {
			options.Asset = writeUnityPackageReleaseTestAsset(t, "io.github.hatayama.uloopmcp-3.13.0.tgz", "signed")
		}},
		{name: "directory instead of an asset", mutate: func(t *testing.T, options *unityPackageReleaseCreateOptions) {
			directory := filepath.Join(t.TempDir(), "io.github.hatayama.uloopmcp-3.14.0.tgz")
			if err := os.Mkdir(directory, 0o700); err != nil {
				t.Fatalf("create directory: %v", err)
			}
			options.Asset = directory
		}},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			fake := newFakeUnityPackageReleaseCommands(t, map[string][]fakeUnityPackageReleaseResponse{})
			options := unityPackageReleaseTestCreateOptions(t)
			test.mutate(t, &options)
			code, output := runCreateUnityPackageReleaseForTest(fake, options)
			if code != 2 {
				t.Fatalf("exit code = %d, want 2; output = %s", code, output)
			}
			if len(fake.calls) != 0 {
				t.Fatalf("calls = %v, want none", fake.calls)
			}
		})
	}
}

// Verifies the required flags and the repository fallback to GITHUB_REPOSITORY.
func TestParseUnityPackageReleaseCreateOptions(t *testing.T) {
	repositoryEnv := func(name string) (string, bool) {
		if name == "GITHUB_REPOSITORY" {
			return unityPackageReleaseTestRepository, true
		}
		return "", false
	}
	complete := []string{"--tag", "v3.14.0", "--commit", unityPackageReleaseTestCommit, "--asset", "signed.tgz"}

	options, err := parseUnityPackageReleaseCreateOptions(context.Background(), complete, repositoryEnv)
	if err != nil {
		t.Fatalf("parse: %v", err)
	}
	if options.Repository != unityPackageReleaseTestRepository || options.Tag != "v3.14.0" ||
		options.Commit != unityPackageReleaseTestCommit || options.Asset != "signed.tgz" {
		t.Fatalf("options = %+v, want the flags and the repository from GITHUB_REPOSITORY", options)
	}
	if _, err := os.Stat(filepath.Join(options.RepoRoot, "release-please-config.json")); err != nil {
		t.Fatalf("default repository root %q has no release-please config: %v", options.RepoRoot, err)
	}

	noEnv := func(string) (string, bool) { return "", false }
	if _, err := parseUnityPackageReleaseCreateOptions(context.Background(), complete, noEnv); err == nil {
		t.Fatal("parse without --repo or GITHUB_REPOSITORY succeeded, want an error")
	}
	for _, flagName := range []string{"--tag", "--commit", "--asset"} {
		var args []string
		for index := 0; index < len(complete); index += 2 {
			if complete[index] != flagName {
				args = append(args, complete[index], complete[index+1])
			}
		}
		if _, err := parseUnityPackageReleaseCreateOptions(context.Background(), args, repositoryEnv); err == nil {
			t.Fatalf("parse without %s succeeded, want an error", flagName)
		}
	}
}

// Verifies the command entry point checks its inputs before it reaches GitHub, so a workflow that
// passes the wrong tarball fails with the usage status.
func TestRunCreateUnityPackageReleaseRejectsAnInvalidAsset(t *testing.T) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	missingAsset := filepath.Join(t.TempDir(), "io.github.hatayama.uloopmcp-3.14.0.tgz")
	code := RunCreateUnityPackageRelease(context.Background(), &stdout, &stderr, []string{
		"--repo", unityPackageReleaseTestRepository, "--repo-root", t.TempDir(),
		"--tag", "v3.14.0", "--commit", unityPackageReleaseTestCommit, "--asset", missingAsset,
	})
	if code != 2 || !strings.Contains(stderr.String(), "--asset") {
		t.Fatalf("exit code = %d, stderr = %q; want 2 naming --asset", code, stderr.String())
	}
}

// Verifies malformed arguments exit with the usage status before anything is read.
func TestRunCreateUnityPackageReleaseRejectsUnknownFlag(t *testing.T) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunCreateUnityPackageRelease(context.Background(), &stdout, &stderr, []string{"--unknown"})
	if code != 2 || stdout.Len() != 0 {
		t.Fatalf("exit code = %d, stdout = %q; want 2 without output", code, stdout.String())
	}
}
