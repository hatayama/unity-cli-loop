package projectrunner

import (
	"context"
	"fmt"
	"sort"
	"strings"
)

// inspectWin32Windows lists the Unity process's windows on Windows and returns the modal ones.
// Why not a command as on macOS: Win32 answers in-process, needs no permission, and starts nothing.
func (inspector dialogInspector) inspectWin32Windows(ctx context.Context, pid int) ([]statusDialog, error) {
	inspectCtx, cancel := context.WithTimeout(ctx, inspector.timeout)
	defer cancel()
	windows, err := inspector.listWindows(inspectCtx, pid)
	// Why before err: a stopped listing returns the context's error, which hides why it stopped.
	stopErr := inspector.stoppedLookError(ctx, inspectCtx, "reading Unity's windows did not finish")
	if stopErr != nil {
		return nil, stopErr
	}
	if err != nil {
		return nil, fmt.Errorf("could not read Unity's windows: %w", err)
	}
	return dialogsFromWin32Windows(windows), nil
}

// win32Window is one top-level window of the Unity process as Win32 reports it. It carries what
// tells a modal window from the others, and the handle a press is sent to; Controls is read only
// for one that may be modal.
type win32Window struct {
	// What Windows names the window by. A press is sent to it; no rule here reads it.
	Handle    uintptr
	ClassName string
	Title     string
	Visible   bool
	Enabled   bool
	HasOwner  bool
	// Meaningful only when HasOwner is true.
	OwnerEnabled bool
	Controls     []win32Control
}

// win32Control is one child window of a window that may be modal: a label, a text box, a button,
// or a progress bar.
type win32Control struct {
	// What Windows names the control by, as win32Window.Handle.
	Handle    uintptr
	ClassName string
	Text      string
	ID        int
	Style     uint32
	Visible   bool
}

const (
	win32DialogClassName      = "#32770"
	win32StaticClassName      = "Static"
	win32EditClassName        = "Edit"
	win32ButtonClassName      = "Button"
	win32ProgressBarClassName = "msctls_progress32"

	// The low four bits of a Button's style say what kind of button it is.
	win32ButtonTypeMask        = 0xF
	win32ButtonTypePush        = 0x0
	win32ButtonTypeDefaultPush = 0x1

	// WS_DISABLED: the window, here a control, takes no input from the user.
	win32StyleDisabled = 0x08000000

	// ES_READONLY: the user cannot change the text of the Edit.
	win32EditStyleReadOnly = 0x0800
)

// isModal reports whether the window holds Unity's main thread. Unity disables every other window
// of the Editor while a modal one is open, so a modal window is the enabled one whose owner is
// disabled.
// Why not the dialog class: a progress window ("Reloading Domain") is also a #32770 dialog, but it
// has no owner and leaves the main window enabled; and a modal Editor window is not a #32770.
// Why the window must be enabled itself: a non-modal utility window (EditorWindow.ShowUtility) is
// owned by the main window too, and a dialog opened above it disables both.
// Why a window with no owner can still be one: a prompt shown while the Editor starts, such as the
// one that offers Safe Mode, opens before the main window exists.
func (window win32Window) isModal() bool {
	if !window.mayBeModal() {
		return false
	}
	return window.HasOwner || window.isMessageBox()
}

// mayBeModal reports what the window alone tells, before its controls are read: whether it can
// be modal. It is what decides which windows have their controls read.
func (window win32Window) mayBeModal() bool {
	if !window.Visible || !window.Enabled {
		return false
	}
	if window.HasOwner {
		return !window.OwnerEnabled
	}
	return isWin32Class(window.ClassName, win32DialogClassName)
}

// isMessageBox tells a message box from a progress window, by the controls of a dialog that has
// no owner: a progress window always holds a progress bar, and shows a Cancel button when the
// progress can be cancelled, so a button alone does not make a message box.
func (window win32Window) isMessageBox() bool {
	for _, control := range window.Controls {
		if control.hasClass(win32ProgressBarClassName) {
			return false
		}
	}
	return len(win32ButtonLabels(win32PushButtons(window.Controls))) > 0
}

// isBuiltLikeAMessageBox reports whether the window holds nothing but what a message box is built
// from: labels, read-only text boxes, and buttons.
// Why it is asked at all: on Windows a file picker is a dialog like a message box, and its Save,
// Select Folder, and Cancel are push buttons with labels. uloop answers message boxes only; what
// a picker takes is a choice of a file, which is the user's to make.
// Why by what a message box holds and not by what a picker holds: a dialog uloop has never seen
// must not become one it presses. A real Editor's message boxes held these three kinds only, and
// each of its three file pickers held a dozen other kinds.
// Why a hidden control counts too: it is part of what the dialog is built from, and counting it
// can only keep uloop from pressing.
func (window win32Window) isBuiltLikeAMessageBox() bool {
	for _, control := range window.Controls {
		if !control.isMessageBoxPart() {
			return false
		}
	}
	return true
}

// isMessageBoxPart reports whether the control is one of the kinds a message box is built from.
// Why a text box must be read-only: a dialog with a text box the user can type in is a form, and
// pressing its button sends whatever the box holds at that moment. A message box keeps its message
// in a text box the user cannot type in: every one a real Editor showed was read-only (Unity
// 2022.3 and 6000.7, from a plain two-button box to the prompt that offers Safe Mode).
func (control win32Control) isMessageBoxPart() bool {
	if control.hasClass(win32EditClassName) {
		return control.isReadOnly()
	}
	return control.hasClass(win32StaticClassName) || control.hasClass(win32ButtonClassName)
}

