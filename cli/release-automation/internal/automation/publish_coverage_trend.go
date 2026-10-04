package automation

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

// coverageTrendIssueLabel identifies the single issue that collects the nightly coverage comments.
const (
	coverageTrendIssueLabel            = "coverage-trend"
	coverageTrendIssueLabelDescription = "Nightly test coverage trend"
	coverageTrendIssueLabelColor       = "0E8A16"
	coverageTrendIssueTitle            = "Test coverage trend"
	coverageTrendIssueBody             = "Each nightly Unity EditMode Tests run appends a comment with the Go and C# test coverage. Keep exactly one open issue with the coverage-trend label: the run fails when it finds several."
)

// publishCoverageTrendOptions is one publish run: the repository, the Markdown to post, and the
// optional workflow run URL shown above it.
type publishCoverageTrendOptions struct {
	Repository string
	BodyFile   string
	RunURL     string
}

type publishCoverageTrendDeps struct {
	runOutput func(context.Context, string, ...string) (string, error)
	now       func() time.Time
}

type coverageTrendIssueListEntry struct {
	Number int `json:"number"`
}

// RunPublishCoverageTrend appends the coverage Markdown as a comment on the open coverage-trend
// issue, creating the label and the issue when none is open.
func RunPublishCoverageTrend(ctx context.Context, stdout io.Writer, stderr io.Writer, args []string) int {
	options, err := parsePublishCoverageTrendOptions(args, os.LookupEnv)
	if err != nil {
		_, _ = fmt.Fprintln(stderr, "publish-coverage-trend:", err)
		return 2
	}
	return runPublishCoverageTrendWithDeps(ctx, stdout, stderr, options, publishCoverageTrendDeps{
		runOutput: runCommandOutput,
		now:       time.Now,
	})
}

func parsePublishCoverageTrendOptions(args []string, lookupEnv func(string) (string, bool)) (publishCoverageTrendOptions, error) {
	flags := flag.NewFlagSet("publish-coverage-trend", flag.ContinueOnError)
	flags.SetOutput(io.Discard)
	repository := flags.String("repo", "", "owner/name of the repository; defaults to GITHUB_REPOSITORY")
	bodyFile := flags.String("body-file", "", "Markdown file to post as the comment body")
	runURL := flags.String("run-url", "", "workflow run URL shown above the Markdown")
	if err := flags.Parse(args); err != nil {
		return publishCoverageTrendOptions{}, err
	}

	if *repository == "" {
		*repository, _ = lookupEnv("GITHUB_REPOSITORY")
	}
	if *repository == "" {
		return publishCoverageTrendOptions{}, errors.New("--repo or GITHUB_REPOSITORY is required")
	}
	if *bodyFile == "" {
		return publishCoverageTrendOptions{}, errors.New("--body-file is required")
	}
	return publishCoverageTrendOptions{Repository: *repository, BodyFile: *bodyFile, RunURL: *runURL}, nil
}

func runPublishCoverageTrendWithDeps(ctx context.Context, stdout io.Writer, stderr io.Writer, options publishCoverageTrendOptions, deps publishCoverageTrendDeps) int {
	issueNumber, err := publishCoverageTrend(ctx, options, deps)
	if err != nil {
		_, _ = fmt.Fprintln(stderr, "publish-coverage-trend:", err)
		return 1
	}
	_, _ = fmt.Fprintf(stdout, "Posted coverage to issue #%d.\n", issueNumber)
	return 0
}

func publishCoverageTrend(ctx context.Context, options publishCoverageTrendOptions, deps publishCoverageTrendDeps) (int, error) {
	markdown, err := os.ReadFile(options.BodyFile)
	if err != nil {
		return 0, fmt.Errorf("read body file: %w", err)
	}
	if strings.TrimSpace(string(markdown)) == "" {
		return 0, errors.New("body file is empty")
	}

	issueNumber, err := findOrCreateCoverageTrendIssue(ctx, options.Repository, deps)
	if err != nil {
		return 0, err
	}

	comment := formatCoverageTrendComment(deps.now(), options.RunURL, string(markdown))
	if err := commentOnCoverageTrendIssue(ctx, options.Repository, issueNumber, comment, deps); err != nil {
		return 0, err
	}
	return issueNumber, nil
}

