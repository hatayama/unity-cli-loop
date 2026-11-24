package unitydialog

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"slices"
	"strconv"
)

// DialogAnswer is an answer uloop gives a dialog it knows: press Label in the frontmost modal dialog
// when that dialog's title is Title and its buttons are exactly Buttons, in order.
// Why the buttons as well as the title: another dialog could share the title, and only the whole
// set of buttons says what pressing Label there does.
type DialogAnswer struct {
	Title   string
	Buttons []string
	Label   string
}

// Matches reports whether dialog is the dialog the answer names.
func (answer DialogAnswer) Matches(dialog Dialog) bool {
	return dialog.Title == answer.Title && slices.Equal(dialog.Buttons, answer.Buttons)
}

// validate rejects an answer that names no dialog, or whose label is not one of its buttons.
func (answer DialogAnswer) validate() error {
	if answer.Title == "" {
		return errors.New("a dialog answer needs the title of the dialog it answers")
	}
	if !slices.Contains(answer.Buttons, answer.Label) {
		return fmt.Errorf("a dialog answer must press one of its buttons %q, not %q", answer.Buttons, answer.Label)
	}
	return nil
}

// AnswerResult is what one InspectAndAnswer call saw. Dialogs are the modal dialogs as read before
// any press, frontmost first, as Inspect returns them. Matched is whether the frontmost one is the
// dialog the answer names, and Pressed whether its label was pressed. Matched without Pressed is a
// press that did not happen or failed, which leaves the dialog for a person to answer.
type AnswerResult struct {
	Dialogs []Dialog
	Matched bool
	Pressed bool
}

// accessibilityAnswerScript reads the modal dialogs of the given process as accessibilityDialogScript
// does and, in the same call, presses the answer's label in the frontmost one when its title and its
// named buttons equal the answer's. The answer is the second argument, as JSON.
// Why the reading and the press are one script: the frontmost dialog is pressed right after it was
// read, without a second osascript call, which takes about as long as the reading.
// Why the frontmost dialog is the first window with a title or a named button: Unity's splash screen
// and its progress windows are modal AXDialogs that have neither, and a real Editor listed its
// compile progress before the Safe Mode prompt. dialogsFromAccessibilityWindows leaves the same
// windows out, and the title is decided as accessibilityWindow.dialog decides it.
// Why it clicks by name, and catches a failed click: a click by name throws instead of pressing a
// window that no longer has the button, and a caught failure still returns what was read, so the
// caller learns the dialog matched and was not pressed.
const accessibilityAnswerScript = `function run(argv){const pid=parseInt(argv[0],10);const answer=JSON.parse(argv[1]);const se=Application("System Events");const ps=se.processes.whose({unixId:pid});if(ps.length===0){return JSON.stringify({processFound:false,dialogs:[]});}const p=ps[0];const out=[];const found=[];for(let i=0;i<p.windows.length;i++){const w=p.windows[i];if(w.subrole()!=="AXDialog"){continue;}let modal=false;try{modal=w.attributes.byName("AXModal").value()===true;}catch(e){}if(!modal){continue;}out.push({windowTitle:w.title(),texts:w.staticTexts.value(),buttons:w.buttons.name()});found.push(w);}const result={processFound:true,dialogs:out,matched:false,pressed:false};const named=function(values){return (values||[]).filter(function(v){return !!v;});};let front=-1;for(let k=0;k<out.length;k++){if(out[k].windowTitle||named(out[k].buttons).length>0){front=k;break;}}if(front<0){return JSON.stringify(result);}const d=out[front];const title=d.windowTitle||named(d.texts)[0]||"";const buttons=named(d.buttons);if(title!==answer.title||buttons.length!==answer.buttons.length||buttons.some(function(b,k){return b!==answer.buttons[k];})){return JSON.stringify(result);}result.matched=true;try{found[front].buttons.byName(answer.label).click();result.pressed=true;}catch(e){}return JSON.stringify(result);}`

// InspectAndAnswer reads the dialogs open in this project's Editor, as Inspect does, and in the same
// look presses answer's label in the frontmost dialog when that dialog is the one answer names.
// An error means the look did not finish and nothing was read; a look stopped by ctx or by the
// timeout after it read the dialog may still have pressed it. An answer without a title or whose
// label is not one of its buttons is a caller error, refused before anything runs. Where dialogs
// cannot be read, the error is ErrInspectionUnsupported.
func (inspector Inspector) InspectAndAnswer(ctx context.Context, projectRoot string, answer DialogAnswer) (AnswerResult, error) {
	if err := answer.validate(); err != nil {
		return AnswerResult{}, err
	}
	if inspector.goos != "darwin" && inspector.goos != "windows" {
		return AnswerResult{}, ErrInspectionUnsupported
	}
	pid, err := inspector.unityPid(ctx, projectRoot)
	if err != nil {
		return AnswerResult{}, err
	}
	if inspector.goos == "windows" {
		return inspector.answerWin32Dialog(ctx, pid, answer)
	}
	return inspector.answerAccessibilityDialog(ctx, pid, answer)
}

