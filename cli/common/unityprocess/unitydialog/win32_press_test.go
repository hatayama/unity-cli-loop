package unitydialog

import (
	"reflect"
	"testing"
)

const (
	win32ProbeDialogHandle      uintptr = 0x1000
	win32ProbeYesButtonHandle   uintptr = 0x1001
	win32ProbeNoButtonHandle    uintptr = 0x1002
	win32ProbeLaterButtonHandle uintptr = 0x1003
)

// The windows a real Editor on Windows listed while DisplayDialogComplex("Probe title", ..., "Yes",
// "No", "Later") was open, with the handles the listing gives each window and control.
func win32ProbeDialogWindows() []win32Window {
	dialog := win32ModalWindow("#32770", "Probe title",
		win32Control{ClassName: "Static", Text: "", ID: 1040, Style: 0x5000080E, Visible: true, Handle: 0x1010},
		win32Control{ClassName: "Edit", Text: "Line one. Line two.", ID: 1019, Style: 0x50000804, Visible: true, Handle: 0x1011},
		win32Control{ClassName: "Button", Text: "&Yes", ID: 1, Style: 0x50010001, Visible: true, Handle: win32ProbeYesButtonHandle, Left: 210, Top: 420, HasPosition: true},
		win32Control{ClassName: "Button", Text: "&Later", ID: 3, Style: 0x50010000, Visible: true, Handle: win32ProbeLaterButtonHandle, Left: 300, Top: 420, HasPosition: true},
		win32Control{ClassName: "Button", Text: "&No", ID: 2, Style: 0x50010000, Visible: true, Handle: win32ProbeNoButtonHandle, Left: 390, Top: 420, HasPosition: true})
	dialog.Handle = win32ProbeDialogHandle
	return []win32Window{dialog, win32DisabledMainWindow, win32InputMethodWindow}
}

// Verifies the button whose label equals the answer is chosen by its handle and control ID, and
// the dialog returned is the one the listing shows first.
func TestWin32PressTargetFindsTheLabeledButton(t *testing.T) {
	windows := win32ProbeDialogWindows()

	got := win32PressTarget(windows, "Later")

	want := win32PressTargetResult{
		DialogFound:  true,
		ButtonFound:  true,
		Dialog:       dialogsFromWin32Windows(windows)[0],
		DialogHandle: win32ProbeDialogHandle,
		ButtonHandle: win32ProbeLaterButtonHandle,
		ButtonID:     3,
	}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("target = %#v, want %#v", got, want)
	}
}

// Verifies a label that differs only by case, by a mnemonic marker, or by a space chooses no
// button, while the dialog and its three buttons are still returned.
func TestWin32PressTargetMatchesTheLabelExactly(t *testing.T) {
	for _, label := range []string{"later", "&Later", " Later"} {
		t.Run(label, func(t *testing.T) {
			got := win32PressTarget(win32ProbeDialogWindows(), label)

			if !got.DialogFound || got.ButtonFound || got.ButtonHandle != 0 {
				t.Fatalf("target = %#v, want the dialog without a button", got)
			}
			if !reflect.DeepEqual(got.Dialog.Buttons, []string{"Yes", "Later", "No"}) {
				t.Fatalf("buttons = %#v, want the three of the dialog", got.Dialog.Buttons)
			}
		})
	}
}

// Verifies windows that do not hold Unity's main thread give no dialog, even when one of them has
// a visible push button with the label: the main window, and a progress window with a Cancel button.
func TestWin32PressTargetIgnoresWindowsThatAreNotDialogs(t *testing.T) {
	windows := []win32Window{win32ProgressWindow("Importing", true), win32EnabledMainWindow, win32InputMethodWindow}

	for _, label := range []string{"OK", "Cancel"} {
		t.Run(label, func(t *testing.T) {
			got := win32PressTarget(windows, label)

			if !reflect.DeepEqual(got, win32PressTargetResult{}) {
				t.Fatalf("target = %#v, want no dialog", got)
			}
		})
	}
}

// Verifies a modal Editor window, which has no push button, is returned as a dialog with an empty,
// non-nil button list and no button to press.
func TestWin32PressTargetReportsADialogWithoutButtons(t *testing.T) {
	modal := win32ModalWindow("UnityContainerWndClass", "Probe modal",
		win32Control{ClassName: "UnityGUIViewWndClass", Text: "UnityEditor.EditorWindow", Visible: true, Handle: 0x2001})
	modal.Handle = 0x2000

	got := win32PressTarget([]win32Window{modal, win32DisabledMainWindow}, "OK")

	want := win32PressTargetResult{
		DialogFound:  true,
		Dialog:       Dialog{Title: "Probe modal", Message: "", Buttons: []string{}},
		DialogHandle: 0x2000,
	}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("target = %#v, want %#v", got, want)
	}
}

