package automation

import (
	"context"
	"errors"
	"strings"
	"testing"
	"time"
)

const (
	unityPackageReleaseTestRepository  = "example-owner/example-repo"
	unityPackageReleaseTestCommit      = "0123456789abcdef0123456789abcdef01234567"
	unityPackageReleaseTestOtherCommit = "fedcba9876543210fedcba9876543210fedcba98"
)

// unityPackageReleaseTestNotFound is how gh reports a release or tag that does not exist.
var unityPackageReleaseTestNotFound = fakeUnityPackageReleaseResponse{err: errors.New("gh api failed: exit status 1\ngh: Not Found (HTTP 404)")}

// unityPackageReleaseWriteKinds are the calls that change the repository; every other call only
// reads.
var unityPackageReleaseWriteKinds = map[string]bool{"delete": true, "ref": true, "create": true}

// fakeUnityPackageReleaseResponse is one canned answer of a gh or git call.
type fakeUnityPackageReleaseResponse struct {
	output string
	err    error
}

// fakeUnityPackageReleaseCommands answers the gh and git calls of the Unity package release
// commands by the kind of call, in the order the responses are queued (the last one repeats), and
// records every call so a test can assert which writes happened. It also stands in for the
// readiness check script.
type fakeUnityPackageReleaseCommands struct {
	t           *testing.T
	tag         string
	responses   map[string][]fakeUnityPackageReleaseResponse
	calls       []string
	commands    [][]string
	sleeps      int
	checkOutput string
	checkErr    error
	checkCalls  int
}

func newFakeUnityPackageReleaseCommands(t *testing.T, responses map[string][]fakeUnityPackageReleaseResponse) *fakeUnityPackageReleaseCommands {
	return &fakeUnityPackageReleaseCommands{t: t, tag: "v3.14.0", responses: responses}
}

func (fake *fakeUnityPackageReleaseCommands) runOutput(_ context.Context, name string, args ...string) (string, error) {
	fake.t.Helper()
	kind := unityPackageReleaseTestCallKind(fake.tag, name, args)
	if kind == "" {
		fake.t.Fatalf("unexpected command %s %v", name, args)
	}
	fake.calls = append(fake.calls, kind)
	fake.commands = append(fake.commands, append([]string{name}, args...))
	queue := fake.responses[kind]
	if len(queue) == 0 {
		fake.t.Fatalf("no response queued for the %s call %s %v", kind, name, args)
	}
	if len(queue) > 1 {
		fake.responses[kind] = queue[1:]
	}
	return queue[0].output, queue[0].err
}

func (fake *fakeUnityPackageReleaseCommands) sleep(time.Duration) {
	fake.sleeps++
}

func (fake *fakeUnityPackageReleaseCommands) checkRelease(context.Context) (string, error) {
	fake.checkCalls++
	return fake.checkOutput, fake.checkErr
}

// writes returns the kinds of the calls that changed the repository, in call order.
func (fake *fakeUnityPackageReleaseCommands) writes() []string {
	var writes []string
	for _, kind := range fake.calls {
		if unityPackageReleaseWriteKinds[kind] {
			writes = append(writes, kind)
		}
	}
	return writes
}

// command returns the arguments of the first call of the kind, or nil when there was none.
func (fake *fakeUnityPackageReleaseCommands) command(kind string) []string {
	for index, recorded := range fake.calls {
		if recorded == kind {
			return fake.commands[index]
		}
	}
	return nil
}

func unityPackageReleaseTestCallKind(tag string, name string, args []string) string {
	command := name + " " + strings.Join(args, " ")
	repository := "repos/" + unityPackageReleaseTestRepository
	switch {
	case command == "gh api "+repository+"/releases/tags/"+tag:
		return "release"
	case command == "gh api "+repository+"/git/ref/tags/"+tag:
		return "tag"
	case command == "gh api --paginate "+repository+"/releases?per_page=100":
		return "drafts"
	case strings.HasPrefix(command, "git -C ") && strings.HasSuffix(command, " show "+unityPackageReleaseTestCommit+":release-please-config.json"):
		return "config"
	case strings.HasPrefix(command, "git -C ") && strings.Contains(command, " show "+unityPackageReleaseTestCommit+":"):
		return "changelog"
	case strings.HasPrefix(command, "gh api -X DELETE "+repository+"/releases/"):
		return "delete"
	case command == "gh api "+repository+"/git/refs -f ref=refs/tags/"+tag+" -f sha="+unityPackageReleaseTestCommit:
		return "ref"
	case strings.HasPrefix(command, "gh release create "+tag+" "):
		return "create"
	}
	return ""
}
