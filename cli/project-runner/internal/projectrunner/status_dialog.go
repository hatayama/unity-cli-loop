package projectrunner

import (
	"context"
	"errors"
	"fmt"
	"runtime"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityprocess/unitydialog"
)

// statusDialog is one modal dialog as uloop status and the dialog commands print it.
type statusDialog = unitydialog.Dialog

// defaultDialogInspector looks at this operating system's windows and finds the Editor through
// unityProcessLookup, read when it is called so that tests replacing the lookup reach it.
func defaultDialogInspector() unitydialog.Inspector {
	return unitydialog.NewInspector(runtime.GOOS, unityProcessLookup)
}

// dialogMacPermissions names the macOS permissions that both looking at and pressing in another
// process's windows need: Accessibility to read the windows, and Automation to ask System Events,
// which runs the script that reads them.
const dialogMacPermissions = "Accessibility and Automation (System Events) access for the app that runs uloop " +
	"(System Settings > Privacy & Security)"

const dialogCheckFailedNextAction = "uloop could not look in Unity's windows for a modal dialog, so one may be " +
	"open without being reported. If this state does not clear, ask the user to look at Unity"

// dialogCheckFailedNextActionFor is the hint status adds when the look fails.
// Why only macOS explains a permission: reading another process's windows needs permissions
// there and nothing on Windows, where a failed look has no setting behind it.
// Why it does not tell the reader to grant access outright: the look fails on every stalled
// status in a sandbox or without the permission, usually while no dialog is open.
func dialogCheckFailedNextActionFor(goos string) string {
	if goos != "darwin" {
		return dialogCheckFailedNextAction + "."
	}
	return dialogCheckFailedNextAction + "; on macOS, " + dialogMacPermissions + " lets uloop report the dialog itself."
}

// needsDialogInspection reports whether the answer leaves room for a dialog uloop must look for
// itself. Only a stalled main thread can hide one; Busy counts because a running command such as
// execute-dynamic-code or compile is the usual way a dialog opens.
func needsDialogInspection(state string, response editorStatusResponse) bool {
	if state == statusStateMainThreadBlocked {
		return true
	}
	return state == statusStateBusy &&
		response.SecondsSinceLastMainThreadTick >= editorStatusMainThreadStallThresholdSeconds
}

// withDialogInspection looks at Unity's windows when the report leaves room for a dialog, and turns
// the report into BlockedByDialog when it finds one. A failed look keeps the state and says why.
func withDialogInspection(
	ctx context.Context,
	projectRoot string,
	response editorStatusResponse,
	report statusReport,
	deps statusCommandDeps,
) statusReport {
	if !needsDialogInspection(report.State, response) {
		return report
	}
	return inspectForDialogs(ctx, projectRoot, report, deps, func(dialogs []statusDialog) statusReport {
		return blockedByDialogReport(response, dialogs)
	})
}

// inspectForDialogs looks at Unity's windows and returns blocked(dialogs) when it finds a dialog.
// A failed look keeps the report and says why.
func inspectForDialogs(
	ctx context.Context,
	projectRoot string,
	report statusReport,
	deps statusCommandDeps,
	blocked func(dialogs []statusDialog) statusReport,
) statusReport {
	dialogs, err := deps.inspectDialogs(ctx, projectRoot)
	// Where uloop cannot look, the report stays what it was before uloop looked for dialogs.
	if errors.Is(err, unitydialog.ErrInspectionUnsupported) {
		return report
	}
	if err != nil {
		report.DialogCheckError = err.Error()
		report.NextActions = append(report.NextActions, deps.dialogCheckNextAction)
		return report
	}
	if len(dialogs) == 0 {
		return report
	}
	return blocked(dialogs)
}

// withDialogInspectionBeforeServer looks for a dialog when Unity runs but its server does not
// answer: the dialogs Unity shows while it starts, such as the Safe Mode prompt, open before the
// server exists.
func withDialogInspectionBeforeServer(
	ctx context.Context,
	projectRoot string,
	report statusReport,
	deps statusCommandDeps,
) statusReport {
	return inspectForDialogs(ctx, projectRoot, report, deps, func(dialogs []statusDialog) statusReport {
		blockedReport := newStatusReport(statusStateBlockedByDialog, blockedByDialogMessage(dialogs))
		blockedReport.Dialogs = dialogs
		blockedReport.UnityProcessRunning = report.UnityProcessRunning
		return blockedReport
	})
}

// blockedByDialogReport describes an Editor held by the given dialogs, which must not be empty. A
// running command is still shown, but uloop cancel is not offered: the command cannot move until
// the dialog closes.
func blockedByDialogReport(response editorStatusResponse, dialogs []statusDialog) statusReport {
	report := newStatusReport(statusStateBlockedByDialog, blockedByDialogMessage(dialogs))
	addEditorStateFields(&report, response)
	report.Dialogs = dialogs
	// readEditorStatus already rejected a busy answer without elapsed seconds.
	if response.IsBusy {
		report.RunningToolName = response.RunningToolName
		report.RunningToolElapsedSeconds = response.RunningToolElapsedSeconds
		report.RunningToolPhase = response.RunningToolPhase
	}
	return report
}

// withDialogAnswerGuidance points a BlockedByDialog report at uloop press-dialog-button when
// uloop can press a button of the frontmost dialog, and at the user otherwise. Every path that
// builds a BlockedByDialog report passes through here, so the condition lives in one place.
func withDialogAnswerGuidance(report statusReport, canAnswerDialogs bool) statusReport {
	if report.State != statusStateBlockedByDialog {
		return report
	}
	report.NextActions = clicore.DialogAnswerNextActions(report.Dialogs, canAnswerDialogs, dialogClosedNextAction)
	return report
}

const dialogClosedNextAction = "After the dialog is closed, run uloop status again."

func blockedByDialogMessage(dialogs []statusDialog) string {
	if len(dialogs) > 1 {
		return fmt.Sprintf("%d modal dialogs are open in Unity; its main thread waits until they are closed.", len(dialogs))
	}
	if dialogs[0].Title == "" {
		return "A modal dialog is open in Unity; its main thread waits until it is closed."
	}
	return fmt.Sprintf("A modal dialog %q is open in Unity; its main thread waits until it is closed.", dialogs[0].Title)
}

// dialogAnswerFailedHintFor is added to a failed press where a missing permission can be the
// cause: only macOS needs one to press another process's buttons.
func dialogAnswerFailedHintFor(goos string) string {
	if goos != "darwin" {
		return ""
	}
	return "On macOS, pressing a button needs " + dialogMacPermissions + "."
}
