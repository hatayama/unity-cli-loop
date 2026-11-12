package unitydialog

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"strconv"
)

var errDialogPressUnsupported = errors.New("pressing a dialog button is supported only on macOS and Windows")

// PressResult is what one press attempt saw. DialogFound is false when no modal dialog was
// open; Pressed is false when Dialog has no button with the label. Dialog is read before the press.
type PressResult struct {
	DialogFound bool
	Pressed     bool
	Dialog      Dialog
}

// PressNotStartedError is a press that failed before anything reached the dialog: before
// osascript started on macOS, before the click was sent on Windows. No click can have happened,
// so pressing again is safe.
type PressNotStartedError struct {
	Err error
}

func (err PressNotStartedError) Error() string {
	return err.Err.Error()
}

func (err PressNotStartedError) Unwrap() error {
	return err.Err
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
func (inspector Inspector) CanPress() bool {
	return inspector.goos == "darwin" || inspector.goos == "windows"
}

// press presses the button labeled exactly label in the frontmost modal dialog of this project's
// Editor, and returns that dialog as it was before the press.
func (inspector Inspector) Press(ctx context.Context, projectRoot string, label string) (PressResult, error) {
	if !inspector.CanPress() {
		return PressResult{}, PressNotStartedError{Err: errDialogPressUnsupported}
	}
	pid, err := inspector.unityPid(ctx, projectRoot)
	if err != nil {
		return PressResult{}, PressNotStartedError{Err: err}
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
		return PressResult{}, stopErr
	}
	if err != nil {
		return PressResult{}, fmt.Errorf("osascript could not press the button: %w", err)
	}
	return pressResultFromAccessibilityOutput(output)
}

// pressResultFromAccessibilityOutput turns the press script's JSON into a result.
func pressResultFromAccessibilityOutput(output []byte) (PressResult, error) {
	var raw struct {
		ProcessFound bool `json:"processFound"`
		DialogFound  bool `json:"dialogFound"`
		Pressed      bool `json:"pressed"`
		accessibilityWindow
	}
	if err := json.Unmarshal(output, &raw); err != nil {
		return PressResult{}, fmt.Errorf("unreadable osascript output: %w", err)
	}
	if !raw.ProcessFound {
		// The script returns before its click when it finds no process, so nothing was pressed.
		return PressResult{}, PressNotStartedError{
			Err: errors.New("this project's Unity process is not listed by System Events"),
		}
	}
	if !raw.DialogFound {
		return PressResult{DialogFound: false}, nil
	}
	return PressResult{DialogFound: true, Pressed: raw.Pressed, Dialog: raw.dialog()}, nil
}
