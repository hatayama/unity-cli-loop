package automation

import (
	"bytes"
	"context"
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

const coverageTrendTestRepository = "example-owner/example-repo"

// fakeCoverageTrendGh records every gh call and answers by the first two arguments, so a test can
// assert both the sequence of steps and the arguments each one received.
type fakeCoverageTrendGh struct {
	t         *testing.T
	responses map[string]string
	failures  map[string]error
	calls     [][]string
	// commentBody is the --body-file content as it was when gh issue comment ran.
	commentBody string
}

func newFakeCoverageTrendGh(t *testing.T, issueListOutput string) *fakeCoverageTrendGh {
	return &fakeCoverageTrendGh{
		t: t,
		responses: map[string]string{
			"issue list":   issueListOutput,
			"issue create": "https://github.com/" + coverageTrendTestRepository + "/issues/57\n",
		},
		failures: map[string]error{},
	}
}

func (fake *fakeCoverageTrendGh) runOutput(_ context.Context, name string, args ...string) (string, error) {
	fake.t.Helper()
	if name != "gh" || len(args) < 2 {
		fake.t.Fatalf("unexpected command %s %v", name, args)
	}
	fake.calls = append(fake.calls, args)
	step := args[0] + " " + args[1]
	if err := fake.failures[step]; err != nil {
		return "", err
	}
	if step == "issue comment" {
		fake.commentBody = fake.readBodyFile(args)
	}
	return fake.responses[step], nil
}

func (fake *fakeCoverageTrendGh) readBodyFile(args []string) string {
	fake.t.Helper()
	for index, arg := range args {
		if arg == "--body-file" && index+1 < len(args) {
			content, err := os.ReadFile(args[index+1])
			if err != nil {
				fake.t.Fatalf("read comment body: %v", err)
			}
			return string(content)
		}
	}
	fake.t.Fatalf("gh issue comment without --body-file: %v", args)
	return ""
}

func (fake *fakeCoverageTrendGh) joinedCalls() []string {
	joined := make([]string, 0, len(fake.calls))
	for _, call := range fake.calls {
		joined = append(joined, strings.Join(call, " "))
	}
	return joined
}

func writeCoverageTrendTestBody(t *testing.T, content string) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), "coverage.md")
	if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
		t.Fatalf("write body file: %v", err)
	}
	return path
}

func runPublishCoverageTrendForTest(t *testing.T, fake *fakeCoverageTrendGh, options publishCoverageTrendOptions) (int, string, string) {
	t.Helper()
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPublishCoverageTrendWithDeps(context.Background(), &stdout, &stderr, options, publishCoverageTrendDeps{
		runOutput: fake.runOutput,
		// 23:30 at UTC-5 is already the next day in UTC, so a local-date bug shows in the header.
		now: func() time.Time { return time.Date(2026, 10, 2, 23, 30, 0, 0, time.FixedZone("UTC-5", -5*3600)) },
	})
	return code, stdout.String(), stderr.String()
}

func coverageTrendTestOptions(t *testing.T) publishCoverageTrendOptions {
	return publishCoverageTrendOptions{
		Repository: coverageTrendTestRepository,
		BodyFile:   writeCoverageTrendTestBody(t, "## Go test coverage\n\n| table |\n"),
		RunURL:     "https://github.com/" + coverageTrendTestRepository + "/actions/runs/123",
	}
}

const coverageTrendTestListCall = "issue list --repo " + coverageTrendTestRepository + " --state open --label coverage-trend --json number --limit 100"

func TestPublishCoverageTrendCommentsOnTheSingleOpenIssue(t *testing.T) {
	// Verifies one open labelled issue receives the comment, headed by the UTC date and run URL,
	// without creating a label or an issue.
	fake := newFakeCoverageTrendGh(t, `[{"number":41}]`)

	code, stdout, stderr := runPublishCoverageTrendForTest(t, fake, coverageTrendTestOptions(t))

	if code != 0 || stdout != "Posted coverage to issue #41.\n" {
		t.Fatalf("expected success on #41, got %d: %s%s", code, stdout, stderr)
	}
	calls := fake.joinedCalls()
	if len(calls) != 2 || calls[0] != coverageTrendTestListCall ||
		!strings.HasPrefix(calls[1], "issue comment 41 --repo "+coverageTrendTestRepository+" --body-file ") {
		t.Fatalf("unexpected gh calls: %q", calls)
	}
	want := "Nightly coverage — 2026-10-03 — https://github.com/" + coverageTrendTestRepository + "/actions/runs/123\n\n## Go test coverage\n\n| table |\n"
	if fake.commentBody != want {
		t.Fatalf("comment body:\nwant %q\ngot  %q", want, fake.commentBody)
	}
}

