package automation

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os/exec"
	"strconv"
	"strings"
)

// pendingApprovalIssueTitle and pendingApprovalIssueLabel identify the single
// tracking issue this command reconciles: an exact title match (rather than a
// generated one) keeps repeated runs from creating duplicates.
const pendingApprovalIssueTitle = "Release approval pending"
const pendingApprovalIssueLabel = "release-approval-pending"
const pendingApprovalIssueLabelDescription = "Release environment approvals waiting on manual review"
const pendingApprovalIssueLabelColor = "5319E7"
const pendingApprovalResolvedComment = "All pending approvals resolved."

// pendingApprovalRun is one workflow run currently waiting on a GitHub
// environment manual approval.
type pendingApprovalRun struct {
	WorkflowName string
	HeadBranch   string
	Environment  string
	URL          string
}

// existingPendingApprovalIssue is the tracking issue's current state, read
// back from GitHub so the reconcile decision can compare against it.
type existingPendingApprovalIssue struct {
	Number int
	Body   string
}

type notifyPendingReleaseApprovalsPlanKind int

const (
	notifyPendingReleaseApprovalsPlanNone notifyPendingReleaseApprovalsPlanKind = iota
	notifyPendingReleaseApprovalsPlanCreate
	notifyPendingReleaseApprovalsPlanUpdate
	notifyPendingReleaseApprovalsPlanClose
)

// notifyPendingReleaseApprovalsPlan is the single action the reconcile loop
// takes this run, decided purely from waiting runs vs. existing issue state.
type notifyPendingReleaseApprovalsPlan struct {
	Kind        notifyPendingReleaseApprovalsPlanKind
	Body        string
	IssueNumber int
}

type notifyPendingReleaseApprovalsDeps struct {
	runOutput func(context.Context, string, ...string) (string, error)
}

type waitingRunListEntry struct {
	DatabaseID   int64  `json:"databaseId"`
	WorkflowName string `json:"workflowName"`
	HeadBranch   string `json:"headBranch"`
	URL          string `json:"url"`
}

type pendingDeploymentEntry struct {
	Environment struct {
		Name string `json:"name"`
	} `json:"environment"`
}

type openIssueListEntry struct {
	Number int    `json:"number"`
	Title  string `json:"title"`
	Body   string `json:"body"`
}

// RunNotifyPendingReleaseApprovals reconciles the repository-wide set of
// approval-waiting workflow runs against a single tracking issue: created or
// updated while runs are waiting, closed once none remain. It only relies on
// waiting-status runs having a pending environment deployment, so it needs no
// per-workflow filter — any future release gate is picked up automatically.
func RunNotifyPendingReleaseApprovals(ctx context.Context, stdout io.Writer, stderr io.Writer, repository string) int {
	if repository == "" {
		writeNotifyPendingReleaseApprovalsLine(stderr, "notify-pending-release-approvals: GITHUB_REPOSITORY is required")
		return 1
	}
	return runNotifyPendingReleaseApprovalsWithDeps(ctx, stdout, stderr, repository, defaultNotifyPendingReleaseApprovalsDeps())
}

func defaultNotifyPendingReleaseApprovalsDeps() notifyPendingReleaseApprovalsDeps {
	return notifyPendingReleaseApprovalsDeps{
		runOutput: runNotifyPendingReleaseApprovalsCommandOutput,
	}
}

func runNotifyPendingReleaseApprovalsWithDeps(ctx context.Context, stdout io.Writer, stderr io.Writer, repository string, deps notifyPendingReleaseApprovalsDeps) int {
	runs, err := listPendingApprovalRuns(ctx, repository, deps)
	if err != nil {
		writeNotifyPendingReleaseApprovalsLine(stderr, "notify-pending-release-approvals:", err)
		return 1
	}

	err = ensurePendingApprovalIssueLabel(ctx, repository, deps)
	if err != nil {
		writeNotifyPendingReleaseApprovalsLine(stderr, "notify-pending-release-approvals:", err)
		return 1
	}

	existing, err := findExistingPendingApprovalIssue(ctx, repository, deps)
	if err != nil {
		writeNotifyPendingReleaseApprovalsLine(stderr, "notify-pending-release-approvals:", err)
		return 1
	}

	plan := planNotifyPendingReleaseApprovals(runs, existing)

	switch plan.Kind {
	case notifyPendingReleaseApprovalsPlanNone:
		writeNotifyPendingReleaseApprovalsLine(stdout, "No changes needed.")
	case notifyPendingReleaseApprovalsPlanCreate:
		_, err = deps.runOutput(ctx, "gh", "issue", "create", "--repo", repository, "--title", pendingApprovalIssueTitle, "--body", plan.Body, "--label", pendingApprovalIssueLabel)
		if err != nil {
			writeNotifyPendingReleaseApprovalsLine(stderr, "notify-pending-release-approvals:", err)
			return 1
		}
		writeNotifyPendingReleaseApprovalsLine(stdout, "Created pending approval issue.")
	case notifyPendingReleaseApprovalsPlanUpdate:
		_, err = deps.runOutput(ctx, "gh", "issue", "edit", strconv.Itoa(plan.IssueNumber), "--repo", repository, "--body", plan.Body)
		if err != nil {
			writeNotifyPendingReleaseApprovalsLine(stderr, "notify-pending-release-approvals:", err)
			return 1
		}
		writeNotifyPendingReleaseApprovalsLine(stdout, fmt.Sprintf("Updated pending approval issue #%d.", plan.IssueNumber))
	case notifyPendingReleaseApprovalsPlanClose:
		_, err = deps.runOutput(ctx, "gh", "issue", "close", strconv.Itoa(plan.IssueNumber), "--repo", repository, "--comment", pendingApprovalResolvedComment)
		if err != nil {
			writeNotifyPendingReleaseApprovalsLine(stderr, "notify-pending-release-approvals:", err)
			return 1
		}
		writeNotifyPendingReleaseApprovalsLine(stdout, fmt.Sprintf("Closed pending approval issue #%d.", plan.IssueNumber))
	}

	return 0
}

