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
	return len(win32DialogButtons(window.Controls)) > 0
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

// dialog is what a modal window shows: its title, its text, and the labels of its push buttons.
func (window win32Window) dialog() statusDialog {
	return statusDialog{
		Title:   window.Title,
		Message: win32DialogMessage(window.Controls),
		Buttons: win32DialogButtons(window.Controls),
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

// win32DialogButtons returns the labels of a dialog's push buttons, leaving out a button that
// shows no label.
func win32DialogButtons(controls []win32Control) []string {
	buttons := win32PushButtons(controls)
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
// is the order its own dialog report and the macOS look give, while Windows draws Alt in the middle.
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