func TestPublishCoverageTrendCreatesTheLabelAndIssueWhenNoneIsOpen(t *testing.T) {
	// Verifies that with no open issue the label is created with --force, the issue is created with
	// the label, and the comment goes to the number read from the created issue URL.
	fake := newFakeCoverageTrendGh(t, `[]`)

	code, _, stderr := runPublishCoverageTrendForTest(t, fake, coverageTrendTestOptions(t))

	if code != 0 {
		t.Fatalf("expected success, got %d: %s", code, stderr)
	}
	calls := fake.joinedCalls()
	wantPrefixes := []string{
		coverageTrendTestListCall,
		"label create coverage-trend --repo " + coverageTrendTestRepository + " --description Nightly test coverage trend --color 0E8A16 --force",
		"issue create --repo " + coverageTrendTestRepository + " --title Test coverage trend --label coverage-trend --body " + coverageTrendIssueBody,
		"issue comment 57 --repo " + coverageTrendTestRepository + " --body-file ",
	}
	if len(calls) != len(wantPrefixes) {
		t.Fatalf("expected %d gh calls, got %q", len(wantPrefixes), calls)
	}
	for index, prefix := range wantPrefixes {
		if !strings.HasPrefix(calls[index], prefix) {
			t.Fatalf("call %d:\nwant prefix %q\ngot         %q", index, prefix, calls[index])
		}
	}
}

func TestPublishCoverageTrendOmitsTheRunURLWhenNoneIsGiven(t *testing.T) {
	// Verifies the comment header is just the date when no run URL is given.
	fake := newFakeCoverageTrendGh(t, `[{"number":41}]`)
	options := coverageTrendTestOptions(t)
	options.RunURL = ""

	if code, _, stderr := runPublishCoverageTrendForTest(t, fake, options); code != 0 {
		t.Fatalf("expected success, got %d: %s", code, stderr)
	}

	if !strings.HasPrefix(fake.commentBody, "Nightly coverage — 2026-10-03\n\n## Go test coverage") {
		t.Fatalf("unexpected comment header: %q", fake.commentBody)
	}
}

func TestPublishCoverageTrendRefusesToChooseBetweenSeveralOpenIssues(t *testing.T) {
	// Verifies several open labelled issues fail with their numbers and nothing is posted.
	fake := newFakeCoverageTrendGh(t, `[{"number":41},{"number":44}]`)

	code, _, stderr := runPublishCoverageTrendForTest(t, fake, coverageTrendTestOptions(t))

	if code == 0 || !strings.Contains(stderr, "several open issues carry the coverage-trend label (#41, #44)") {
		t.Fatalf("expected a several-issues failure, got %d: %s", code, stderr)
	}
	if len(fake.calls) != 1 {
		t.Fatalf("expected only the list call, got %q", fake.joinedCalls())
	}
}

func TestPublishCoverageTrendReportsWhichGhStepFailed(t *testing.T) {
	// Verifies each failing gh step fails the run with a message naming that step.
	cases := map[string]struct {
		issueList string
		arrange   func(fake *fakeCoverageTrendGh)
		message   string
	}{
		"list fails": {
			issueList: `[]`,
			arrange:   func(fake *fakeCoverageTrendGh) { fake.failures["issue list"] = errors.New("boom") },
			message:   "list coverage trend issues: boom",
		},
		"list is not JSON": {
			issueList: `not json`,
			arrange:   func(*fakeCoverageTrendGh) {},
			message:   "parse coverage trend issue list",
		},
		"label create fails": {
			issueList: `[]`,
			arrange:   func(fake *fakeCoverageTrendGh) { fake.failures["label create"] = errors.New("boom") },
			message:   "create coverage-trend label: boom",
		},
		"issue create fails": {
			issueList: `[]`,
			arrange:   func(fake *fakeCoverageTrendGh) { fake.failures["issue create"] = errors.New("boom") },
			message:   "create coverage trend issue: boom",
		},
		"issue create prints no URL": {
			issueList: `[]`,
			arrange:   func(fake *fakeCoverageTrendGh) { fake.responses["issue create"] = "created\n" },
			message:   `read the issue number from the created issue URL "created"`,
		},
		"comment fails": {
			issueList: `[{"number":41}]`,
			arrange:   func(fake *fakeCoverageTrendGh) { fake.failures["issue comment"] = errors.New("boom") },
			message:   "comment on coverage trend issue #41: boom",
		},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			fake := newFakeCoverageTrendGh(t, testCase.issueList)
			testCase.arrange(fake)

			code, _, stderr := runPublishCoverageTrendForTest(t, fake, coverageTrendTestOptions(t))

			if code == 0 || !strings.Contains(stderr, testCase.message) {
				t.Fatalf("expected a failure with %q, got %d: %s", testCase.message, code, stderr)
			}
		})
	}
}

