package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"os/exec"
	"runtime"
	"strconv"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// statusDialog is one modal dialog: what the Editor reports in OpenDialogs and what uloop status
// prints in Dialogs. The tags are the Editor's PascalCase property names.
type statusDialog struct {
	Title   string   `json:"Title"`
	Message string   `json:"Message"`
	Buttons []string `json:"Buttons"`
}

var errDialogInspectionUnsupported = errors.New(
	"looking for dialogs in Unity's windows is supported only on macOS and Windows")

const dialogInspectionTimeout = 5 * time.Second

// accessibilityDialogScript asks System Events for every modal dialog window of the given process
// and prints each one's window title, static texts, and button names as JSON.
// Why AXModal as well as the AXDialog subrole: a non-modal utility window (EditorWindow.ShowUtility)
// is also an AXDialog, and it does not hold the main thread.
// Why it reads the windows one by one through an index: calling windows() on the whose() result
// returned only the main window while a dialog was open, in a real Editor.
const accessibilityDialogScript = `function run(argv){const pid=parseInt(argv[0],10);const se=Application("System Events");const ps=se.processes.whose({unixId:pid});if(ps.length===0){return JSON.stringify({processFound:false,dialogs:[]});}const p=ps[0];const out=[];for(let i=0;i<p.windows.length;i++){const w=p.windows[i];if(w.subrole()!=="AXDialog"){continue;}let modal=false;try{modal=w.attributes.byName("AXModal").value()===true;}catch(e){}if(!modal){continue;}out.push({windowTitle:w.title(),texts:w.staticTexts.value(),buttons:w.buttons.name()});}return JSON.stringify({processFound:true,dialogs:out});}`

// dialogInspector looks for modal dialogs in the Unity Editor's windows through the operating
// system. The fields are what tests replace.
type dialogInspector struct {
	goos          string
	timeout       time.Duration
	lookupProcess func(ctx context.Context, projectRoot string) (*unityprocess.UnityProcess, error)
	// Starts osascript on macOS.
	runCommand func(ctx context.Context, name string, args ...string) ([]byte, error)
	// Lists the Unity process's windows on Windows.
	listWindows func(ctx context.Context, pid int) ([]win32Window, error)
	// Presses the button chosen in a dialog on Windows; target always names a button.
	pressWindowButton func(ctx context.Context, target win32PressTargetResult) error
}

func defaultDialogInspector() dialogInspector {
	return dialogInspector{
		goos:              runtime.GOOS,
		timeout:           dialogInspectionTimeout,
		lookupProcess:     unityProcessLookup,
		runCommand:        runCommandCapturingStderr,
		listWindows:       listUnityProcessWindows,
		pressWindowButton: pressWin32WindowButton,
	}
}

// inspect returns the dialogs open in this project's Editor, or errDialogInspectionUnsupported
// where it cannot look.
func (inspector dialogInspector) inspect(ctx context.Context, projectRoot string) ([]statusDialog, error) {
	// Why before the lookup: other operating systems must not start a process scan for nothing.
	if inspector.goos != "darwin" && inspector.goos != "windows" {
		return nil, errDialogInspectionUnsupported
	}
	pid, err := inspector.unityPid(ctx, projectRoot)
	if err != nil {
		return nil, err
	}
	if inspector.goos == "windows" {
		return inspector.inspectWin32Windows(ctx, pid)
	}
	return inspector.inspectAccessibility(ctx, pid)
}

// unityPid finds the process ID of this project's Unity Editor.
func (inspector dialogInspector) unityPid(ctx context.Context, projectRoot string) (int, error) {
	process, err := inspector.lookupProcess(ctx, projectRoot)
	if err != nil {
		return 0, fmt.Errorf("could not find this project's Unity process: %w", err)
	}
	if process == nil {
		return 0, errors.New("no Unity process runs for this project")
	}
	return process.Pid, nil
}

// inspectAccessibility asks System Events for the modal dialogs of the Unity process on macOS.
func (inspector dialogInspector) inspectAccessibility(ctx context.Context, pid int) ([]statusDialog, error) {
	inspectCtx, cancel := context.WithTimeout(ctx, inspector.timeout)
	defer cancel()
	output, err := inspector.runCommand(
		inspectCtx, "osascript", "-l", "JavaScript", "-e", accessibilityDialogScript, strconv.Itoa(pid))
	// Why before err: a stopped command fails with "signal: killed", which hides why it stopped.
	if stopErr := inspector.stoppedLookError(ctx, inspectCtx, "osascript did not answer"); stopErr != nil {
		return nil, stopErr
	}
	if err != nil {
		return nil, fmt.Errorf("osascript could not read Unity's windows: %w", err)
	}
	return dialogsFromAccessibilityOutput(output)
}

// stoppedLookError says why a look stopped before it finished: the caller cancelled it, or it ran
// out of time, which is worded with unanswered. It returns nil for a look that was not stopped.
func (inspector dialogInspector) stoppedLookError(
	ctx context.Context,
	inspectCtx context.Context,
	unanswered string,
) error {
	if ctx.Err() != nil {
		return fmt.Errorf("looking for dialogs was cancelled: %w", ctx.Err())
	}
	if inspectCtx.Err() != nil {
		return fmt.Errorf("%s within %s", unanswered, inspector.timeout)
	}
	return nil
}

