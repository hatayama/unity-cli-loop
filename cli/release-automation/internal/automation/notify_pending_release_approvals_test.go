package automation

import (
	"bytes"
	"context"
	"fmt"
	"strings"
	"testing"
)

// Verifies that when no existing issue is open, the body-generation logic
// composes one entry per waiting run with its workflow, branch, environment
// and run URL, plus the fixed explanation trailer.
func TestBuildPendingApprovalIssueBodyListsEachRunAndExplanation(t *testing.T) {
	runs := []pendingApprovalRun{
		{WorkflowName: "native-cli-publish", HeadBranch: "v3-beta", Environment: "cli-release", URL: "https://github.com/hatayama/unity-cli-loop/actions/runs/1"},
		{WorkflowName: "dispatcher-publish", HeadBranch: "v3-beta", Environment: "cli-release", URL: "https://github.com/hatayama/unity-cli-loop/actions/runs/2"},
	}

	body := buildPendingApprovalIssueBody(runs)

	assertNotifyPendingReleaseApprovalsContains(t, body, "native-cli-publish")
	assertNotifyPendingReleaseApprovalsContains(t, body, "dispatcher-publish")
	assertNotifyPendingReleaseApprovalsContains(t, body, "v3-beta")
	assertNotifyPendingReleaseApprovalsContains(t, body, "cli-release")
	assertNotifyPendingReleaseApprovalsContains(t, body, "https://github.com/hatayama/unity-cli-loop/actions/runs/1")
	assertNotifyPendingReleaseApprovalsContains(t, body, "https://github.com/hatayama/unity-cli-loop/actions/runs/2")
	assertNotifyPendingReleaseApprovalsContains(t, body, "Approve the pending deployment(s)")
}

// Verifies that two calls to buildPendingApprovalIssueBody with the same
// input runs produce byte-identical output, which is the idempotency
// guarantee the reconcile loop relies on to avoid pointless issue updates.
func TestBuildPendingApprovalIssueBodyIsDeterministic(t *testing.T) {
	runs := []pendingApprovalRun{
		{WorkflowName: "native-cli-publish", HeadBranch: "v3-beta", Environment: "cli-release", URL: "https://github.com/hatayama/unity-cli-loop/actions/runs/1"},
	}

	first := buildPendingApprovalIssueBody(runs)
	second := buildPendingApprovalIssueBody(runs)

	if first != second {
		t.Fatalf("expected deterministic body, got:\n%s\n---\n%s", first, second)
	}
}

// Verifies that with no waiting runs and no existing issue, the plan is a
// no-op: nothing to create and nothing to close.
func TestPlanNotifyPendingReleaseApprovalsNoRunsNoIssueIsNoop(t *testing.T) {
	plan := planNotifyPendingReleaseApprovals(nil, nil)

	if plan.Kind != notifyPendingReleaseApprovalsPlanNone {
		t.Fatalf("expected plan kind None, got %v", plan.Kind)
	}
}

// Verifies that when waiting runs exist and no issue is currently open, the
// plan is to create a new issue with the generated body.
func TestPlanNotifyPendingReleaseApprovalsRunsWithoutIssueCreates(t *testing.T) {
	runs := []pendingApprovalRun{
		{WorkflowName: "native-cli-publish", HeadBranch: "v3-beta", Environment: "cli-release", URL: "https://github.com/hatayama/unity-cli-loop/actions/runs/1"},
	}

	plan := planNotifyPendingReleaseApprovals(runs, nil)

	if plan.Kind != notifyPendingReleaseApprovalsPlanCreate {
		t.Fatalf("expected plan kind Create, got %v", plan.Kind)
	}
	assertNotifyPendingReleaseApprovalsContains(t, plan.Body, "native-cli-publish")
}

// Verifies that when the waiting runs are unchanged from the last reconcile
// (the existing issue body already matches the freshly generated body), the
// plan is a no-op so the issue is not rewritten on every 15-minute tick.
func TestPlanNotifyPendingReleaseApprovalsUnchangedRunsIsNoop(t *testing.T) {
	runs := []pendingApprovalRun{
		{WorkflowName: "native-cli-publish", HeadBranch: "v3-beta", Environment: "cli-release", URL: "https://github.com/hatayama/unity-cli-loop/actions/runs/1"},
	}
	existing := &existingPendingApprovalIssue{Number: 42, Body: buildPendingApprovalIssueBody(runs)}

	plan := planNotifyPendingReleaseApprovals(runs, existing)

	if plan.Kind != notifyPendingReleaseApprovalsPlanNone {
		t.Fatalf("expected plan kind None for unchanged runs, got %v", plan.Kind)
	}
}

