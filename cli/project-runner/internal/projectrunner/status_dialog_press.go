package projectrunner

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"strconv"
)

var errDialogPressUnsupported = errors.New("pressing a dialog button is supported only on macOS and Windows")

// dialogPressNotStartedError is a press that failed before anything reached the dialog: before
// osascript started on macOS, before the click was sent on Windows. No click can have happened,
// so pressing again is safe.
type dialogPressNotStartedError struct {
	err error
}

func (err dialogPressNotStartedError) Error() string {
	return err.err.Error()
}

func (err dialogPressNotStartedError) Unwrap() error {
	return err.err
}

// accessibilityPressScript finds the first modal dialog window of the given process, reads its
// window title, static texts, and button names, presses the button whose name equals the label
// exactly, and prints what it read as JSON.
// Why it returns what it read before the press: once the dialog closes, the same window index
// points at another window, and reading after the press returned the main window's title in a
// real Editor.
// Why the reading and the press are one script, and why it clicks by name: only one call separates
// the reading from the press, and a click by name throws instead of pressing in a window that has
// no button with that label, so a dialog that closed in between cannot turn the click into a press
// of another window's button at the same index. It cannot tell apart a different dialog that
// replaced the first one in that moment and has a button with the same label.
// idx only decides whether to press at all.
// Why it skips a window with no title and no named button: Unity's splash screen is such a modal
// AXDialog while a project opens, and pressing must reach the real dialog listed after it.
const accessibilityPressScript = `function run(argv){const pid=parseInt(argv[0],10);const label=argv[1];const se=Application("System Events");const ps=se.processes.whose({unixId:pid});if(ps.length===0){return JSON.stringify({processFound:false});}const p=ps[0];for(let i=0;i<p.windows.length;i++){const w=p.windows[i];if(w.subrole()!=="AXDialog"){continue;}let modal=false;try{modal=w.attributes.byName("AXModal").value()===true;}catch(e){}if(!modal){continue;}const seen={processFound:true,dialogFound:true,pressed:false,windowTitle:w.title(),texts:w.staticTexts.value(),buttons:w.buttons.name()};if(!seen.windowTitle&&!seen.buttons.some(function(b){return !!b;})){continue;}const idx=seen.buttons.indexOf(label);if(idx<0){return JSON.stringify(seen);}w.buttons.byName(label).click();seen.pressed=true;return JSON.stringify(seen);}return JSON.stringify({processFound:true,dialogFound:false});}`

// canPress reports whether press works on this operating system.
func (inspector dialogInspector) canPress() bool {
	return inspector.goos == "darwin" || inspector.goos == "windows"
}

// press presses the button labeled exactly label in the frontmost modal dialog of this project's
// Editor, and returns that dialog as it was before the press.
func (inspector dialogInspector) press(ctx context.Context, projectRoot string, label string) (dialogPressResult, error) {
	if !inspector.canPress() {
		return dialogPressResult{}, dialogPressNotStartedError{err: errDialogPressUnsupported}
	}
	pid, err := inspector.unityPid(ctx, projectRoot)
	if err != nil {
		return dialogPressResult{}, dialogPressNotStartedError{err: err}
	}
	if inspector.goos == "windows" {
		return inspector.pressWin32Button(ctx, pid, label)
	}
	pressCtx, cancel := context.WithTimeout(ctx, inspector.timeout)
	defer cancel()
	output, err := inspector.runCommand(
		pressCtx, "osascript", "-l", "JavaScript", "-e", accessibilityPressScript, strconv.Itoa(pid), label)
	// Why before err: a stopped command fails with "signal: killed", which hides why it stopped.
	if stopErr := inspector.stoppedLookError(ctx, pressCtx, "osascript did not answer"); stopErr != nil {
		return dialogPressResult{}, stopErr
	}
	if err != nil {
		return dialogPressResult{}, fmt.Errorf("osascript could not press the button: %w", err)
	}
	return pressResultFromAccessibilityOutput(output)
}

// answerFailedHint is added to a failed press where a missing permission can be the cause: only
// macOS needs one to press another process's buttons.
func (inspector dialogInspector) answerFailedHint() string {
	if inspector.goos != "darwin" {
		return ""
	}
	return "On macOS, pressing a button needs " + dialogMacPermissions + "."
}

// pressResultFromAccessibilityOutput turns the press script's JSON into a result.
func pressResultFromAccessibilityOutput(output []byte) (dialogPressResult, error) {
	var raw struct {
		ProcessFound bool `json:"processFound"`
		DialogFound  bool `json:"dialogFound"`
		Pressed      bool `json:"pressed"`
		accessibilityWindow
	}
	if err := json.Unmarshal(output, &raw); err != nil {
		return dialogPressResult{}, fmt.Errorf("unreadable osascript output: %w", err)
	}
	if !raw.ProcessFound {
		// The script returns before its click when it finds no process, so nothing was pressed.
		return dialogPressResult{}, dialogPressNotStartedError{
			err: errors.New("this project's Unity process is not listed by System Events"),
		}
	}
	if !raw.DialogFound {
		return dialogPressResult{DialogFound: false}, nil
	}
	return dialogPressResult{DialogFound: true, Pressed: raw.Pressed, Dialog: raw.dialog()}, nil
}
