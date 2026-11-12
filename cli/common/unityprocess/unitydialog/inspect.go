// Package unitydialog reads the modal dialogs open in a Unity Editor through the operating system,
// macOS Accessibility and Win32 windows, and presses their buttons. Inspector is its entry point.
package unitydialog

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"os/exec"
	"strconv"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// Dialog is one modal dialog: what the Editor reports in OpenDialogs and what uloop status
// prints in Dialogs. The tags are the Editor's PascalCase property names.
type Dialog struct {
	Title   string   `json:"Title"`
	Message string   `json:"Message"`
	Buttons []string `json:"Buttons"`
}

var ErrInspectionUnsupported = errors.New(
	"looking for dialogs in Unity's windows is supported only on macOS and Windows")

const dialogInspectionTimeout = 5 * time.Second

// accessibilityDialogScript asks System Events for every modal dialog window of the given process
// and prints each one's window title, static texts, and button names as JSON.
// Why AXModal as well as the AXDialog subrole: a non-modal utility window (EditorWindow.ShowUtility)
// is also an AXDialog, and it does not hold the main thread.
// Why it reads the windows one by one through an index: calling windows() on the whose() result
// returned only the main window while a dialog was open, in a real Editor.
const accessibilityDialogScript = `function run(argv){const pid=parseInt(argv[0],10);const se=Application("System Events");const ps=se.processes.whose({unixId:pid});if(ps.length===0){return JSON.stringify({processFound:false,dialogs:[]});}const p=ps[0];const out=[];for(let i=0;i<p.windows.length;i++){const w=p.windows[i];if(w.subrole()!=="AXDialog"){continue;}let modal=false;try{modal=w.attributes.byName("AXModal").value()===true;}catch(e){}if(!modal){continue;}out.push({windowTitle:w.title(),texts:w.staticTexts.value(),buttons:w.buttons.name()});}return JSON.stringify({processFound:true,dialogs:out});}`

// ProcessLookup finds the Unity Editor that has the project open; nil when none runs.
type ProcessLookup func(ctx context.Context, projectRoot string) (*unityprocess.UnityProcess, error)

// Inspector looks for modal dialogs in the Unity Editor's windows through the operating system,
// and presses their buttons. The fields are what this package's tests replace.
type Inspector struct {
	goos          string
	timeout       time.Duration
	lookupProcess ProcessLookup
	// Starts osascript on macOS.
	runCommand func(ctx context.Context, name string, args ...string) ([]byte, error)
	// Lists the Unity process's windows on Windows.
	listWindows func(ctx context.Context, pid int) ([]win32Window, error)
	// Presses the button chosen in a dialog on Windows; target always names a button.
	pressWindowButton func(ctx context.Context, target win32PressTargetResult) error
}

// NewInspector returns an inspector for the operating system named by goos (a runtime.GOOS value)
// that finds the Editor through lookupProcess. It looks only on macOS and Windows; elsewhere
// Inspect returns ErrInspectionUnsupported and CanPress is false, and neither looks up a process.
func NewInspector(goos string, lookupProcess ProcessLookup) Inspector {
	return Inspector{
		goos:              goos,
		timeout:           dialogInspectionTimeout,
		lookupProcess:     lookupProcess,
		runCommand:        runCommandCapturingStderr,
		listWindows:       listUnityProcessWindows,
		pressWindowButton: pressWin32WindowButton,
	}
}

// inspect returns the dialogs open in this project's Editor, or ErrInspectionUnsupported
// where it cannot look.
func (inspector Inspector) Inspect(ctx context.Context, projectRoot string) ([]Dialog, error) {
	// Why before the lookup: other operating systems must not start a process scan for nothing.
	if inspector.goos != "darwin" && inspector.goos != "windows" {
		return nil, ErrInspectionUnsupported
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
func (inspector Inspector) unityPid(ctx context.Context, projectRoot string) (int, error) {
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
func (inspector Inspector) inspectAccessibility(ctx context.Context, pid int) ([]Dialog, error) {
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
func (inspector Inspector) stoppedLookError(
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
func dialogsFromAccessibilityOutput(output []byte) ([]Dialog, error) {
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

	dialogs := make([]Dialog, 0, len(raw.Dialogs))
	for _, window := range raw.Dialogs {
		if window.isUntitledWithoutButtons() {
			continue
		}
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

// isUntitledWithoutButtons reports whether the window has neither a title nor a named button. Unity's
// splash screen is such a modal AXDialog while a project opens; it asks nothing and closes by itself.
// Why both conditions: an alert has no window title but always a named button, and a modal Editor
// window has unnamed buttons but a title, and both of those hold the main thread.
func (window accessibilityWindow) isUntitledWithoutButtons() bool {
	if window.WindowTitle != nil && *window.WindowTitle != "" {
		return false
	}
	return len(nonEmptyStrings(window.Buttons)) == 0
}

// dialog turns the window into a title, a message, and its named buttons. Buttons is never nil.
func (window accessibilityWindow) dialog() Dialog {
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
	return Dialog{
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
