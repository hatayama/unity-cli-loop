package automation

import (
	"bytes"
	"context"
	"encoding/base64"
	"errors"
	"fmt"
	"strings"
	"testing"
)

const releasePleaseGateManifest = `{"Packages/src":"3.21.3","cli/dispatcher":"3.4.0","cli/project-runner":"3.12.3"}`

// releasePleaseGateStub answers the manifest read and one release read per tag;
// a tag with no entry answers as published.
type releasePleaseGateStub struct {
	manifestOutput string
	manifestErr    error
	releaseJSON    map[string]string
	releaseErr     map[string]error
	commandLog     []string
}

func (stub *releasePleaseGateStub) runOutput(ctx context.Context, name string, args ...string) (string, error) {
	commandLine := strings.Join(append([]string{name}, args...), " ")
	stub.commandLog = append(stub.commandLog, commandLine)
	if strings.HasPrefix(commandLine, "gh api repos/owner/repository/contents/"+releasePleaseManifestRelativePath+"?ref=main ") {
		return stub.manifestOutput, stub.manifestErr
	}
	if strings.HasPrefix(commandLine, "gh release view ") {
		tag := args[2]
		if err, found := stub.releaseErr[tag]; found {
			return "", err
		}
		if output, found := stub.releaseJSON[tag]; found {
			return output, nil
		}
		return `{"isDraft":false}`, nil
	}
	return "", fmt.Errorf("unexpected command: %s", commandLine)
}

func newReleasePleaseGateStub(manifest string) *releasePleaseGateStub {
	return &releasePleaseGateStub{
		manifestOutput: base64.StdEncoding.EncodeToString([]byte(manifest)) + "\n",
		releaseJSON:    map[string]string{},
		releaseErr:     map[string]error{},
	}
}

func runReleasePleaseGateCase(t *testing.T, stub *releasePleaseGateStub, args []string) (int, string, string) {
	t.Helper()
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := RunReleasePleaseGateCheckWithDeps(context.Background(), &stdout, &stderr, args, stub.runOutput)
	return exitCode, stdout.String(), stderr.String()
}

func releasePleaseGateArgs() []string {
	return []string{"--repo", "owner/repository", "--ref", "main"}
}

func releasePleaseGateMissingErr(tag string) error {
	return errors.New("gh release view " + tag + " failed: exit status 1\nrelease not found")
}

// Verifies release-please may run once every release the manifest names is published, and that all three tags were read.
func TestReleasePleaseGateRunsWhenEveryManifestReleaseIsPublished(t *testing.T) {
	stub := newReleasePleaseGateStub(releasePleaseGateManifest)

	exitCode, stdout, stderr := runReleasePleaseGateCase(t, stub, releasePleaseGateArgs())

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	if stdout != "skip=false\n" {
		t.Fatalf("expected skip=false, got %q", stdout)
	}
	commandLogText := strings.Join(stub.commandLog, "\n")
	for _, tag := range []string{"v3.21.3", "dispatcher-v3.4.0", "uloop-project-runner-v3.12.3"} {
		assertReleasePRCheckLogContains(t, commandLogText, "gh release view "+tag+" ")
	}
}

// Verifies release-please is skipped while a release the manifest names does not exist yet, naming that tag.
func TestReleasePleaseGateSkipsWhileAManifestReleaseIsMissing(t *testing.T) {
	stub := newReleasePleaseGateStub(releasePleaseGateManifest)
	stub.releaseErr["uloop-project-runner-v3.12.3"] = releasePleaseGateMissingErr("uloop-project-runner-v3.12.3")

	exitCode, stdout, stderr := runReleasePleaseGateCase(t, stub, releasePleaseGateArgs())

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	if stdout != "skip=true\n" {
		t.Fatalf("expected skip=true, got %q", stdout)
	}
	assertReleasePRCheckLogContains(t, stderr, "release-please is skipped: uloop-project-runner-v3.12.3 not published yet")
}

// Verifies release-please is skipped while a release the manifest names is still a draft.
func TestReleasePleaseGateSkipsWhileAManifestReleaseIsDraft(t *testing.T) {
	stub := newReleasePleaseGateStub(releasePleaseGateManifest)
	stub.releaseJSON["v3.21.3"] = `{"isDraft":true}`

	exitCode, stdout, stderr := runReleasePleaseGateCase(t, stub, releasePleaseGateArgs())

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	if stdout != "skip=true\n" {
		t.Fatalf("expected skip=true, got %q", stdout)
	}
	assertReleasePRCheckLogContains(t, stderr, "release-please is skipped: v3.21.3 not published yet")
}