// Verifies that when the waiting run set changed since the existing issue
// was last written (e.g. a new run started waiting), the plan is to update
// the existing issue in place rather than creating a duplicate.
func TestPlanNotifyPendingReleaseApprovalsChangedRunsUpdates(t *testing.T) {
	oldRuns := []pendingApprovalRun{
		{WorkflowName: "native-cli-publish", HeadBranch: "v3-beta", Environment: "cli-release", URL: "https://github.com/hatayama/unity-cli-loop/actions/runs/1"},
	}
	newRuns := []pendingApprovalRun{
		{WorkflowName: "native-cli-publish", HeadBranch: "v3-beta", Environment: "cli-release", URL: "https://github.com/hatayama/unity-cli-loop/actions/runs/1"},
		{WorkflowName: "dispatcher-publish", HeadBranch: "v3-beta", Environment: "cli-release", URL: "https://github.com/hatayama/unity-cli-loop/actions/runs/2"},
	}
	existing := &existingPendingApprovalIssue{Number: 42, Body: buildPendingApprovalIssueBody(oldRuns)}

	plan := planNotifyPendingReleaseApprovals(newRuns, existing)

	if plan.Kind != notifyPendingReleaseApprovalsPlanUpdate {
		t.Fatalf("expected plan kind Update, got %v", plan.Kind)
	}
	if plan.IssueNumber != 42 {
		t.Fatalf("expected issue number 42, got %d", plan.IssueNumber)
	}
	assertNotifyPendingReleaseApprovalsContains(t, plan.Body, "dispatcher-publish")
}

// Verifies that when no waiting runs remain but an issue is still open, the
// plan is to close that issue, so approvals resolving outside the 15-minute
// schedule (via the workflow_run trigger) still clear the issue promptly.
func TestPlanNotifyPendingReleaseApprovalsNoRunsWithIssueCloses(t *testing.T) {
	existing := &existingPendingApprovalIssue{Number: 42, Body: "stale body"}

	plan := planNotifyPendingReleaseApprovals(nil, existing)

	if plan.Kind != notifyPendingReleaseApprovalsPlanClose {
		t.Fatalf("expected plan kind Close, got %v", plan.Kind)
	}
	if plan.IssueNumber != 42 {
		t.Fatalf("expected issue number 42, got %d", plan.IssueNumber)
	}
}

// Verifies the end-to-end orchestration: waiting runs are listed repo-wide,
// each run's pending environment is looked up, the label is ensured, and
// (since no matching issue exists yet) a new issue is created with a body
// covering both waiting runs.
func TestRunNotifyPendingReleaseApprovalsCreatesIssueForWaitingRuns(t *testing.T) {
	commandLog := []string{}
	deps := notifyPendingReleaseApprovalsDeps{
		runOutput: func(ctx context.Context, name string, args ...string) (string, error) {
			commandLine := strings.Join(append([]string{name}, args...), " ")
			commandLog = append(commandLog, commandLine)
			switch {
			case commandLine == "gh run list --repo owner/repository --status waiting --json databaseId,workflowName,headBranch,url --limit 100":
				return `[{"databaseId":1,"workflowName":"native-cli-publish","headBranch":"v3-beta","url":"https://github.com/owner/repository/actions/runs/1"}]`, nil
			case commandLine == "gh api repos/owner/repository/actions/runs/1/pending_deployments":
				return `[{"environment":{"name":"cli-release"}}]`, nil
			case strings.HasPrefix(commandLine, "gh label create release-approval-pending"):
				return "", nil
			case commandLine == "gh issue list --repo owner/repository --state open --label release-approval-pending --json number,title,body --limit 100":
				return `[]`, nil
			case strings.HasPrefix(commandLine, "gh issue create --repo owner/repository --title Release approval pending"):
				return "https://github.com/owner/repository/issues/99", nil
			}
			return "", fmt.Errorf("unexpected command: %s", commandLine)
		},
	}
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := runNotifyPendingReleaseApprovalsWithDeps(context.Background(), &stdout, &stderr, "owner/repository", deps)

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr.String())
	}
	commandLogText := strings.Join(commandLog, "\n")
	assertNotifyPendingReleaseApprovalsContains(t, commandLogText, "gh issue create --repo owner/repository --title Release approval pending")
}