// findOrCreateCoverageTrendIssue refuses to choose between several labelled issues, because
// posting to an arbitrary one would split the trend across issues without anyone noticing.
func findOrCreateCoverageTrendIssue(ctx context.Context, repository string, deps publishCoverageTrendDeps) (int, error) {
	output, err := deps.runOutput(ctx, "gh", "issue", "list", "--repo", repository, "--state", "open", "--label", coverageTrendIssueLabel, "--json", "number", "--limit", "100")
	if err != nil {
		return 0, fmt.Errorf("list coverage trend issues: %w", err)
	}

	entries := []coverageTrendIssueListEntry{}
	if err := json.Unmarshal([]byte(output), &entries); err != nil {
		return 0, fmt.Errorf("parse coverage trend issue list: %w", err)
	}
	switch len(entries) {
	case 0:
		return createCoverageTrendIssue(ctx, repository, deps)
	case 1:
		return entries[0].Number, nil
	}

	numbers := make([]string, 0, len(entries))
	for _, entry := range entries {
		numbers = append(numbers, "#"+strconv.Itoa(entry.Number))
	}
	return 0, fmt.Errorf("several open issues carry the %s label (%s); close all but one", coverageTrendIssueLabel, strings.Join(numbers, ", "))
}

func createCoverageTrendIssue(ctx context.Context, repository string, deps publishCoverageTrendDeps) (int, error) {
	// --force makes the call succeed when the label already exists, so no existence check is needed.
	if _, err := deps.runOutput(ctx, "gh", "label", "create", coverageTrendIssueLabel, "--repo", repository, "--description", coverageTrendIssueLabelDescription, "--color", coverageTrendIssueLabelColor, "--force"); err != nil {
		return 0, fmt.Errorf("create %s label: %w", coverageTrendIssueLabel, err)
	}

	output, err := deps.runOutput(ctx, "gh", "issue", "create", "--repo", repository, "--title", coverageTrendIssueTitle, "--label", coverageTrendIssueLabel, "--body", coverageTrendIssueBody)
	if err != nil {
		return 0, fmt.Errorf("create coverage trend issue: %w", err)
	}

	issueURL := strings.TrimSpace(output)
	number, err := strconv.Atoi(issueURL[strings.LastIndex(issueURL, "/")+1:])
	if err != nil || number <= 0 {
		return 0, fmt.Errorf("read the issue number from the created issue URL %q", issueURL)
	}
	return number, nil
}

func formatCoverageTrendComment(now time.Time, runURL string, markdown string) string {
	header := "Nightly coverage — " + now.UTC().Format("2006-01-02")
	if runURL != "" {
		header += " — " + runURL
	}
	return header + "\n\n" + markdown
}

// commentOnCoverageTrendIssue passes the comment through a file because the Markdown tables can
// exceed what is comfortable to pass as a single command-line argument.
func commentOnCoverageTrendIssue(ctx context.Context, repository string, issueNumber int, comment string, deps publishCoverageTrendDeps) error {
	dir, err := os.MkdirTemp("", "coverage-trend-")
	if err != nil {
		return fmt.Errorf("create comment body directory: %w", err)
	}
	defer func() { _ = os.RemoveAll(dir) }()

	bodyPath := filepath.Join(dir, "comment.md")
	if err := os.WriteFile(bodyPath, []byte(comment), 0o600); err != nil {
		return fmt.Errorf("write comment body: %w", err)
	}
	if _, err := deps.runOutput(ctx, "gh", "issue", "comment", strconv.Itoa(issueNumber), "--repo", repository, "--body-file", bodyPath); err != nil {
		return fmt.Errorf("comment on coverage trend issue #%d: %w", issueNumber, err)
	}
	return nil
}
