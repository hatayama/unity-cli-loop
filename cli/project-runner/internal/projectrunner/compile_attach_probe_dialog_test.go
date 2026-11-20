package projectrunner

import (
	"context"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// countedPendingStatusQuery answers the pending request with answer(call) and counts those queries.
func countedPendingStatusQuery(
	requestID string,
	queries *int,
	answer func(call int) (compileStatusResponse, error),
) func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
	return pendingCompileStatusQuery(requestID, func(call int) (compileStatusResponse, error) {
		*queries = call
		return answer(call)
	})
}

// Verifies a compile whose attach probe Unity accepts but does not answer looks for a dialog at once
// and stops with BLOCKED_BY_DIALOG after one query and one look, keeping the pending record for the
// next uloop compile and sending no new compile.
func TestRunCompileAttachUnansweredProbeStopsAtOnceForADialog(t *testing.T) {
	test := newAttachProbeTest(t, "compile_attach_probe_dialog")
	queries := 0
	deps := compileWaitTestDeps(countedPendingStatusQuery(test.requestID, &queries, func(int) (compileStatusResponse, error) {
		return compileStatusResponse{}, unansweredStatusQueryError()
	}))
	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())
	deps.dialogWatch = look.deps()
	params := map[string]any{compileWaitTimeoutParam: attachProbeTestWaitTimeoutSeconds}

	code, _, stderr := test.runCompile(context.Background(), params, deps)

	if code != 1 || queries != 1 || look.calls != 1 {
		t.Fatalf("code = %d, queries = %d, looks = %d, want exit 1 after one query and one look", code, queries, look.calls)
	}
	if !strings.Contains(stderr, "BLOCKED_BY_DIALOG") || !strings.Contains(stderr, compileDialogClosedNextAction) {
		t.Fatalf("expected BLOCKED_BY_DIALOG pointing at the next uloop compile:\n%s", stderr)
	}
	test.assertRecordUnchanged(t)
	test.assertVibeLog(t, nil, []string{vibeLogAttachStart, vibeLogNewCompileRequest})
	test.assertServerHealthy(t)
}

// Verifies an unanswered probe with no dialog keeps retrying, so an Editor that then stops accepting
// connections still gets a new compile, and the look runs once.
func TestRunCompileAttachUnansweredProbeWithoutADialogKeepsTheLastAnswerRule(t *testing.T) {
	test := newAttachProbeTest(t, "compile_attach_probe_no_dialog")
	deps := compileWaitTestDeps(pendingCompileStatusQuery(test.requestID, func(call int) (compileStatusResponse, error) {
		if call == 1 {
			return compileStatusResponse{}, unansweredStatusQueryError()
		}
		return compileStatusResponse{}, unreachableStatusQueryError(test.connection.ProjectRoot)
	}))
	deps.attachProbeTimeout = attachProbeTestLongProbeTimeout
	look := newFakeDialogLook()
	deps.dialogWatch = look.deps()
	params := map[string]any{compileWaitTimeoutParam: attachProbeTestWaitTimeoutSeconds}

	code, stdout, stderr := test.runCompile(context.Background(), params, deps)

	if code != 1 || look.calls != 1 {
		t.Fatalf("code = %d, looks = %d, want the new compile's result after one look: %s", code, look.calls, stderr)
	}
	if got := compileResultErrorCount(t, stdout); got != attachProbeTestNewCompileErrorCount {
		t.Fatalf("expected the new compile's result, got ErrorCount %d", got)
	}
	test.assertRecordUnchanged(t)
	test.assertServerHealthy(t)
}

// Verifies a probe that only fails to reach Unity, as during a domain reload, never looks for a dialog.
func TestProbePendingCompileStatusDoesNotLookWhenUnityIsUnreachable(t *testing.T) {
	connection := compileWaitTestConnection(t)
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{}, unreachableStatusQueryError(connection.ProjectRoot)
	})
	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())
	deps.dialogWatch = look.deps()

	_, err := probePendingCompileStatus(context.Background(), connection, "compile_unreachable", deps)

	if err == nil || look.calls != 0 {
		t.Fatalf("err = %v, looks = %d, want the unreachable error without a look", err, look.calls)
	}
}

// Verifies unanswered probes within one interval look for a dialog only once and keep retrying until
// Unity answers.
func TestProbePendingCompileStatusLooksOncePerIntervalForUnansweredQueries(t *testing.T) {
	connection := compileWaitTestConnection(t)
	queries := 0
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		queries++
		if queries <= 2 {
			return compileStatusResponse{}, unansweredStatusQueryError()
		}
		return compileStatusResponse{IsCompiling: true}, nil
	})
	deps.attachProbeTimeout = attachProbeTestLongProbeTimeout
	look := newFakeDialogLook()
	deps.dialogWatch = look.deps()

	_, err := probePendingCompileStatus(context.Background(), connection, "compile_twice", deps)

	if err != nil || queries != 3 || look.calls != 1 {
		t.Fatalf("err = %v, queries = %d, looks = %d, want an answer on the third query after one look", err, queries, look.calls)
	}
}
