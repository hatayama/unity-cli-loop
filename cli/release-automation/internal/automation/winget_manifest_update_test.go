package automation

import (
	"bytes"
	"context"
	"encoding/base64"
	"errors"
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

type wingetTestScenario struct {
	versionExists          bool
	packageExists          bool
	branchExists           bool
	pullRequestOpen        bool
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
	t.Setenv(wingetPkgsTokenEnvName, "winget-token")
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	code := runUpdateWingetManifestWithDeps(
		context.Background(),
		&stdout,
		&stderr,
		testWingetConfig("dispatcher-v3.1.0"),
		wingetManifestUpdateDeps{runOutput: scenario.runOutput},
	)
	if code != 0 && stderr.Len() > 0 {
		t.Logf("stderr: %s", stderr.String())
	}
	return stdout.String(), code
}

func (s *wingetTestScenario) runOutput(_ context.Context, extraEnv []string, name string, args ...string) (string, error) {
	joined := strings.Join(args, " ")
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
		return testWingetSHA256 + "  " + wingetWindowsAmd64AssetName + "\n", true, nil
	}
	if strings.Contains(joined, "release view") {
		return "2026-08-31T12:34:56Z\n", true, nil
	}
	return "", false, nil
}

func (s *wingetTestScenario) upstreamOutput(_ []string, joined string) (string, bool, error) {
	if strings.Contains(joined, "repos/microsoft/winget-pkgs/contents/manifests/h/hatayama/uloop/3.1.0?ref=master") {
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
		return `{}`, true, nil
	}
	if strings.Contains(joined, "repos/microsoft/winget-pkgs/git/ref/heads/master") {
		return "upstream-master-sha\n", true, nil
	}
	return "", false, nil
}

func (s *wingetTestScenario) branchOutput(_ []string, joined string) (string, bool, error) {
	if strings.Contains(joined, "repos/hatayama/winget-pkgs/git/refs") {
		if s.branchExists {
			return "", true, errors.New("gh api failed: HTTP 422 Reference already exists")
		}
		return `{}`, true, nil
	}
	return "", false, nil
}

func (s *wingetTestScenario) manifestFileOutput(args []string, joined string) (string, bool, error) {
	if strings.Contains(joined, "repos/hatayama/winget-pkgs/contents/manifests/h/hatayama/uloop/3.1.0/") {
		if strings.Contains(joined, "-X PUT") {
			s.putCalls++
			encodedContent := flagValue(args, "content")
			decodedContent, err := base64.StdEncoding.DecodeString(encodedContent)
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
	if strings.Contains(joined, "repos/microsoft/winget-pkgs/pulls?head=hatayama:hatayama-uloop-3.1.0&state=open") {
		if s.pullRequestOpen {
			return `[{"html_url":"https://example.invalid/existing"}]`, true, nil
		}
		return `[]`, true, nil
	}
	if strings.Contains(joined, "repos/microsoft/winget-pkgs/pulls") && strings.Contains(joined, "-X POST") {
		s.pullRequestCreateCalls++
		s.pullRequestTitle = flagValue(args, "title")
		return `{"html_url":"https://example.invalid/new"}`, true, nil
	}
	return "", false, nil
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
