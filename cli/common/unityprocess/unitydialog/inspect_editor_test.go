package unitydialog

import (
	"context"
	"errors"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// Verifies that on Windows a process ID the Editor reported is the one whose windows are listed,
// and the Unity process is not looked up.
func TestInspectEditorOnWindowsUsesTheReportedProcessIDWithoutLookingUpTheProcess(t *testing.T) {
	listedPID := 0
	listWindows := func(_ context.Context, pid int) ([]win32Window, error) {
		listedPID = pid
		return []win32Window{win32ModalWindow("#32770", "Probe title", win32ComplexDialogControls("Body")...)}, nil
	}
	inspector := win32DialogInspectorForTest(t, failingProcessLookup(t), listWindows)

	dialogs, err := inspector.InspectEditor(context.Background(), "/project", 4242)

	if err != nil || len(dialogs) != 1 {
		t.Fatalf("dialogs = %#v, %v; want one dialog and no error", dialogs, err)
	}
	if listedPID != 4242 {
		t.Fatalf("listed process ID = %d, want 4242", listedPID)
	}
}

// Verifies that on Windows an Editor that did not report its process ID is looked up as before,
// and the windows of the process found are listed.
func TestInspectEditorOnWindowsLooksUpTheProcessWhenNoProcessIDIsReported(t *testing.T) {
	lookups := 0
	lookup := func(context.Context, string) (*unityprocess.UnityProcess, error) {
		lookups++
		return &unityprocess.UnityProcess{Pid: 5151}, nil
	}
	listedPID := 0
	listWindows := func(_ context.Context, pid int) ([]win32Window, error) {
		listedPID = pid
		return []win32Window{win32ModalWindow("#32770", "Probe title", win32ComplexDialogControls("Body")...)}, nil
	}
	inspector := win32DialogInspectorForTest(t, lookup, listWindows)

	dialogs, err := inspector.InspectEditor(context.Background(), "/project", 0)

	if err != nil || len(dialogs) != 1 {
		t.Fatalf("dialogs = %#v, %v; want one dialog and no error", dialogs, err)
	}
	if lookups != 1 || listedPID != 5151 {
		t.Fatalf("lookups = %d, listed process ID = %d; want one lookup and 5151", lookups, listedPID)
	}
}

// Verifies that on macOS a reported process ID is passed to the accessibility script without
// looking up the Unity process.
func TestInspectEditorOnMacOSUsesTheReportedProcessID(t *testing.T) {
	scriptPID := ""
	command := func(_ context.Context, _ string, args ...string) ([]byte, error) {
		scriptPID = args[len(args)-1]
		return []byte(`{"processFound":true,"dialogs":[]}`), nil
	}
	inspector := dialogInspectorForTest(t, "darwin", failingProcessLookup(t), command)

	_, err := inspector.InspectEditor(context.Background(), "/project", 4242)

	if err != nil || scriptPID != "4242" {
		t.Fatalf("script process ID = %q, %v; want 4242 and no error", scriptPID, err)
	}
}

// Verifies that a reported process ID does not make the inspector look on an operating system
// where it cannot.
func TestInspectEditorIsUnsupportedOutsideMacOSAndWindowsEvenWithAProcessID(t *testing.T) {
	inspector := dialogInspectorForTest(t, "linux", failingProcessLookup(t), failingCommand(t))

	_, err := inspector.InspectEditor(context.Background(), "/project", 4242)

	if !errors.Is(err, ErrInspectionUnsupported) {
		t.Fatalf("error = %v, want ErrInspectionUnsupported", err)
	}
}