// Verifies the skip report names every unpublished release, not only the first one found.
func TestReleasePleaseGateNamesEveryUnpublishedRelease(t *testing.T) {
	stub := newReleasePleaseGateStub(releasePleaseGateManifest)
	stub.releaseJSON["v3.21.3"] = `{"isDraft":true}`
	stub.releaseErr["dispatcher-v3.4.0"] = releasePleaseGateMissingErr("dispatcher-v3.4.0")

	exitCode, stdout, stderr := runReleasePleaseGateCase(t, stub, releasePleaseGateArgs())

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	if stdout != "skip=true\n" {
		t.Fatalf("expected skip=true, got %q", stdout)
	}
	assertReleasePRCheckLogContains(t, stderr, "release-please is skipped: v3.21.3, dispatcher-v3.4.0 not published yet")
}

// Verifies a release read that fails for any reason other than a missing release fails the run and decides nothing on stdout.
func TestReleasePleaseGateFailsWhenAReleaseCannotBeRead(t *testing.T) {
	stub := newReleasePleaseGateStub(releasePleaseGateManifest)
	stub.releaseErr["dispatcher-v3.4.0"] = errors.New("gh release view dispatcher-v3.4.0 failed: exit status 1\nHTTP 502: Bad Gateway")

	exitCode, stdout, stderr := runReleasePleaseGateCase(t, stub, releasePleaseGateArgs())

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	if stdout != "" {
		t.Fatalf("expected no stdout, got %q", stdout)
	}
	assertReleasePRCheckLogContains(t, stderr, "HTTP 502: Bad Gateway")
}

// Verifies a manifest entry the gate cannot map to a release tag fails the run instead of being skipped.
func TestReleasePleaseGateRejectsUnknownManifestPath(t *testing.T) {
	stub := newReleasePleaseGateStub(`{"Packages/src":"3.21.3","cli/other":"1.0.0"}`)

	exitCode, stdout, stderr := runReleasePleaseGateCase(t, stub, releasePleaseGateArgs())

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	if stdout != "" {
		t.Fatalf("expected no stdout, got %q", stdout)
	}
	assertReleasePRCheckLogContains(t, stderr, `unknown release manifest path "cli/other"`)
}

// Verifies an unreadable, unparsable, or padded manifest fails the run with its own error and decides nothing on stdout.
func TestReleasePleaseGateFailsOnUnusableManifest(t *testing.T) {
	cases := []struct {
		name    string
		output  string
		err     error
		wantErr string
	}{
		{"manifest read", "", errors.New("manifest read failed"), "manifest read failed"},
		{"manifest JSON", base64Of("{"), nil, "failed to parse " + releasePleaseManifestRelativePath + " at main"},
		{"padded version", base64Of(`{"cli/dispatcher":" 3.4.0"}`), nil, `releases "cli/dispatcher" as " 3.4.0", which is not a bare version`},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			stub := newReleasePleaseGateStub(releasePleaseGateManifest)
			stub.manifestOutput = testCase.output
			stub.manifestErr = testCase.err

			exitCode, stdout, stderr := runReleasePleaseGateCase(t, stub, releasePleaseGateArgs())

			if exitCode != 1 {
				t.Fatalf("expected exit code 1, got %d", exitCode)
			}
			if stdout != "" {
				t.Fatalf("expected no stdout, got %q", stdout)
			}
			assertReleasePRCheckLogContains(t, stderr, testCase.wantErr)
		})
	}
}

// Verifies the gate refuses to run without the repository or the ref, and on an unknown flag.
func TestParseReleasePleaseGateFlagsRejectsInvalidInput(t *testing.T) {
	cases := []struct {
		name    string
		args    []string
		wantErr string
	}{
		{"missing repo", []string{"--ref", "main"}, "--repo is required"},
		{"missing ref", []string{"--repo", "owner/repository"}, "--ref is required"},
		{"unknown flag", []string{"--repo", "owner/repository", "--ref", "main", "--other"}, "flag provided but not defined"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			stub := newReleasePleaseGateStub(releasePleaseGateManifest)

			exitCode, _, stderr := runReleasePleaseGateCase(t, stub, testCase.args)

			if exitCode != 1 {
				t.Fatalf("expected exit code 1, got %d", exitCode)
			}
			assertReleasePRCheckLogContains(t, stderr, testCase.wantErr)
			if len(stub.commandLog) != 0 {
				t.Fatalf("expected no gh call, got %v", stub.commandLog)
			}
		})
	}
}
