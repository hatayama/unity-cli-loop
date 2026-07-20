package automation

import (
	"bytes"
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os/exec"
	"strconv"
	"strings"
)

// cancelSupersededWaitingRunsConfig holds the target workflow run selection for cancellation.
type cancelSupersededWaitingRunsConfig struct {
	repository   string
	workflow     string
	branch       string
	currentRunID int64
}

type cancelSupersededWaitingRunsDeps struct {
	runOutput func(context.Context, string, ...string) (string, error)
}

type waitingWorkflowRun struct {
	DatabaseID int64  `json:"databaseId"`
	CreatedAt  string `json:"createdAt"`
}

// RunCancelSupersededWaitingRuns cancels approval-waiting publish runs for the same workflow and
// branch that are older than the currently running one, so approval-waiting runs never pile up
// between a release-please release PR merge and the eventual manual publish approval.
func RunCancelSupersededWaitingRuns(ctx context.Context, stdout io.Writer, stderr io.Writer, args []string) int {
	config, err := parseCancelSupersededWaitingRunsFlags(args)
	if err != nil {
		writeCancelSupersededWaitingRunsLine(stderr, "cancel-superseded-waiting-runs:", err)
		return 1
	}
	return runCancelSupersededWaitingRunsWithDeps(ctx, stdout, stderr, config, defaultCancelSupersededWaitingRunsDeps())
}

func defaultCancelSupersededWaitingRunsDeps() cancelSupersededWaitingRunsDeps {
	return cancelSupersededWaitingRunsDeps{
		runOutput: runCancelSupersededWaitingRunsCommandOutput,
	}
}

func parseCancelSupersededWaitingRunsFlags(args []string) (cancelSupersededWaitingRunsConfig, error) {
	flagSet := flag.NewFlagSet("cancel-superseded-waiting-runs", flag.ContinueOnError)
	repository := flagSet.String("repo", "", "GitHub repository in owner/name form")
	workflow := flagSet.String("workflow", "", "workflow file name to filter runs by")
	branch := flagSet.String("branch", "", "branch name to filter runs by")
	currentRunID := flagSet.Int64("current-run-id", 0, "database id of the run invoking this command")
	err := flagSet.Parse(args)
	if err != nil {
		return cancelSupersededWaitingRunsConfig{}, err
	}
	if *repository == "" {
		return cancelSupersededWaitingRunsConfig{}, fmt.Errorf("--repo is required")
	}
	if *workflow == "" {
		return cancelSupersededWaitingRunsConfig{}, fmt.Errorf("--workflow is required")
	}
	if *branch == "" {
		return cancelSupersededWaitingRunsConfig{}, fmt.Errorf("--branch is required")
	}
	if *currentRunID == 0 {
		return cancelSupersededWaitingRunsConfig{}, fmt.Errorf("--current-run-id is required")
	}
	return cancelSupersededWaitingRunsConfig{
		repository:   *repository,
		workflow:     *workflow,
		branch:       *branch,
		currentRunID: *currentRunID,
	}, nil
}

func runCancelSupersededWaitingRunsWithDeps(ctx context.Context, stdout io.Writer, stderr io.Writer, config cancelSupersededWaitingRunsConfig, deps cancelSupersededWaitingRunsDeps) int {
	runs, err := listCancelSupersededWaitingRuns(ctx, config, deps)
	if err != nil {
		writeCancelSupersededWaitingRunsLine(stderr, "cancel-superseded-waiting-runs:", err)
		return 1
	}

	for _, run := range runs {
		if run.DatabaseID >= config.currentRunID {
			continue
		}
		err = cancelSupersededWaitingRun(ctx, config, run.DatabaseID, deps)
		if err != nil {
			writeCancelSupersededWaitingRunsLine(stderr, fmt.Sprintf("cancel-superseded-waiting-runs: warning: failed to cancel waiting run %d: %v", run.DatabaseID, err))
			continue
		}
		writeCancelSupersededWaitingRunsLine(stdout, fmt.Sprintf("Cancelled superseded waiting run %d.", run.DatabaseID))
	}

	return 0
}

func writeCancelSupersededWaitingRunsLine(writer io.Writer, values ...any) {
	// CI status output failures cannot be recovered after the command outcome is known.
	_, _ = fmt.Fprintln(writer, values...)
}

func listCancelSupersededWaitingRuns(ctx context.Context, config cancelSupersededWaitingRunsConfig, deps cancelSupersededWaitingRunsDeps) ([]waitingWorkflowRun, error) {
	output, err := deps.runOutput(
		ctx,
		"gh",
		"run",
		"list",
		"--repo",
		config.repository,
		"--workflow",
		config.workflow,
		"--branch",
		config.branch,
		"--status",
		"waiting",
		"--json",
		"databaseId,createdAt",
		"--limit",
		"50",
	)
	if err != nil {
		return nil, err
	}

	runs := []waitingWorkflowRun{}
	err = json.Unmarshal([]byte(output), &runs)
	if err != nil {
		return nil, fmt.Errorf("failed to parse waiting workflow runs: %w", err)
	}
	return runs, nil
}

func cancelSupersededWaitingRun(ctx context.Context, config cancelSupersededWaitingRunsConfig, runID int64, deps cancelSupersededWaitingRunsDeps) error {
	_, err := deps.runOutput(ctx, "gh", "run", "cancel", strconv.FormatInt(runID, 10), "--repo", config.repository)
	return err
}

func runCancelSupersededWaitingRunsCommandOutput(ctx context.Context, name string, args ...string) (string, error) {
	command := exec.CommandContext(ctx, name, args...)
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	command.Stdout = &stdout
	command.Stderr = &stderr
	err := command.Run()
	if err != nil {
		return "", fmt.Errorf("%s %s failed: %w\n%s%s", name, strings.Join(args, " "), err, stderr.String(), stdout.String())
	}
	return stdout.String(), nil
}
