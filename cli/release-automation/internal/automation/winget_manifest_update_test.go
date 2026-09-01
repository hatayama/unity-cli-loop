package automation

import (
	"bytes"
	"context"
	"encoding/base64"
	"errors"
	"fmt"
	"net/url"
	"strings"
	"testing"
)

const testWingetSHA256 = "abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd"

// TestUpdateWingetManifestSkipsWithoutToken verifies an unset winget token exits successfully without invoking gh.
func TestUpdateWingetManifestSkipsWithoutToken(t *testing.T) {
	t.Setenv(wingetPkgsTokenEnvName, "")
	calls := 0
	deps := wingetManifestUpdateDeps{
		runOutput: func(context.Context, []string, string, ...string) (string, error) {
			calls++
			return "", errors.New("runOutput must not be called when the winget token is unset")
		},
	}

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	code := runUpdateWingetManifestWithDeps(context.Background(), &stdout, &stderr, testWingetConfig("dispatcher-v3.1.0"), deps)

	if code != 0 {
		t.Fatalf("exit code = %d, stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "WINGET_PKGS_TOKEN is not configured; skipping winget manifest update.") {
		t.Fatalf("unexpected stdout: %s", stdout.String())
	}
	if calls != 0 {
		t.Fatalf("runOutput called %d times", calls)
	}
}

// TestUpdateWingetManifestSkipsPrerelease verifies prerelease tags skip before release assets are downloaded.
func TestUpdateWingetManifestSkipsPrerelease(t *testing.T) {
	t.Setenv(wingetPkgsTokenEnvName, "winget-token")
	calls := 0
	deps := wingetManifestUpdateDeps{
		runOutput: func(context.Context, []string, string, ...string) (string, error) {
			calls++
			return "", errors.New("runOutput must not be called for a prerelease")
		},
	}

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	code := runUpdateWingetManifestWithDeps(context.Background(), &stdout, &stderr, testWingetConfig("dispatcher-v3.2.0-beta.1"), deps)

	if code != 0 {
		t.Fatalf("exit code = %d, stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "dispatcher 3.2.0-beta.1 is a pre-release; winget receives stable releases only. Skipping.") {
		t.Fatalf("unexpected stdout: %s", stdout.String())
	}
	if calls != 0 {
		t.Fatalf("runOutput called %d times", calls)
	}
}

// TestUpdateWingetManifestSkipsGitHubPrerelease verifies release metadata prevents stable-looking prereleases from publishing.
func TestUpdateWingetManifestSkipsGitHubPrerelease(t *testing.T) {
	scenario := newWingetTestScenario()
	scenario.releasePrerelease = true
	stdout, code := runWingetTestScenarioWithTag(t, scenario, "dispatcher-v3.2.0")

	if code != 0 {
		t.Fatalf("exit code = %d", code)
	}
	if !strings.Contains(stdout, "GitHub release dispatcher-v3.2.0 is marked as a pre-release; winget receives stable releases only. Skipping.") {
		t.Fatalf("unexpected stdout: %s", stdout)
	}
	if scenario.putCalls != 0 || scenario.pullRequestCreateCalls != 0 {
		t.Fatalf("PUT calls = %d, PR creation calls = %d", scenario.putCalls, scenario.pullRequestCreateCalls)
	}
	assertWingetPublishSetupNotCalled(t, scenario)
}

// TestRenderWingetManifests verifies all three winget manifests render the exact release metadata.
func TestRenderWingetManifests(t *testing.T) {
	manifests, err := renderWingetManifests(wingetManifestData{
		Version:     "3.1.0",
		Repository:  "hatayama/unity-cli-loop",
		SHA256Upper: strings.ToUpper(testWingetSHA256),
		ReleaseDate: "2026-08-31",
	})
	if err != nil {
		t.Fatalf("renderWingetManifests failed: %v", err)
	}

	expected := map[string]string{
		"hatayama.uloop.yaml": `PackageIdentifier: hatayama.uloop
PackageVersion: 3.1.0
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.10.0
`,
		"hatayama.uloop.installer.yaml": `PackageIdentifier: hatayama.uloop
PackageVersion: 3.1.0
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
  - RelativeFilePath: uloop.exe
    PortableCommandAlias: uloop
Commands:
  - uloop
ReleaseDate: 2026-08-31
Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/hatayama/unity-cli-loop/releases/download/dispatcher-v3.1.0/uloop-dispatcher-windows-amd64.zip
    InstallerSha256: ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCD
ManifestType: installer
ManifestVersion: 1.10.0
`,
		"hatayama.uloop.locale.en-US.yaml": `PackageIdentifier: hatayama.uloop
PackageVersion: 3.1.0
PackageLocale: en-US
Publisher: hatayama
PublisherUrl: https://github.com/hatayama
PublisherSupportUrl: https://github.com/hatayama/unity-cli-loop/issues
PackageName: Unity CLI Loop
PackageUrl: https://github.com/hatayama/unity-cli-loop
License: MIT
LicenseUrl: https://github.com/hatayama/unity-cli-loop/blob/main/LICENSE
ShortDescription: Let AI drive Unity, from Editor to Play Mode
Moniker: uloop
Tags:
  - unity
  - cli
  - ai
ReleaseNotesUrl: https://github.com/hatayama/unity-cli-loop/releases/tag/dispatcher-v3.1.0
ManifestType: defaultLocale
ManifestVersion: 1.10.0
`,
	}

	if len(manifests) != len(expected) {
		t.Fatalf("manifest count = %d, want %d", len(manifests), len(expected))
	}
	for filename, expectedContent := range expected {
		if manifests[filename] != expectedContent {
			t.Fatalf("manifest %s mismatch:\n got:\n%s\nwant:\n%s", filename, manifests[filename], expectedContent)
		}
	}
	if wingetManifestSchemaVersion != "1.10.0" {
		t.Fatalf("manifest schema version = %q, want 1.10.0", wingetManifestSchemaVersion)
	}
}

// TestUpdateWingetManifestSkipsExistingVersion verifies an upstream version prevents manifest PUTs and PR creation.
func TestUpdateWingetManifestSkipsExistingVersion(t *testing.T) {
	scenario := newWingetTestScenario()
	scenario.versionExists = true
	stdout, code := runWingetTestScenario(t, scenario)

	if code != 0 {
		t.Fatalf("exit code = %d", code)
	}
	if !strings.Contains(stdout, "winget manifest for 3.1.0 already exists upstream; skipping.") {
		t.Fatalf("unexpected stdout: %s", stdout)
	}
	if scenario.putCalls != 0 || scenario.pullRequestCreateCalls != 0 {
		t.Fatalf("PUT calls = %d, PR creation calls = %d", scenario.putCalls, scenario.pullRequestCreateCalls)
	}
	if scenario.releaseDownloadCalls != 0 || scenario.releaseViewCalls != 0 {
		t.Fatalf("release download calls = %d, release view calls = %d", scenario.releaseDownloadCalls, scenario.releaseViewCalls)
	}
	assertWingetPublishSetupNotCalled(t, scenario)
}

// TestUpdateWingetManifestUsesNewPackageTitle verifies a missing upstream package uses the initial-submission PR title.
func TestUpdateWingetManifestUsesNewPackageTitle(t *testing.T) {
	scenario := newWingetTestScenario()
	scenario.packageExists = false
	_, code := runWingetTestScenario(t, scenario)

	if code != 0 {
		t.Fatalf("exit code = %d", code)
	}
	if scenario.pullRequestTitle != "New package: hatayama.uloop version 3.1.0" {
		t.Fatalf("PR title = %q", scenario.pullRequestTitle)
	}
}

// TestUpdateWingetManifestUsesNewVersionTitle verifies an existing upstream package uses the update PR title.
func TestUpdateWingetManifestUsesNewVersionTitle(t *testing.T) {
	scenario := newWingetTestScenario()
	scenario.packageExists = true
	_, code := runWingetTestScenario(t, scenario)

	if code != 0 {
		t.Fatalf("exit code = %d", code)
	}
	if scenario.pullRequestTitle != "New version: hatayama.uloop version 3.1.0" {
		t.Fatalf("PR title = %q", scenario.pullRequestTitle)
	}
}

// TestUpdateWingetManifestContinuesWhenBranchExists verifies a retry reaches all manifest PUTs after the branch already exists.
func TestUpdateWingetManifestContinuesWhenBranchExists(t *testing.T) {
	scenario := newWingetTestScenario()
	scenario.branchExists = true
	_, code := runWingetTestScenario(t, scenario)

	if code != 0 {
		t.Fatalf("exit code = %d", code)
	}
	if scenario.putCalls != 3 {
		t.Fatalf("PUT calls = %d, want 3", scenario.putCalls)
	}
}

// TestUpdateWingetManifestSkipsOpenPullRequest verifies an existing open PR prevents duplicate PR creation.
func TestUpdateWingetManifestSkipsOpenPullRequest(t *testing.T) {
	scenario := newWingetTestScenario()
	scenario.pullRequestOpen = true
	stdout, code := runWingetTestScenario(t, scenario)

	if code != 0 {
		t.Fatalf("exit code = %d", code)
	}
	if !strings.Contains(stdout, "winget-pkgs pull request is already open; skipping.") {
		t.Fatalf("unexpected stdout: %s", stdout)
	}
	if scenario.pullRequestCreateCalls != 0 {
		t.Fatalf("PR creation calls = %d", scenario.pullRequestCreateCalls)
	}
}

// TestWingetQueriesEncodeBuildMetadata verifies branch names with SemVer build metadata are URL encoded in query values.
func TestWingetQueriesEncodeBuildMetadata(t *testing.T) {
	endpoints := []string{}
	deps := wingetManifestUpdateDeps{
		runOutput: func(_ context.Context, _ []string, _ string, args ...string) (string, error) {
			endpoint := args[len(args)-1]
			endpoints = append(endpoints, endpoint)
			if strings.Contains(endpoint, "/contents/") {
				return "", errors.New("gh api failed: HTTP 404 Not Found")
			}
			return `[]`, nil
		},
	}
	branch := "hatayama-uloop-3.1.0+build"

	_, err := wingetForkContentSHA(context.Background(), deps, "token", "hatayama/winget-pkgs", branch, "manifest.yaml")
	if err != nil {
		t.Fatalf("wingetForkContentSHA failed: %v", err)
	}
	_, err = wingetPullRequestOpen(context.Background(), deps, "token", "hatayama", branch)
	if err != nil {
		t.Fatalf("wingetPullRequestOpen failed: %v", err)
	}

	joined := strings.Join(endpoints, "\n")
	if !strings.Contains(joined, "?ref=hatayama-uloop-3.1.0%2Bbuild") {
		t.Fatalf("encoded ref query missing from endpoints: %s", joined)
	}
	if !strings.Contains(joined, "?head=hatayama%3Ahatayama-uloop-3.1.0%2Bbuild&state=open") {
		t.Fatalf("encoded head query missing from endpoints: %s", joined)
	}
}

type wingetTestScenario struct {
	version                string
	versionExists          bool
	packageExists          bool
	branchExists           bool
	pullRequestOpen        bool
	releasePrerelease      bool
	releaseDownloadCalls   int
	releaseViewCalls       int
	mergeUpstreamCalls     int
	upstreamRefCalls       int
	branchCreationCalls    int
	putCalls               int
	pullRequestCreateCalls int
	pullRequestTitle       string
}

func newWingetTestScenario() *wingetTestScenario {
	return &wingetTestScenario{packageExists: true}
}

func testWingetConfig(tag string) wingetManifestUpdateConfig {
	return wingetManifestUpdateConfig{
		repository: "hatayama/unity-cli-loop",
		tag:        tag,
		forkRepo:   "hatayama/winget-pkgs",
	}
}

func runWingetTestScenario(t *testing.T, scenario *wingetTestScenario) (string, int) {
	t.Helper()
	return runWingetTestScenarioWithTag(t, scenario, "dispatcher-v3.1.0")
}

func runWingetTestScenarioWithTag(t *testing.T, scenario *wingetTestScenario, tag string) (string, int) {
	t.Helper()
	t.Setenv(wingetPkgsTokenEnvName, "winget-token")
	scenario.version = strings.TrimPrefix(tag, "dispatcher-v")
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	code := runUpdateWingetManifestWithDeps(
		context.Background(),
		&stdout,
		&stderr,
		testWingetConfig(tag),
		wingetManifestUpdateDeps{runOutput: scenario.runOutput},
	)
	if code != 0 && stderr.Len() > 0 {
		t.Logf("stderr: %s", stderr.String())
	}
	return stdout.String(), code
}

func (s *wingetTestScenario) runOutput(_ context.Context, extraEnv []string, name string, args ...string) (string, error) {
	joined := strings.Join(args, " ")
	if err := validateWingetTestEnvironment(extraEnv, joined); err != nil {
		return "", err
	}
	handlers := []func([]string, string) (string, bool, error){
		s.releaseOutput,
		s.upstreamOutput,
		s.branchOutput,
		s.manifestFileOutput,
		s.pullRequestOutput,
	}
	for _, handler := range handlers {
		output, handled, err := handler(args, joined)
		if handled {
			return output, err
		}
	}
	return "", errors.New("unexpected command: " + name + " " + joined + " env=" + strings.Join(extraEnv, ","))
}

func (s *wingetTestScenario) releaseOutput(_ []string, joined string) (string, bool, error) {
	if strings.Contains(joined, "release download") {
		s.releaseDownloadCalls++
		return testWingetSHA256 + "  " + wingetWindowsAmd64AssetName + "\n", true, nil
	}
	if strings.Contains(joined, "release view") {
		s.releaseViewCalls++
		return fmt.Sprintf(`{"publishedAt":"2026-08-31T12:34:56Z","isPrerelease":%t}`, s.releasePrerelease), true, nil
	}
	return "", false, nil
}

func (s *wingetTestScenario) upstreamOutput(_ []string, joined string) (string, bool, error) {
	versionPath := "repos/microsoft/winget-pkgs/contents/manifests/h/hatayama/uloop/" + s.version + "?ref=master"
	if strings.Contains(joined, versionPath) {
		if s.versionExists {
			return `{}`, true, nil
		}
		return "", true, errors.New("gh api failed: HTTP 404 Not Found")
	}
	if strings.Contains(joined, "repos/microsoft/winget-pkgs/contents/manifests/h/hatayama/uloop?ref=master") {
		if s.packageExists {
			return `{}`, true, nil
		}
		return "", true, errors.New("gh api failed: HTTP 404 Not Found")
	}
	if strings.Contains(joined, "repos/hatayama/winget-pkgs/merge-upstream") {
		s.mergeUpstreamCalls++
		return `{}`, true, nil
	}
	if strings.Contains(joined, "repos/microsoft/winget-pkgs/git/ref/heads/master") {
		s.upstreamRefCalls++
		return "upstream-master-sha\n", true, nil
	}
	return "", false, nil
}

func (s *wingetTestScenario) branchOutput(args []string, joined string) (string, bool, error) {
	if strings.Contains(joined, "repos/hatayama/winget-pkgs/git/refs") {
		s.branchCreationCalls++
		if err := validateWingetBranchCreationArgs(args, s.version); err != nil {
			return "", true, err
		}
		if s.branchExists {
			return "", true, errors.New("gh api failed: HTTP 422 Reference already exists")
		}
		return `{}`, true, nil
	}
	return "", false, nil
}

func (s *wingetTestScenario) manifestFileOutput(args []string, joined string) (string, bool, error) {
	versionPath := "repos/hatayama/winget-pkgs/contents/manifests/h/hatayama/uloop/" + s.version + "/"
	if strings.Contains(joined, versionPath) {
		if strings.Contains(joined, "-X PUT") {
			s.putCalls++
			decodedContent, err := validateWingetManifestPutArgs(args, s.version)
			if err != nil {
				return "", true, err
			}
			if strings.Contains(string(decodedContent), "InstallerSha256:") && !strings.Contains(string(decodedContent), strings.ToUpper(testWingetSHA256)) {
				return "", true, errors.New("installer sha256 was not uppercased")
			}
			return `{}`, true, nil
		}
		return "", true, errors.New("gh api failed: HTTP 404 Not Found")
	}
	return "", false, nil
}

func (s *wingetTestScenario) pullRequestOutput(args []string, joined string) (string, bool, error) {
	headQuery := "repos/microsoft/winget-pkgs/pulls?head=" + url.QueryEscape("hatayama:hatayama-uloop-"+s.version) + "&state=open"
	if strings.Contains(joined, headQuery) {
		if s.pullRequestOpen {
			return `[{"html_url":"https://example.invalid/existing"}]`, true, nil
		}
		return `[]`, true, nil
	}
	if strings.Contains(joined, "repos/microsoft/winget-pkgs/pulls") && strings.Contains(joined, "-X POST") {
		s.pullRequestCreateCalls++
		if err := validateWingetPullRequestArgs(args, s.version); err != nil {
			return "", true, err
		}
		s.pullRequestTitle = flagValue(args, "title")
		return `{"html_url":"https://example.invalid/new"}`, true, nil
	}
	return "", false, nil
}

func validateWingetTestEnvironment(extraEnv []string, joined string) error {
	if strings.Contains(joined, "release download") || strings.Contains(joined, "release view") {
		if len(extraEnv) != 0 {
			return fmt.Errorf("gh release call has unexpected environment: %v", extraEnv)
		}
		return nil
	}
	if len(extraEnv) != 1 || extraEnv[0] != "GH_TOKEN=winget-token" {
		return fmt.Errorf("gh api call environment = %v, want GH_TOKEN=winget-token", extraEnv)
	}
	return nil
}

func validateWingetBranchCreationArgs(args []string, version string) error {
	expectedRef := "refs/heads/hatayama-uloop-" + version
	if flagValue(args, "ref") != expectedRef {
		return fmt.Errorf("branch ref = %q, want %q", flagValue(args, "ref"), expectedRef)
	}
	if flagValue(args, "sha") != "upstream-master-sha" {
		return fmt.Errorf("branch sha = %q, want upstream-master-sha", flagValue(args, "sha"))
	}
	return nil
}

func validateWingetManifestPutArgs(args []string, version string) ([]byte, error) {
	if flagValue(args, "message") == "" {
		return nil, errors.New("manifest PUT message is empty")
	}
	encodedContent := flagValue(args, "content")
	if encodedContent == "" {
		return nil, errors.New("manifest PUT content is empty")
	}
	decodedContent, err := base64.StdEncoding.DecodeString(encodedContent)
	if err != nil {
		return nil, fmt.Errorf("manifest PUT content is not valid base64: %w", err)
	}
	if len(decodedContent) == 0 {
		return nil, errors.New("manifest PUT decoded content is empty")
	}
	expectedBranch := "hatayama-uloop-" + version
	if flagValue(args, "branch") != expectedBranch {
		return nil, fmt.Errorf("manifest PUT branch = %q, want %q", flagValue(args, "branch"), expectedBranch)
	}
	if containsFlagName(args, "sha") {
		return nil, errors.New("manifest PUT unexpectedly includes sha after a 404 response")
	}
	return decodedContent, nil
}

func validateWingetPullRequestArgs(args []string, version string) error {
	expectedHead := "hatayama:hatayama-uloop-" + version
	if flagValue(args, "head") != expectedHead {
		return fmt.Errorf("pull request head = %q, want %q", flagValue(args, "head"), expectedHead)
	}
	if flagValue(args, "base") != "master" {
		return fmt.Errorf("pull request base = %q, want master", flagValue(args, "base"))
	}
	body := flagValue(args, "body")
	expectedReleaseURL := "https://github.com/hatayama/unity-cli-loop/releases/tag/dispatcher-v" + version
	if body == "" || !strings.Contains(body, expectedReleaseURL) {
		return fmt.Errorf("pull request body = %q, want release URL %q", body, expectedReleaseURL)
	}
	return nil
}

func assertWingetPublishSetupNotCalled(t *testing.T, scenario *wingetTestScenario) {
	t.Helper()
	if scenario.mergeUpstreamCalls != 0 || scenario.upstreamRefCalls != 0 || scenario.branchCreationCalls != 0 {
		t.Fatalf(
			"merge-upstream calls = %d, upstream ref calls = %d, branch creation calls = %d",
			scenario.mergeUpstreamCalls,
			scenario.upstreamRefCalls,
			scenario.branchCreationCalls,
		)
	}
}

func flagValue(args []string, name string) string {
	prefix := name + "="
	for index := 0; index+1 < len(args); index++ {
		if args[index] == "-f" && strings.HasPrefix(args[index+1], prefix) {
			return strings.TrimPrefix(args[index+1], prefix)
		}
	}
	return ""
}
