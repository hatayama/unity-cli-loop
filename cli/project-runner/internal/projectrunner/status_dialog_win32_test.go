package projectrunner

import (
	"context"
	"errors"
	"reflect"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// The controls of EditorUtility.DisplayDialogComplex("Probe title", ..., "Yes", "No", "Later") in
// the order a real Editor on Windows listed them: the icon, the message, then the buttons from
// left to right, which is not the order of their IDs.
func win32ComplexDialogControls(message string) []win32Control {
	return []win32Control{
		{ClassName: "Static", Text: "", ID: 1040, Style: 0x5000080E, Visible: true},
		{ClassName: "Edit", Text: message, ID: 1019, Style: 0x50000804, Visible: true},
		{ClassName: "Button", Text: "&Yes", ID: 1, Style: 0x50010001, Visible: true},
		{ClassName: "Button", Text: "&Later", ID: 3, Style: 0x50010000, Visible: true},
		{ClassName: "Button", Text: "&No", ID: 2, Style: 0x50010000, Visible: true},
	}
}

// A window that holds Unity's main thread: visible, enabled, and owned by a disabled window.
func win32ModalWindow(className string, title string, controls ...win32Control) win32Window {
	return win32Window{
		ClassName:    className,
		Title:        title,
		Visible:      true,
		Enabled:      true,
		HasOwner:     true,
		OwnerEnabled: false,
		Controls:     controls,
	}
}

// The Editor's main window as it is while a modal dialog is open: disabled, with no owner.
var win32DisabledMainWindow = win32Window{
	ClassName: "UnityContainerWndClass",
	Title:     "project - scene - Windows, Mac, Linux - Unity 2022.3.62f3 <DX11>",
	Visible:   true,
	Enabled:   false,
}

// The prompt a real Editor on Windows showed while it started with compile errors: a message box
// with no owner, because the main window does not exist yet.
func win32StartupMessageBox() win32Window {
	return win32Window{
		ClassName: "#32770",
		Title:     "Enter Safe Mode?",
		Visible:   true,
		Enabled:   true,
		Controls: []win32Control{
			{ClassName: "Static", Text: "", ID: 1040, Style: 0x5000080E, Visible: true},
			{ClassName: "Edit", Text: "The project has compilation errors.\r\n\r\nEnter Safe Mode?", ID: 1019, Style: 0x50000804, Visible: true},
			{ClassName: "Button", Text: "&Enter Safe Mode", ID: 1, Style: 0x50010001, Visible: true},
			{ClassName: "Button", Text: "&Ignore", ID: 3, Style: 0x50010000, Visible: true},
			{ClassName: "Button", Text: "&Quit", ID: 2, Style: 0x50010000, Visible: true},
		},
	}
}

// The window of EditorUtility.DisplayProgressBar as a real Editor on Windows listed it: a dialog
// with no owner whose buttons are hidden unless the progress can be cancelled.
func win32ProgressWindow(title string, cancelButtonVisible bool) win32Window {
	return win32Window{
		ClassName: "#32770",
		Title:     title,
		Visible:   true,
		Enabled:   true,
		Controls: []win32Control{
			{ClassName: "msctls_progress32", Text: "", ID: 1031, Style: 0x50000000, Visible: true},
			{ClassName: "Button", Text: "Cancel", ID: 2, Style: 0x50010000, Visible: cancelButtonVisible},
			{ClassName: "Button", Text: "Skip Transcoding", ID: 3, Style: 0x40010000, Visible: false},
			{ClassName: "Static", Text: "", ID: 1033, Style: 0x40028080, Visible: false},
			{ClassName: "Static", Text: "Working", ID: 1032, Style: 0x50028080, Visible: true},
		},
	}
}

// The Editor's main window while no dialog is open.
var win32EnabledMainWindow = win32Window{ClassName: "UnityContainerWndClass", Title: "main", Visible: true, Enabled: true}

// The input method windows every Unity process owns; they are never visible.
var win32InputMethodWindow = win32Window{ClassName: "IME", Title: "Default IME", HasOwner: true, OwnerEnabled: true}

// Verifies each modal window shape read from Unity's windows on Windows becomes a title, a
// message, and the push buttons in the order of their control IDs with the mnemonic marker removed.
func TestDialogsFromWin32WindowsReadsModalWindows(t *testing.T) {
	cases := []struct {
		name    string
		windows []win32Window
		want    []statusDialog
	}{
		{
			name: "message box with three buttons",
			windows: []win32Window{
				win32ModalWindow("#32770", "Probe title", win32ComplexDialogControls("Line one. Line two.")...),
				win32DisabledMainWindow,
				win32InputMethodWindow,
			},
			want: []statusDialog{{Title: "Probe title", Message: "Line one. Line two.", Buttons: []string{"Yes", "No", "Later"}}},
		},
		{
			name: "message line break arrives as CRLF",
			windows: []win32Window{
				win32ModalWindow("#32770", "Probe title", win32ComplexDialogControls("Line one\r\nLine two")...),
			},
			want: []statusDialog{{Title: "Probe title", Message: "Line one\nLine two", Buttons: []string{"Yes", "No", "Later"}}},
		},
		{
			name: "modal Editor window with only a title",
			windows: []win32Window{
				win32ModalWindow("UnityContainerWndClass", "Probe modal",
					win32Control{ClassName: "UnityGUIViewWndClass", Text: "UnityEditor.EditorWindow", Visible: true}),
				win32DisabledMainWindow,
			},
			want: []statusDialog{{Title: "Probe modal", Message: "", Buttons: []string{}}},
		},
		{
			name: "message box without an owner while the Editor starts",
			windows: []win32Window{
				win32StartupMessageBox(),
				{ClassName: "UnitySplashWindow", Title: "Compiling Scripts", Visible: false, Enabled: true},
				win32InputMethodWindow,
			},
			want: []statusDialog{{
				Title:   "Enter Safe Mode?",
				Message: "The project has compilation errors.\n\nEnter Safe Mode?",
				Buttons: []string{"Enter Safe Mode", "Quit", "Ignore"},
			}},
		},
		{
			name: "two modal windows keep their order",
			windows: []win32Window{
				win32ModalWindow("#32770", "First",
					win32Control{ClassName: "Edit", Text: "First body", Visible: true},
					win32Control{ClassName: "Button", Text: "&OK", ID: 1, Visible: true}),
				win32ModalWindow("UnityContainerWndClass", "Second"),
				win32DisabledMainWindow,
			},
			want: []statusDialog{
				{Title: "First", Message: "First body", Buttons: []string{"OK"}},
				{Title: "Second", Message: "", Buttons: []string{}},
			},
		},
		{
			name: "empty, hidden, and non-push-button controls are dropped",
			windows: []win32Window{
				win32ModalWindow("#32770", "Title",
					win32Control{ClassName: "Static", Text: "", Visible: true},
					win32Control{ClassName: "Edit", Text: "Body", Visible: true},
					win32Control{ClassName: "Static", Text: "Hidden text", Visible: false},
					win32Control{ClassName: "Button", Text: "", ID: 1, Visible: true},
					win32Control{ClassName: "Button", Text: "&Hidden", ID: 2, Visible: false},
					win32Control{ClassName: "Button", Text: "&Remember my choice", ID: 4, Style: 0x50010003, Visible: true},
					win32Control{ClassName: "Button", Text: "&OK", ID: 3, Visible: true}),
			},
			want: []statusDialog{{Title: "Title", Message: "Body", Buttons: []string{"OK"}}},
		},
		{
			name: "static and edit texts are joined in order",
			windows: []win32Window{
				win32ModalWindow("#32770", "Native",
					win32Control{ClassName: "Static", Text: "Heading", Visible: true},
					win32Control{ClassName: "Edit", Text: "Detail", Visible: true},
					win32Control{ClassName: "Button", Text: "OK", ID: 1, Visible: true}),
			},
			want: []statusDialog{{Title: "Native", Message: "Heading\nDetail", Buttons: []string{"OK"}}},
		},
		{
			name: "class names compare without case",
			windows: []win32Window{
				win32ModalWindow("#32770", "Title",
					win32Control{ClassName: "EDIT", Text: "Body", Visible: true},
					win32Control{ClassName: "button", Text: "&OK", ID: 1, Visible: true}),
			},
			want: []statusDialog{{Title: "Title", Message: "Body", Buttons: []string{"OK"}}},
		},
		{
			name: "mnemonic marker inside a label and a literal ampersand",
			windows: []win32Window{
				win32ModalWindow("#32770", "Title",
					win32Control{ClassName: "Button", Text: "&Save & Quit", ID: 1, Visible: true},
					win32Control{ClassName: "Button", Text: "O&K - Do not show me this message again for this session.", ID: 3, Visible: true},
					win32Control{ClassName: "Button", Text: "Tom & Jerry", ID: 2, Visible: true}),
			},
			want: []statusDialog{{Title: "Title", Message: "", Buttons: []string{
				"Save & Quit", "Tom & Jerry", "OK - Do not show me this message again for this session.",
			}}},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got := dialogsFromWin32Windows(testCase.windows)
			if !reflect.DeepEqual(got, testCase.want) {
				t.Fatalf("dialogs = %#v, want %#v", got, testCase.want)
			}
		})
	}
}