// Verifies no button of a file picker is chosen, neither the one that takes the choice nor Cancel,
// while the picker is returned as a dialog with an empty, non-nil button list.
func TestWin32PressTargetChoosesNoButtonInAFilePicker(t *testing.T) {
	picker := win32FilePickerWindow("Probe save", win32FilePickerSaveButton)
	picker.Handle = 0x7000
	for index := range picker.Controls {
		picker.Controls[index].Handle = 0x7001 + uintptr(index)
	}

	for _, label := range []string{"Save", "Cancel"} {
		t.Run(label, func(t *testing.T) {
			got := win32PressTarget([]win32Window{picker, win32DisabledMainWindow}, label)

			want := win32PressTargetResult{
				DialogFound:  true,
				Dialog:       Dialog{Title: "Probe save", Message: "File &name:", Buttons: []string{}},
				DialogHandle: 0x7000,
			}
			if !reflect.DeepEqual(got, want) {
				t.Fatalf("target = %#v, want %#v", got, want)
			}
		})
	}
}

// Verifies only the first dialog is searched: a button with the label in the dialog behind it is
// not chosen.
func TestWin32PressTargetLooksOnlyAtTheFirstDialog(t *testing.T) {
	first := win32ModalWindow("#32770", "First",
		win32Control{ClassName: "Button", Text: "&Yes", ID: 1, Visible: true, Handle: 0x3001},
		win32Control{ClassName: "Button", Text: "&No", ID: 2, Visible: true, Handle: 0x3002})
	first.Handle = 0x3000
	second := win32ModalWindow("#32770", "Second",
		win32Control{ClassName: "Button", Text: "&OK", ID: 1, Visible: true, Handle: 0x4001})
	second.Handle = 0x4000

	got := win32PressTarget([]win32Window{first, second, win32DisabledMainWindow}, "OK")

	want := win32PressTargetResult{
		DialogFound:  true,
		Dialog:       Dialog{Title: "First", Message: "", Buttons: []string{"Yes", "No"}},
		DialogHandle: 0x3000,
	}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("target = %#v, want %#v", got, want)
	}
}

// Verifies a button of the prompt Unity shows while it starts, a message box with no owner, is
// chosen.
func TestWin32PressTargetFindsAStartupPromptButton(t *testing.T) {
	prompt := win32StartupMessageBox()
	prompt.Handle = 0x5000
	for index := range prompt.Controls {
		prompt.Controls[index].Handle = 0x5001 + uintptr(index)
	}

	got := win32PressTarget([]win32Window{prompt, win32InputMethodWindow}, "Ignore")

	// "&Ignore" is the fourth control of the prompt.
	if !got.DialogFound || !got.ButtonFound || got.ButtonHandle != 0x5004 || got.ButtonID != 3 || got.DialogHandle != 0x5000 {
		t.Fatalf("target = %#v, want the Ignore button of the prompt", got)
	}
	if !reflect.DeepEqual(got.Dialog.Buttons, []string{"Enter Safe Mode", "Ignore", "Quit"}) {
		t.Fatalf("buttons = %#v, want the three of the prompt", got.Dialog.Buttons)
	}
}

// Verifies that of two buttons with the same label the one with the smaller control ID is chosen,
// which is the one the listing shows first.
func TestWin32PressTargetTakesTheFirstOfDuplicateLabels(t *testing.T) {
	dialog := win32ModalWindow("#32770", "Title",
		win32Control{ClassName: "Button", Text: "&OK", ID: 5, Visible: true, Handle: 0x6005},
		win32Control{ClassName: "Button", Text: "O&K", ID: 4, Visible: true, Handle: 0x6004})
	dialog.Handle = 0x6000

	got := win32PressTarget([]win32Window{dialog}, "OK")

	if !got.ButtonFound || got.ButtonHandle != 0x6004 || got.ButtonID != 4 {
		t.Fatalf("target = %#v, want the button with control ID 4", got)
	}
}

// Verifies a button the dialog has disabled is found but marked as one that must not be pressed,
// while its label is still listed and an enabled button of the same dialog is not marked.
func TestWin32PressTargetMarksADisabledButton(t *testing.T) {
	windows := win32ProbeDialogWindows()
	controls := windows[0].Controls
	for index := range controls {
		if controls[index].Handle == win32ProbeLaterButtonHandle {
			controls[index].Style |= win32StyleDisabled
		}
	}

	disabled := win32PressTarget(windows, "Later")
	enabled := win32PressTarget(windows, "Yes")

	if !disabled.ButtonFound || !disabled.ButtonDisabled || disabled.ButtonHandle != win32ProbeLaterButtonHandle {
		t.Fatalf("target = %#v, want the Later button marked disabled", disabled)
	}
	if !reflect.DeepEqual(disabled.Dialog.Buttons, []string{"Yes", "Later", "No"}) {
		t.Fatalf("buttons = %#v, want the three of the dialog", disabled.Dialog.Buttons)
	}
	if !enabled.ButtonFound || enabled.ButtonDisabled {
		t.Fatalf("target = %#v, want the Yes button not marked disabled", enabled)
	}
}