// answerAccessibilityDialog runs the answer script for the Unity process on macOS.
func (inspector Inspector) answerAccessibilityDialog(ctx context.Context, pid int, answer DialogAnswer) (AnswerResult, error) {
	answerJSON, err := json.Marshal(struct {
		Title   string   `json:"title"`
		Buttons []string `json:"buttons"`
		Label   string   `json:"label"`
	}{Title: answer.Title, Buttons: answer.Buttons, Label: answer.Label})
	if err != nil {
		return AnswerResult{}, fmt.Errorf("could not encode the dialog answer: %w", err)
	}
	answerCtx, cancel := context.WithTimeout(ctx, inspector.timeout)
	defer cancel()
	output, err := inspector.runCommand(
		answerCtx, "osascript", "-l", "JavaScript", "-e", accessibilityAnswerScript, strconv.Itoa(pid), string(answerJSON))
	// Why before err: a stopped command fails with "signal: killed", which hides why it stopped.
	if stopErr := inspector.stoppedLookError(ctx, answerCtx, "osascript did not answer"); stopErr != nil {
		return AnswerResult{}, stopErr
	}
	if err != nil {
		return AnswerResult{}, fmt.Errorf("osascript could not read or answer Unity's dialogs: %w", err)
	}
	return answerResultFromAccessibilityOutput(output, answer)
}

// answerResultFromAccessibilityOutput turns the answer script's JSON into a result.
func answerResultFromAccessibilityOutput(output []byte, answer DialogAnswer) (AnswerResult, error) {
	var raw struct {
		ProcessFound bool                  `json:"processFound"`
		Dialogs      []accessibilityWindow `json:"dialogs"`
		Matched      bool                  `json:"matched"`
		Pressed      bool                  `json:"pressed"`
	}
	if err := json.Unmarshal(output, &raw); err != nil {
		return AnswerResult{}, fmt.Errorf("unreadable osascript output: %w", err)
	}
	if !raw.ProcessFound {
		return AnswerResult{}, errors.New("this project's Unity process is not listed by System Events")
	}
	return newAnswerResult(dialogsFromAccessibilityWindows(raw.Dialogs), raw.Matched, raw.Pressed, answer)
}

// newAnswerResult returns the result the script reported, after checking its verdict against the
// dialogs it read.
// Why the check: the script decides which dialog is frontmost and whether it matches in JavaScript,
// apart from the Go that reads the dialogs, and a result that contradicts itself must not reach a
// caller that acts on Matched and Pressed.
func newAnswerResult(dialogs []Dialog, matched bool, pressed bool, answer DialogAnswer) (AnswerResult, error) {
	frontmostMatches := len(dialogs) > 0 && answer.Matches(dialogs[0])
	if matched != frontmostMatches || (pressed && !matched) {
		return AnswerResult{}, fmt.Errorf(
			"the dialog answer reported matched=%v and pressed=%v, which the dialogs it read do not bear out", matched, pressed)
	}
	return AnswerResult{Dialogs: dialogs, Matched: matched, Pressed: pressed}, nil
}

// answerWin32Dialog reads the Unity process's windows on Windows once, and presses the answer's label
// in the frontmost modal window when it is the dialog the answer names.
// Why a failed press is a result and not an error: the dialogs were read, and the caller needs them
// to report the dialog it could not answer.
func (inspector Inspector) answerWin32Dialog(ctx context.Context, pid int, answer DialogAnswer) (AnswerResult, error) {
	answerCtx, cancel := context.WithTimeout(ctx, inspector.timeout)
	defer cancel()
	windows, err := inspector.listWindows(answerCtx, pid)
	// Why before err: a stopped listing returns the context's error, which hides why it stopped.
	if stopErr := inspector.stoppedLookError(ctx, answerCtx, "reading Unity's windows did not finish"); stopErr != nil {
		return AnswerResult{}, stopErr
	}
	if err != nil {
		return AnswerResult{}, fmt.Errorf("could not read Unity's windows: %w", err)
	}

	result := AnswerResult{Dialogs: dialogsFromWin32Windows(windows)}
	target := win32PressTarget(windows, answer.Label)
	// Why the target's dialog is compared: the press goes to the target's handle, so the dialog read
	// here is the one that would be pressed. It is also the first of result.Dialogs.
	if !target.DialogFound || !answer.Matches(target.Dialog) {
		return result, nil
	}
	result.Matched = true
	// Why a disabled button is not pressed: the dialog would act on the click all the same, and uloop
	// must not do what the dialog does not let the user do.
	if !target.ButtonFound || target.ButtonDisabled {
		return result, nil
	}
	if err := inspector.pressWindowButton(answerCtx, target); err != nil {
		return result, nil
	}
	result.Pressed = true
	return result, nil
}