// Verifies windows that do not hold Unity's main thread are not counted as dialogs, and the
// result is an empty, non-nil list: a utility window beside an enabled main window, the same
// utility window while a dialog has disabled it, the main window, a hidden window, and the windows
// that share a message box's class and lack of an owner without being one (a progress window, with
// or without a visible Cancel button, and a dialog with no push button).
func TestDialogsFromWin32WindowsIgnoresWindowsThatAreNotModal(t *testing.T) {
	utilityControl := win32Control{ClassName: "UnityGUIViewWndClass", Text: "UnityEditor.EditorWindow", Visible: true}
	cases := []struct {
		name    string
		windows []win32Window
	}{
		{name: "no window at all", windows: nil},
		{
			name: "utility window whose owner is enabled",
			windows: []win32Window{
				{
					ClassName: "UnityContainerWndClass", Title: "Probe utility", Visible: true, Enabled: true,
					HasOwner: true, OwnerEnabled: true, Controls: []win32Control{utilityControl},
				},
				{ClassName: "UnityContainerWndClass", Title: "main", Visible: true, Enabled: true},
				win32InputMethodWindow,
			},
		},
		{
			name: "utility window disabled by a dialog",
			windows: []win32Window{
				{
					ClassName: "UnityContainerWndClass", Title: "Probe utility", Visible: true, Enabled: false,
					HasOwner: true, OwnerEnabled: false,
				},
				win32DisabledMainWindow,
			},
		},
		{
			name: "progress window without an owner",
			windows: []win32Window{
				win32ProgressWindow("Reloading Domain (busy for 10s)...", false),
				win32EnabledMainWindow,
			},
		},
		{
			name: "progress window with a visible Cancel button",
			windows: []win32Window{
				win32ProgressWindow("Importing", true),
				win32EnabledMainWindow,
			},
		},
		{
			name:    "progress window while the Editor starts",
			windows: []win32Window{win32ProgressWindow("Importing", true)},
		},
		{
			name: "dialog without an owner and without a push button",
			windows: []win32Window{
				{
					ClassName: "#32770", Title: "Notice", Visible: true, Enabled: true,
					Controls: []win32Control{{ClassName: "Static", Text: "Working", Visible: true}},
				},
			},
		},
		{
			name: "disabled message box without an owner",
			windows: []win32Window{
				func() win32Window {
					window := win32StartupMessageBox()
					window.Enabled = false
					return window
				}(),
			},
		},
		{
			name: "window of another class without an owner that has a push button",
			windows: []win32Window{
				{
					ClassName: "UnityContainerWndClass", Title: "main", Visible: true, Enabled: true,
					Controls: []win32Control{{ClassName: "Button", Text: "&OK", ID: 1, Visible: true}},
				},
			},
		},
		{
			name: "hidden window owned by a disabled window",
			windows: []win32Window{
				{ClassName: "#32770", Title: "Hidden", Visible: false, Enabled: true, HasOwner: true, OwnerEnabled: false},
				win32DisabledMainWindow,
			},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got := dialogsFromWin32Windows(testCase.windows)
			if got == nil || len(got) != 0 {
				t.Fatalf("dialogs = %#v, want an empty non-nil slice", got)
			}
		})
	}
}