// planNotifyPendingReleaseApprovals decides the single action to take, purely
// from the current waiting runs and the existing issue's last-written body.
// Comparing generated body text (rather than a separate hash/version field)
// keeps the reconcile loop idempotent without extra state to track.
func planNotifyPendingReleaseApprovals(runs []pendingApprovalRun, existing *existingPendingApprovalIssue) notifyPendingReleaseApprovalsPlan {
	if len(runs) == 0 {
		if existing == nil {
			return notifyPendingReleaseApprovalsPlan{Kind: notifyPendingReleaseApprovalsPlanNone}
		}
		return notifyPendingReleaseApprovalsPlan{Kind: notifyPendingReleaseApprovalsPlanClose, IssueNumber: existing.Number}
	}

	body := buildPendingApprovalIssueBody(runs)
	if existing == nil {
		return notifyPendingReleaseApprovalsPlan{Kind: notifyPendingReleaseApprovalsPlanCreate, Body: body}
	}
	if existing.Body == body {
		return notifyPendingReleaseApprovalsPlan{Kind: notifyPendingReleaseApprovalsPlanNone}
	}
	return notifyPendingReleaseApprovalsPlan{Kind: notifyPendingReleaseApprovalsPlanUpdate, Body: body, IssueNumber: existing.Number}
}

func buildPendingApprovalIssueBody(runs []pendingApprovalRun) string {
	builder := strings.Builder{}
	builder.WriteString("The following release approval(s) are waiting on a manual review:\n\n")
	for _, run := range runs {
		builder.WriteString(fmt.Sprintf("- Workflow: %s\n  Branch: %s\n  Environment: %s\n  Run: %s\n\n", run.WorkflowName, run.HeadBranch, run.Environment, run.URL))
	}
	builder.WriteString("Approve the pending deployment(s) at the run URL(s) above. After approval, the next release-please run publishes the blocked package releases automatically.")
	return builder.String()
}

func listPendingApprovalRuns(ctx context.Context, repository string, deps notifyPendingReleaseApprovalsDeps) ([]pendingApprovalRun, error) {
	output, err := deps.runOutput(ctx, "gh", "run", "list", "--repo", repository, "--status", "waiting", "--json", "databaseId,workflowName,headBranch,url", "--limit", "100")
	if err != nil {
		return nil, err
	}

	entries := []waitingRunListEntry{}
	err = json.Unmarshal([]byte(output), &entries)
	if err != nil {
		return nil, fmt.Errorf("failed to parse waiting workflow runs: %w", err)
	}

	runs := make([]pendingApprovalRun, 0, len(entries))
	for _, entry := range entries {
		environment, err := resolvePendingDeploymentEnvironment(ctx, repository, entry.DatabaseID, deps)
		if err != nil {
			return nil, fmt.Errorf("failed to resolve pending deployment environment for run %d: %w", entry.DatabaseID, err)
		}
		runs = append(runs, pendingApprovalRun{
			WorkflowName: entry.WorkflowName,
			HeadBranch:   entry.HeadBranch,
			Environment:  environment,
			URL:          entry.URL,
		})
	}
	return runs, nil
}

func resolvePendingDeploymentEnvironment(ctx context.Context, repository string, runID int64, deps notifyPendingReleaseApprovalsDeps) (string, error) {
	output, err := deps.runOutput(ctx, "gh", "api", fmt.Sprintf("repos/%s/actions/runs/%d/pending_deployments", repository, runID))
	if err != nil {
		return "", err
	}

	entries := []pendingDeploymentEntry{}
	err = json.Unmarshal([]byte(output), &entries)
	if err != nil {
		return "", fmt.Errorf("failed to parse pending deployments: %w", err)
	}

	environments := make([]string, 0, len(entries))
	for _, entry := range entries {
		environments = append(environments, entry.Environment.Name)
	}
	return strings.Join(environments, ", "), nil
}

func ensurePendingApprovalIssueLabel(ctx context.Context, repository string, deps notifyPendingReleaseApprovalsDeps) error {
	_, err := deps.runOutput(ctx, "gh", "label", "create", pendingApprovalIssueLabel, "--repo", repository, "--description", pendingApprovalIssueLabelDescription, "--color", pendingApprovalIssueLabelColor, "--force")
	return err
}

func findExistingPendingApprovalIssue(ctx context.Context, repository string, deps notifyPendingReleaseApprovalsDeps) (*existingPendingApprovalIssue, error) {
	output, err := deps.runOutput(ctx, "gh", "issue", "list", "--repo", repository, "--state", "open", "--label", pendingApprovalIssueLabel, "--json", "number,title,body", "--limit", "100")
	if err != nil {
		return nil, err
	}

	entries := []openIssueListEntry{}
	err = json.Unmarshal([]byte(output), &entries)
	if err != nil {
		return nil, fmt.Errorf("failed to parse open issue list: %w", err)
	}

	for _, entry := range entries {
		if entry.Title == pendingApprovalIssueTitle {
			return &existingPendingApprovalIssue{Number: entry.Number, Body: entry.Body}, nil
		}
	}
	return nil, nil
}

func writeNotifyPendingReleaseApprovalsLine(writer io.Writer, values ...any) {
	// CI status output failures cannot be recovered after the command outcome is known.
	_, _ = fmt.Fprintln(writer, values...)
}

func runNotifyPendingReleaseApprovalsCommandOutput(ctx context.Context, name string, args ...string) (string, error) {
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