// runCommandCapturingStderr runs a command and puts its stderr into the error, because osascript
// explains a denied permission only there.
func runCommandCapturingStderr(ctx context.Context, name string, args ...string) ([]byte, error) {
	var stderr bytes.Buffer
	command := exec.CommandContext(ctx, name, args...)
	command.Stderr = &stderr
	output, err := command.Output()
	if err != nil {
		return nil, fmt.Errorf("%w: %s", err, strings.TrimSpace(stderr.String()))
	}
	return output, nil
}

// dialogsFromAccessibilityOutput turns the script's JSON into dialogs. Buttons is never nil, so a
// dialog whose buttons have no names prints [].
func dialogsFromAccessibilityOutput(output []byte) ([]statusDialog, error) {
	var raw struct {
		ProcessFound bool                  `json:"processFound"`
		Dialogs      []accessibilityWindow `json:"dialogs"`
	}
	if err := json.Unmarshal(output, &raw); err != nil {
		return nil, fmt.Errorf("unreadable osascript output: %w", err)
	}
	if !raw.ProcessFound {
		return nil, errors.New("this project's Unity process is not listed by System Events")
	}

	dialogs := make([]statusDialog, 0, len(raw.Dialogs))
	for _, window := range raw.Dialogs {
		dialogs = append(dialogs, window.dialog())
	}
	return dialogs, nil
}

// accessibilityWindow is one dialog window as the accessibility scripts print it.
type accessibilityWindow struct {
	WindowTitle *string   `json:"windowTitle"`
	Texts       []*string `json:"texts"`
	Buttons     []*string `json:"buttons"`
}

// dialog turns the window into a title, a message, and its named buttons. Buttons is never nil.
func (window accessibilityWindow) dialog() statusDialog {
	texts := nonEmptyStrings(window.Texts)
	title := ""
	if window.WindowTitle != nil {
		title = *window.WindowTitle
	}
	if title == "" && len(texts) > 0 {
		// An alert has no window title; its first text is the title.
		title, texts = texts[0], texts[1:]
	} else {
		// A modal Editor window repeats its title as a text.
		texts = stringsExcept(texts, title)
	}
	return statusDialog{
		Title:   title,
		Message: strings.Join(texts, "\n"),
		Buttons: nonEmptyStrings(window.Buttons),
	}
}

// nonEmptyStrings drops null and empty entries; window buttons such as close have no name.
func nonEmptyStrings(values []*string) []string {
	result := make([]string, 0, len(values))
	for _, value := range values {
		if value != nil && *value != "" {
			result = append(result, *value)
		}
	}
	return result
}

func stringsExcept(values []string, excluded string) []string {
	result := make([]string, 0, len(values))
	for _, value := range values {
		if value != excluded {
			result = append(result, value)
		}
	}
	return result
}

// dialogAccessibilitySetting names the macOS permission that both looking at and pressing in
// another process's windows need.
const dialogAccessibilitySetting = "Accessibility access for the app that runs uloop " +
	"(System Settings > Privacy & Security > Accessibility)"

const dialogCheckFailedNextAction = "uloop could not look in Unity's windows for a modal dialog, so one may be " +
	"open without being reported. If this state does not clear, ask the user to look at Unity"

// checkFailedNextAction is the hint status adds when the look fails.
// Why only macOS explains a permission: reading another process's windows needs Accessibility
// access there and nothing on Windows, where a failed look has no setting behind it.
// Why it does not tell the reader to grant access outright: the look fails on every stalled
// status in a sandbox or without the permission, usually while no dialog is open.
func (inspector dialogInspector) checkFailedNextAction() string {
	if inspector.goos != "darwin" {
		return dialogCheckFailedNextAction + "."
	}
	return dialogCheckFailedNextAction + "; on macOS, " + dialogAccessibilitySetting + " lets uloop report the dialog itself."
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
	if errors.Is(err, errDialogInspectionUnsupported) {
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
	if !canAnswerDialogs || len(report.Dialogs) == 0 || len(report.Dialogs[0].Buttons) == 0 {
		report.NextActions = unanswerableDialogNextActions()
		return report
	}
	report.NextActions = []string{
		"Read the dialog's Title, Message, and Buttons, then press the button that fits what the user asked for: " +
			"uloop press-dialog-button --label \"<button>\".",
		"Ask the user first when no button clearly fits or the choice would discard work (such as \"Don't Save\").",
		dialogClosedNextAction,
	}
	return report
}

const dialogClosedNextAction = "After the dialog is closed, run uloop status again."

func unanswerableDialogNextActions() []string {
	return []string{
		"uloop cannot press a button in this dialog. Show the user its Title and Message and ask them to answer it in Unity.",
		dialogClosedNextAction,
	}
}

func blockedByDialogMessage(dialogs []statusDialog) string {
	if len(dialogs) > 1 {
		return fmt.Sprintf("%d modal dialogs are open in Unity; its main thread waits until they are closed.", len(dialogs))
	}
	if dialogs[0].Title == "" {
		return "A modal dialog is open in Unity; its main thread waits until it is closed."
	}
	return fmt.Sprintf("A modal dialog %q is open in Unity; its main thread waits until it is closed.", dialogs[0].Title)
}