// Verifies a utility window that a dialog has disabled is left out while the dialog above it is
// reported, as a real Editor listed them with both open.
func TestDialogsFromWin32WindowsReportsOnlyTheDialogAboveADisabledUtilityWindow(t *testing.T) {
	windows := []win32Window{
		win32ModalWindow("UnityContainerWndClass", "Probe modal"),
		{
			ClassName: "UnityContainerWndClass", Title: "Probe utility", Visible: true, Enabled: false,
			HasOwner: true, OwnerEnabled: false,
		},
		win32DisabledMainWindow,
		win32InputMethodWindow,
	}

	got := dialogsFromWin32Windows(windows)

	want := []statusDialog{{Title: "Probe modal", Message: "", Buttons: []string{}}}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("dialogs = %#v, want %#v", got, want)
	}
}

// Verifies that on Windows a failed Unity process lookup is reported with its cause and no window
// is listed.
func TestDialogInspectorOnWindowsFailsWhenProcessLookupFails(t *testing.T) {
	lookup := func(context.Context, string) (*unityprocess.UnityProcess, error) {
		return nil, errors.New("process list refused")
	}
	inspector := win32DialogInspectorForTest(t, lookup, failingWindowList(t))

	_, err := inspector.inspect(context.Background(), "/project")

	if err == nil || !strings.Contains(err.Error(), "process list refused") {
		t.Fatalf("error = %v, want the lookup failure", err)
	}
}