// Verifies that when no waiting runs exist but a matching open issue is
// found, the command closes it with a resolution comment instead of leaving
// it open or creating a duplicate.
func TestRunNotifyPendingReleaseApprovalsClosesIssueWhenNoWaitingRunsRemain(t *testing.T) {
	commandLog := []string{}
	deps := notifyPendingReleaseApprovalsDeps{
		runOutput: func(ctx context.Context, name string, args ...string) (string, error) {
			commandLine := strings.Join(append([]string{name}, args...), " ")
			commandLog = append(commandLog, commandLine)
			switch {
			case commandLine == "gh run list --repo owner/repository --status waiting --json databaseId,workflowName,headBranch,url --limit 100":
				return `[]`, nil
			case strings.HasPrefix(commandLine, "gh label create release-approval-pending"):
				return "", nil
			case commandLine == "gh issue list --repo owner/repository --state open --label release-approval-pending --json number,title,body --limit 100":
				return `[{"number":42,"title":"Release approval pending","body":"stale body"}]`, nil
			case commandLine == "gh issue close 42 --repo owner/repository --comment All pending approvals resolved.":
				return "", nil
			}
			return "", fmt.Errorf("unexpected command: %s", commandLine)
		},
	}
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := runNotifyPendingReleaseApprovalsWithDeps(context.Background(), &stdout, &stderr, "owner/repository", deps)

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr.String())
	}
	commandLogText := strings.Join(commandLog, "\n")
	assertNotifyPendingReleaseApprovalsContains(t, commandLogText, "gh issue close 42 --repo owner/repository --comment All pending approvals resolved.")
}

// Verifies that when the waiting run set is unchanged from what the existing
// issue already describes, no create/update/close command is issued at all,
// proving the reconcile loop is idempotent across repeated runs.
func TestRunNotifyPendingReleaseApprovalsNoopsWhenUnchanged(t *testing.T) {
	runs := []pendingApprovalRun{
		{WorkflowName: "native-cli-publish", HeadBranch: "v3-beta", Environment: "cli-release", URL: "https://github.com/owner/repository/actions/runs/1"},
	}
	existingBody := buildPendingApprovalIssueBody(runs)
	commandLog := []string{}
	deps := notifyPendingReleaseApprovalsDeps{
		runOutput: func(ctx context.Context, name string, args ...string) (string, error) {
			commandLine := strings.Join(append([]string{name}, args...), " ")
			commandLog = append(commandLog, commandLine)
			switch {
			case commandLine == "gh run list --repo owner/repository --status waiting --json databaseId,workflowName,headBranch,url --limit 100":
				return `[{"databaseId":1,"workflowName":"native-cli-publish","headBranch":"v3-beta","url":"https://github.com/owner/repository/actions/runs/1"}]`, nil
			case commandLine == "gh api repos/owner/repository/actions/runs/1/pending_deployments":
				return `[{"environment":{"name":"cli-release"}}]`, nil
			case strings.HasPrefix(commandLine, "gh label create release-approval-pending"):
				return "", nil
			case commandLine == "gh issue list --repo owner/repository --state open --label release-approval-pending --json number,title,body --limit 100":
				return fmt.Sprintf(`[{"number":42,"title":"Release approval pending","body":%q}]`, existingBody), nil
			}
			return "", fmt.Errorf("unexpected command: %s", commandLine)
		},
	}
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := runNotifyPendingReleaseApprovalsWithDeps(context.Background(), &stdout, &stderr, "owner/repository", deps)

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr.String())
	}
	commandLogText := strings.Join(commandLog, "\n")
	assertNotifyPendingReleaseApprovalsDoesNotContain(t, commandLogText, "gh issue create")
	assertNotifyPendingReleaseApprovalsDoesNotContain(t, commandLogText, "gh issue edit")
	assertNotifyPendingReleaseApprovalsDoesNotContain(t, commandLogText, "gh issue close")
}

// Verifies that a gh run list failure aborts with a non-zero exit code
// instead of silently treating the repository as having no waiting runs.
func TestRunNotifyPendingReleaseApprovalsFailsWhenRunListFails(t *testing.T) {
	deps := notifyPendingReleaseApprovalsDeps{
		runOutput: func(ctx context.Context, name string, args ...string) (string, error) {
			return "", fmt.Errorf("gh: authentication failed")
		},
	}
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := runNotifyPendingReleaseApprovalsWithDeps(context.Background(), &stdout, &stderr, "owner/repository", deps)

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	assertNotifyPendingReleaseApprovalsContains(t, stderr.String(), "authentication failed")
}

func assertNotifyPendingReleaseApprovalsContains(t *testing.T, actual string, expected string) {
	t.Helper()
	if !strings.Contains(actual, expected) {
		t.Fatalf("expected %q to contain %q", actual, expected)
	}
}

func assertNotifyPendingReleaseApprovalsDoesNotContain(t *testing.T, actual string, unexpected string) {
	t.Helper()
	if strings.Contains(actual, unexpected) {
		t.Fatalf("expected %q not to contain %q", actual, unexpected)
	}
}