func TestPublishCoverageTrendRejectsAnUnreadableOrEmptyBodyBeforeCallingGh(t *testing.T) {
	// Verifies a missing or blank body file fails before any gh call, so no empty comment or
	// stray issue is created.
	cases := map[string]struct {
		bodyFile func(t *testing.T) string
		message  string
	}{
		"missing": {
			bodyFile: func(t *testing.T) string { return filepath.Join(t.TempDir(), "absent.md") },
			message:  "read body file",
		},
		"blank": {
			bodyFile: func(t *testing.T) string { return writeCoverageTrendTestBody(t, " \n") },
			message:  "body file is empty",
		},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			fake := newFakeCoverageTrendGh(t, `[{"number":41}]`)
			options := coverageTrendTestOptions(t)
			options.BodyFile = testCase.bodyFile(t)

			code, _, stderr := runPublishCoverageTrendForTest(t, fake, options)

			if code == 0 || !strings.Contains(stderr, testCase.message) || len(fake.calls) != 0 {
				t.Fatalf("expected %q with no gh calls, got %d (%q): %s", testCase.message, code, fake.joinedCalls(), stderr)
			}
		})
	}
}

func TestParsePublishCoverageTrendOptionsResolvesTheRepository(t *testing.T) {
	// Verifies --repo wins over GITHUB_REPOSITORY, the variable fills in when --repo is absent, and
	// a missing repository or body file is rejected with its own message.
	environment := func(value string) func(string) (string, bool) {
		return func(name string) (string, bool) {
			if name == "GITHUB_REPOSITORY" && value != "" {
				return value, true
			}
			return "", false
		}
	}

	options, err := parsePublishCoverageTrendOptions([]string{"--repo", "flag-owner/repo", "--body-file", "b.md", "--run-url", "u"}, environment("env-owner/repo"))
	if err != nil || options != (publishCoverageTrendOptions{Repository: "flag-owner/repo", BodyFile: "b.md", RunURL: "u"}) {
		t.Fatalf("expected the flag repository, got %+v, %v", options, err)
	}
	options, err = parsePublishCoverageTrendOptions([]string{"--body-file", "b.md"}, environment("env-owner/repo"))
	if err != nil || options.Repository != "env-owner/repo" {
		t.Fatalf("expected the environment repository, got %+v, %v", options, err)
	}
	if _, err := parsePublishCoverageTrendOptions([]string{"--body-file", "b.md"}, environment("")); err == nil || err.Error() != "--repo or GITHUB_REPOSITORY is required" {
		t.Fatalf("expected a missing repository error, got %v", err)
	}
	if _, err := parsePublishCoverageTrendOptions([]string{"--repo", "o/r"}, environment("")); err == nil || err.Error() != "--body-file is required" {
		t.Fatalf("expected a missing body file error, got %v", err)
	}
}

func TestRunPublishCoverageTrendRejectsAnUnknownFlag(t *testing.T) {
	// Verifies the exported entry point stops at flag parsing for an unknown flag, before any gh call.
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunPublishCoverageTrend(context.Background(), &stdout, &stderr, []string{"--bogus"})

	if code != 2 || !strings.Contains(stderr.String(), "flag provided but not defined: -bogus") {
		t.Fatalf("expected an unknown flag error, got %d: %s", code, stderr.String())
	}
}