// Verifies that on Windows the inspector reports that no Unity process runs, and lists no window.
func TestDialogInspectorOnWindowsFailsWhenUnityIsNotRunning(t *testing.T) {
	lookup := func(context.Context, string) (*unityprocess.UnityProcess, error) {
		return nil, nil
	}
	inspector := win32DialogInspectorForTest(t, lookup, failingWindowList(t))

	_, err := inspector.inspect(context.Background(), "/project")

	if err == nil || !strings.Contains(err.Error(), "no Unity process runs for this project") {
		t.Fatalf("error = %v, want one saying Unity is not running", err)
	}
}

// Verifies that on Windows a failed window listing is reported with its cause.
func TestDialogInspectorOnWindowsFailsWhenWindowListingFails(t *testing.T) {
	listWindows := func(context.Context, int) ([]win32Window, error) {
		return nil, errors.New("EnumWindows failed: access is denied")
	}
	inspector := win32DialogInspectorForTest(t, unityProcessWithPID(4242), listWindows)

	_, err := inspector.inspect(context.Background(), "/project")

	if err == nil || !strings.Contains(err.Error(), "EnumWindows failed: access is denied") {
		t.Fatalf("error = %v, want the listing failure", err)
	}
}

// Verifies that on Windows the inspector lists the windows of the Unity process ID and returns
// the modal ones as dialogs, without starting a command.
func TestDialogInspectorOnWindowsReadsDialogsFromWindows(t *testing.T) {
	var gotPID int
	listWindows := func(_ context.Context, pid int) ([]win32Window, error) {
		gotPID = pid
		return []win32Window{
			win32ModalWindow("#32770", "Probe title", win32ComplexDialogControls("Line one. Line two.")...),
			win32DisabledMainWindow,
		}, nil
	}
	inspector := win32DialogInspectorForTest(t, unityProcessWithPID(4242), listWindows)

	dialogs, err := inspector.inspect(context.Background(), "/project")
	if err != nil {
		t.Fatalf("inspect returned error: %v", err)
	}
	if gotPID != 4242 {
		t.Fatalf("listed process ID = %d, want 4242", gotPID)
	}
	want := []statusDialog{{Title: "Probe title", Message: "Line one. Line two.", Buttons: []string{"Yes", "No", "Later"}}}
	if !reflect.DeepEqual(dialogs, want) {
		t.Fatalf("dialogs = %#v, want %#v", dialogs, want)
	}
}