// answerButtons returns the buttons uloop may list and press in the window: the visible push
// buttons of a message box, and none of any other dialog. Both the listing and the press take
// their buttons from here, so a button that is not listed cannot be pressed.
func (window win32Window) answerButtons() []win32Control {
	if !window.isBuiltLikeAMessageBox() {
		return []win32Control{}
	}
	return win32PushButtons(window.Controls)
}

// dialogsFromWin32Windows turns the Unity process's windows into the dialogs that hold its main
// thread, in the order given. The result is never nil, and neither is a dialog's Buttons.
func dialogsFromWin32Windows(windows []win32Window) []statusDialog {
	dialogs := make([]statusDialog, 0)
	for _, window := range win32ModalWindows(windows) {
		dialogs = append(dialogs, window.dialog())
	}
	return dialogs
}

// win32ModalWindows keeps the windows that hold Unity's main thread, in the order given. Both the
// listing and the press take their dialogs from here, so they agree on which window is one.
func win32ModalWindows(windows []win32Window) []win32Window {
	modalWindows := make([]win32Window, 0)
	for _, window := range windows {
		if window.isModal() {
			modalWindows = append(modalWindows, window)
		}
	}
	return modalWindows
}

// dialog is what a modal window shows: its title, its text, and the labels of the buttons uloop
// may press in it.
func (window win32Window) dialog() statusDialog {
	return statusDialog{
		Title:   window.Title,
		Message: win32DialogMessage(window.Controls),
		Buttons: win32ButtonLabels(window.answerButtons()),
	}
}

// win32DialogMessage joins the texts a dialog shows. Unity's message box keeps its message in a
// read-only Edit; a Static is where other dialogs keep theirs.
func win32DialogMessage(controls []win32Control) string {
	texts := make([]string, 0, len(controls))
	for _, control := range controls {
		if !control.Visible || control.Text == "" {
			continue
		}
		if !control.hasClass(win32StaticClassName) && !control.hasClass(win32EditClassName) {
			continue
		}
		// An Edit control breaks its lines with CRLF; the Editor's own report uses LF.
		texts = append(texts, strings.ReplaceAll(control.Text, "\r\n", "\n"))
	}
	return strings.Join(texts, "\n")
}

// win32ButtonLabels returns the labels of the buttons, leaving out a button that shows no label.
func win32ButtonLabels(buttons []win32Control) []string {
	labels := make([]string, 0, len(buttons))
	for _, button := range buttons {
		label := withoutMnemonicMarker(button.Text)
		if label != "" {
			labels = append(labels, label)
		}
	}
	return labels
}

// win32PushButtons returns a dialog's visible push buttons.
// Why sorted by control ID and not left to right: Unity numbers the buttons OK, Cancel, Alt, which
// is the order the macOS look gives, while Windows draws Alt in the middle. The Editor's own dialog
// report on Windows (Unity 6000.7) lists them as drawn, OK, Alt, Cancel, so the two orders can differ.
func win32PushButtons(controls []win32Control) []win32Control {
	buttons := make([]win32Control, 0, len(controls))
	for _, control := range controls {
		if control.Visible && control.isPushButton() {
			buttons = append(buttons, control)
		}
	}
	sort.SliceStable(buttons, func(i int, j int) bool {
		return buttons[i].ID < buttons[j].ID
	})
	return buttons
}

func (control win32Control) hasClass(className string) bool {
	return isWin32Class(control.ClassName, className)
}

// Window class names compare without case in Win32.
func isWin32Class(className string, expected string) bool {
	return strings.EqualFold(className, expected)
}

// isPushButton leaves out check boxes, radio buttons, and group boxes, which share the Button class.
func (control win32Control) isPushButton() bool {
	if !control.hasClass(win32ButtonClassName) {
		return false
	}
	buttonType := control.Style & win32ButtonTypeMask
	return buttonType == win32ButtonTypePush || buttonType == win32ButtonTypeDefaultPush
}

// isDisabled reports whether the user cannot click the control.
func (control win32Control) isDisabled() bool {
	return control.Style&win32StyleDisabled != 0
}

// isReadOnly reports whether the user cannot change the text of the control. It is meaningful for
// an Edit only: the same bit of another class's style says something else.
func (control win32Control) isReadOnly() bool {
	return control.Style&win32EditStyleReadOnly != 0
}

// withoutMnemonicMarker removes the "&" Unity puts before a button's keyboard shortcut letter
// ("&Yes", "O&K").
// Why only the first "&" that has a letter after it: Unity adds one marker and does not escape
// an "&" the label already had, so "&Save & Quit" must stay "Save & Quit".
func withoutMnemonicMarker(label string) string {
	runes := []rune(label)
	for index := 0; index < len(runes)-1; index++ {
		if runes[index] != '&' {
			continue
		}
		next := runes[index+1]
		if next == ' ' || next == '&' {
			continue
		}
		return string(runes[:index]) + string(runes[index+1:])
	}
	return label
}