// Verifies that on Windows an Editor with no modal window gives an empty, non-nil list and no error.
func TestDialogInspectorOnWindowsReturnsNoDialogs(t *testing.T) {
	listWindows := func(context.Context, int) ([]win32Window, error) {
		return []win32Window{{ClassName: "UnityContainerWndClass", Title: "main", Visible: true, Enabled: true}}, nil
	}
	inspector := win32DialogInspectorForTest(t, unityProcessWithPID(4242), listWindows)

	dialogs, err := inspector.inspect(context.Background(), "/project")

	if err != nil || dialogs == nil || len(dialogs) != 0 {
		t.Fatalf("dialogs = %#v, %v; want an empty non-nil slice and no error", dialogs, err)
	}
}

// Verifies that on Windows a window listing that does not finish in time is reported as a
// timeout, not as the error the stopped listing returned.
func TestDialogInspectorOnWindowsFailsWhenWindowListingTimesOut(t *testing.T) {
	listWindows := func(ctx context.Context, _ int) ([]win32Window, error) {
		<-ctx.Done()
		return nil, ctx.Err()
	}
	inspector := win32DialogInspectorForTest(t, unityProcessWithPID(4242), listWindows)
	inspector.timeout = 50 * time.Millisecond

	_, err := inspector.inspect(context.Background(), "/project")

	if err == nil || !strings.Contains(err.Error(), "reading Unity's windows did not finish within 50ms") {
		t.Fatalf("error = %v, want a timeout error", err)
	}
}

// Verifies that on Windows a look stopped because the caller cancelled is reported as cancelled,
// not as a timeout.
func TestDialogInspectorOnWindowsReportsCancellation(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	listWindows := func(listCtx context.Context, _ int) ([]win32Window, error) {
		cancel()
		return nil, listCtx.Err()
	}
	inspector := win32DialogInspectorForTest(t, unityProcessWithPID(4242), listWindows)

	_, err := inspector.inspect(ctx, "/project")

	if !errors.Is(err, context.Canceled) || strings.Contains(err.Error(), "did not finish") {
		t.Fatalf("error = %v, want a cancellation", err)
	}
}

// Verifies the hint about a failed look explains Accessibility access only on macOS; elsewhere it
// only asks to look at Unity, because Windows needs no permission to read another window.
func TestDialogInspectorCheckFailedNextActionDiffersByOperatingSystem(t *testing.T) {
	const askTheUser = "ask the user to look at Unity"

	macOS := dialogInspector{goos: "darwin"}.checkFailedNextAction()
	if !strings.Contains(macOS, askTheUser) || !strings.Contains(macOS, "Accessibility access") {
		t.Fatalf("macOS hint = %q, want it to ask the user and explain Accessibility access", macOS)
	}

	windows := dialogInspector{goos: "windows"}.checkFailedNextAction()
	if !strings.Contains(windows, askTheUser) || strings.Contains(windows, "Accessibility") || strings.Contains(windows, "macOS") {
		t.Fatalf("Windows hint = %q, want it to ask the user without the macOS permission", windows)
	}
}
