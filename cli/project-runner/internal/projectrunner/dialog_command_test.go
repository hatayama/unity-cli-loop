package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"reflect"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/unityprocess/unitydialog"
)

const dialogTestProjectRoot = "/project"

// dialogTestRecheckCount is small so a dialog that never closes fails the test quickly.
const dialogTestRecheckCount = 3

var dialogTestPressed = statusDialog{
	Title:   "Probe title",
	Message: "Line one. Line two.",
	Buttons: []string{"Yes", "No", "Later"},
}

var dialogTestFollowing = statusDialog{
	Title:   "Second probe",
	Message: "Shown after the first one.",
	Buttons: []string{"OK"},
}

type dialogLookAnswer struct {
	dialogs []statusDialog
	err     error
}

// fakeDialogCommand records how often look and press ran and what press was given. looks are
// answered in order; the last answer repeats.
type fakeDialogCommand struct {
	canPress      bool
	looks         []dialogLookAnswer
	pressResult   dialogPressResult
	pressErr      error
	afterPress    func()
	recheckDelay  time.Duration
	lookCalls     int
	pressCalls    int
	pressedLabels []string
}

func (fake *fakeDialogCommand) deps() dialogCommandDeps {
	return dialogCommandDeps{
		canPress: fake.canPress,
		look: func(ctx context.Context, projectRoot string) ([]statusDialog, error) {
			fake.lookCalls++
			if len(fake.looks) == 0 {
				return nil, nil
			}
			index := fake.lookCalls - 1
			if index >= len(fake.looks) {
				index = len(fake.looks) - 1
			}
			return fake.looks[index].dialogs, fake.looks[index].err
		},
		press: func(ctx context.Context, projectRoot string, label string) (dialogPressResult, error) {
			fake.pressCalls++
			fake.pressedLabels = append(fake.pressedLabels, label)
			if fake.afterPress != nil {
				fake.afterPress()
			}
			return fake.pressResult, fake.pressErr
		},
		recheckDelay: fake.recheckDelay,
		recheckCount: dialogTestRecheckCount,
	}
}

// dialogRun is one command run; command is the name its error envelope must carry.
type dialogRun struct {
	command string
	code    int
	stdout  string
	stderr  string
}

type dialogCommandEntry func(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
	deps dialogCommandDeps,
) int

func runDialogEntryForTest(
	ctx context.Context,
	command string,
	entry dialogCommandEntry,
	args []string,
	fake *fakeDialogCommand,
) dialogRun {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	connection := unityipc.Connection{ProjectRoot: dialogTestProjectRoot}
	code := entry(ctx, connection, args, &stdout, &stderr, fake.deps())
	return dialogRun{command: command, code: code, stdout: stdout.String(), stderr: stderr.String()}
}

func runGetDialogsForTest(ctx context.Context, args []string, fake *fakeDialogCommand) dialogRun {
	return runDialogEntryForTest(ctx, "get-dialogs", runGetDialogsCommandWithDeps, args, fake)
}

func runPressDialogButtonForTest(ctx context.Context, args []string, fake *fakeDialogCommand) dialogRun {
	return runDialogEntryForTest(ctx, "press-dialog-button", runPressDialogButtonCommandWithDeps, args, fake)
}

func pressedFake() *fakeDialogCommand {
	return &fakeDialogCommand{
		canPress:    true,
		pressResult: dialogPressResult{DialogFound: true, Pressed: true, Dialog: dialogTestPressed},
	}
}

func decodeDialogOutput(t *testing.T, run dialogRun) map[string]any {
	t.Helper()
	if run.code != 0 {
		t.Fatalf("exit code = %d, want 0; stdout=%s stderr=%s", run.code, run.stdout, run.stderr)
	}
	if run.stderr != "" {
		t.Fatalf("stderr must stay empty on success: %s", run.stderr)
	}
	var output map[string]any
	if err := json.Unmarshal([]byte(run.stdout), &output); err != nil {
		t.Fatalf("stdout is not JSON: %v\n%s", err, run.stdout)
	}
	return output
}

// expectedDialogFailure is what an error row of the plan's table promises.
type expectedDialogFailure struct {
	code        string
	retryable   bool
	safeToRetry bool
}

func decodeDialogError(t *testing.T, run dialogRun, want expectedDialogFailure) clierrors.CLIError {
	t.Helper()
	if run.code != 1 {
		t.Fatalf("exit code = %d, want 1; stdout=%s stderr=%s", run.code, run.stdout, run.stderr)
	}
	if run.stdout != "" {
		t.Fatalf("stdout must stay empty on failure: %s", run.stdout)
	}
	var envelope clierrors.CLIErrorEnvelope
	if err := json.Unmarshal([]byte(run.stderr), &envelope); err != nil {
		t.Fatalf("stderr is not an error envelope: %v\n%s", err, run.stderr)
	}
	got := envelope.Error
	if got.ErrorCode != want.code || got.Retryable != want.retryable || got.SafeToRetry != want.safeToRetry {
		t.Fatalf("error = %s Retryable=%v SafeToRetry=%v, want %s Retryable=%v SafeToRetry=%v\n%s",
			got.ErrorCode, got.Retryable, got.SafeToRetry, want.code, want.retryable, want.safeToRetry, run.stderr)
	}
	if got.Command != run.command || got.ProjectRoot != dialogTestProjectRoot {
		t.Fatalf("error must name the command and project: Command=%q ProjectRoot=%q, want Command=%q",
			got.Command, got.ProjectRoot, run.command)
	}
	return got
}

// assertDialogFailure is decodeDialogError for tests that only check the error row.
func assertDialogFailure(t *testing.T, run dialogRun, want expectedDialogFailure) {
	t.Helper()
	_ = decodeDialogError(t, run, want)
}

func assertDialogJSON(t *testing.T, got any, want any) {
	t.Helper()
	wantJSON, err := json.Marshal(want)
	if err != nil {
		t.Fatal(err)
	}
	var normalized any
	if err := json.Unmarshal(wantJSON, &normalized); err != nil {
		t.Fatal(err)
	}
	if !reflect.DeepEqual(got, normalized) {
		t.Fatalf("got %#v, want %#v", got, normalized)
	}
}

func assertPressedDialogOutput(t *testing.T, output map[string]any) {
	t.Helper()
	if output["Success"] != true || output["Pressed"] != "Later" {
		t.Fatalf("output must say Later was pressed: %#v", output)
	}
	assertDialogJSON(t, output["Title"], dialogTestPressed.Title)
	assertDialogJSON(t, output["Message"], dialogTestPressed.Message)
	assertDialogJSON(t, output["Buttons"], dialogTestPressed.Buttons)
}

// Verifies uloop get-dialogs prints every open dialog in the order the look found
// them and never presses anything.
func TestRunDialogCommandListsOpenDialogs(t *testing.T) {
	fake := &fakeDialogCommand{
		canPress: true,
		looks:    []dialogLookAnswer{{dialogs: []statusDialog{dialogTestPressed, dialogTestFollowing}}},
	}

	output := decodeDialogOutput(t, runGetDialogsForTest(context.Background(), nil, fake))

	if output["Success"] != true {
		t.Fatalf("Success must be true: %#v", output)
	}
	assertDialogJSON(t, output["Dialogs"], []statusDialog{dialogTestPressed, dialogTestFollowing})
	if fake.pressCalls != 0 {
		t.Fatalf("listing must not press: press ran %d times", fake.pressCalls)
	}
}

// Verifies uloop get-dialogs prints an empty Dialogs array, not null, when no dialog is open.
func TestRunDialogCommandListsNoDialogs(t *testing.T) {
	fake := &fakeDialogCommand{canPress: true, looks: []dialogLookAnswer{{dialogs: nil}}}

	run := runGetDialogsForTest(context.Background(), nil, fake)
	output := decodeDialogOutput(t, run)

	dialogs, ok := output["Dialogs"].([]any)
	if !ok || len(dialogs) != 0 {
		t.Fatalf("Dialogs must be an empty array: %s", run.stdout)
	}
}

// Verifies a failed look while listing is reported as DIALOG_CHECK_FAILED with its cause.
func TestRunDialogCommandReportsFailedLook(t *testing.T) {
	fake := &fakeDialogCommand{
		canPress: true,
		looks:    []dialogLookAnswer{{err: errors.New("osascript could not read Unity's windows")}},
	}

	got := decodeDialogError(t, runGetDialogsForTest(context.Background(), nil, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeDialogCheckFailed, retryable: true, safeToRetry: true})

	if !strings.Contains(got.Message, "osascript could not read Unity's windows") {
		t.Fatalf("Message must carry the cause: %q", got.Message)
	}
}

// Verifies listing does not depend on pressing: where uloop can look but not press, the open
// dialogs are still printed.
func TestRunDialogCommandListsWhereUloopCannotPress(t *testing.T) {
	fake := &fakeDialogCommand{
		canPress: false,
		looks:    []dialogLookAnswer{{dialogs: []statusDialog{dialogTestPressed}}},
	}

	output := decodeDialogOutput(t, runGetDialogsForTest(context.Background(), nil, fake))

	assertDialogJSON(t, output["Dialogs"], []statusDialog{dialogTestPressed})
}

// Verifies listing where uloop cannot look at Unity's windows is DIALOG_NOT_ANSWERABLE.
func TestRunDialogCommandIsUnsupportedWhereUloopCannotLook(t *testing.T) {
	fake := &fakeDialogCommand{canPress: false, looks: []dialogLookAnswer{{err: unitydialog.ErrInspectionUnsupported}}}

	assertDialogFailure(t, runGetDialogsForTest(context.Background(), nil, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeDialogNotAnswerable, retryable: false, safeToRetry: true})
}

// Verifies --label presses the named button once and prints the dialog read before the press.
func TestRunDialogCommandPressesTheNamedButton(t *testing.T) {
	for _, args := range [][]string{{"--label", "Later"}, {"--label=Later"}} {
		t.Run(strings.Join(args, " "), func(t *testing.T) {
			fake := pressedFake()

			output := decodeDialogOutput(t, runPressDialogButtonForTest(context.Background(), args, fake))

			assertPressedDialogOutput(t, output)
			if _, ok := output["NextDialogs"]; ok {
				t.Fatalf("NextDialogs must be left out when no dialog follows: %#v", output)
			}
			if fake.pressCalls != 1 || fake.pressedLabels[0] != "Later" {
				t.Fatalf("press must run once with Later: calls=%d labels=%v", fake.pressCalls, fake.pressedLabels)
			}
		})
	}
}

// Verifies a label the dialog lacks fails with the dialog's buttons, the label reaches press as
// typed (no case folding), and nothing waits for the dialog to close.
func TestRunDialogCommandRejectsAnUnknownButton(t *testing.T) {
	for _, label := range []string{"Nope", "later"} {
		t.Run(label, func(t *testing.T) {
			fake := &fakeDialogCommand{
				canPress:    true,
				pressResult: dialogPressResult{DialogFound: true, Pressed: false, Dialog: dialogTestPressed},
			}

			got := decodeDialogError(t, runPressDialogButtonForTest(context.Background(), []string{"--label", label}, fake),
				expectedDialogFailure{code: clierrors.ErrorCodeDialogButtonNotFound, retryable: false, safeToRetry: true})

			assertDialogJSON(t, got.Details["Buttons"], []string{"Yes", "No", "Later"})
			if len(fake.pressedLabels) != 1 || fake.pressedLabels[0] != label {
				t.Fatalf("press must get the label as typed: %v", fake.pressedLabels)
			}
			if fake.lookCalls != 0 {
				t.Fatalf("an unpressed dialog must not be rechecked: look ran %d times", fake.lookCalls)
			}
		})
	}
}

// Verifies --label with no modal dialog open is DIALOG_NOT_OPEN and nothing is rechecked.
func TestRunDialogCommandFailsWhenNoDialogIsOpen(t *testing.T) {
	fake := &fakeDialogCommand{canPress: true, pressResult: dialogPressResult{DialogFound: false}}

	assertDialogFailure(t, runPressDialogButtonForTest(context.Background(), []string{"--label", "Later"}, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeDialogNotOpen, retryable: false, safeToRetry: true})

	if fake.lookCalls != 0 {
		t.Fatalf("look must not run: %d", fake.lookCalls)
	}
}

// Verifies a dialog whose buttons have no readable names, such as a modal Editor window, is
// DIALOG_NOT_ANSWERABLE and its title is named in the message.
func TestRunDialogCommandCannotAnswerADialogWithoutButtons(t *testing.T) {
	fake := &fakeDialogCommand{
		canPress: true,
		pressResult: dialogPressResult{
			DialogFound: true,
			Pressed:     false,
			Dialog:      statusDialog{Title: "Probe modal", Buttons: []string{}},
		},
	}

	got := decodeDialogError(t, runPressDialogButtonForTest(context.Background(), []string{"--label", "OK"}, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeDialogNotAnswerable, retryable: false, safeToRetry: true})

	if !strings.Contains(got.Message, "Probe modal") {
		t.Fatalf("Message must name the dialog: %q", got.Message)
	}
	if fake.lookCalls != 0 {
		t.Fatalf("look must not run: %d", fake.lookCalls)
	}
}

// Verifies --label where uloop cannot press buttons fails before looking or pressing, and names
// the operating systems where it can.
func TestRunDialogCommandCannotPressOnThisSystem(t *testing.T) {
	fake := pressedFake()
	fake.canPress = false

	got := decodeDialogError(t, runPressDialogButtonForTest(context.Background(), []string{"--label", "Later"}, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeDialogNotAnswerable, retryable: false, safeToRetry: true})

	if got.Message != "uloop can press a dialog button only on macOS and Windows." {
		t.Fatalf("Message = %q, want it to name macOS and Windows", got.Message)
	}

	if fake.pressCalls != 0 || fake.lookCalls != 0 {
		t.Fatalf("press and look must not run: press=%d look=%d", fake.pressCalls, fake.lookCalls)
	}
}

// Verifies a failed press is DIALOG_ANSWER_FAILED with its cause and is not safe to retry, since
// the press may have landed.
func TestRunDialogCommandReportsFailedPress(t *testing.T) {
	fake := &fakeDialogCommand{canPress: true, pressErr: errors.New("osascript did not answer within 5s")}

	got := decodeDialogError(t, runPressDialogButtonForTest(context.Background(), []string{"--label", "Later"}, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeDialogAnswerFailed, retryable: false, safeToRetry: false})

	if !strings.Contains(got.Message, "osascript did not answer within 5s") {
		t.Fatalf("Message must carry the cause: %q", got.Message)
	}
	if fake.lookCalls != 0 {
		t.Fatalf("look must not run: %d", fake.lookCalls)
	}
}

// Verifies a press that failed before osascript started, so no click can have happened, is
// DIALOG_ANSWER_FAILED that is safe to retry.
func TestRunDialogCommandReportsAPressThatNeverStarted(t *testing.T) {
	fake := &fakeDialogCommand{
		canPress: true,
		pressErr: unitydialog.PressNotStartedError{Err: errors.New("no Unity process runs for this project")},
	}

	got := decodeDialogError(t, runPressDialogButtonForTest(context.Background(), []string{"--label", "Later"}, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeDialogAnswerFailed, retryable: false, safeToRetry: true})

	if !strings.Contains(got.Message, "no Unity process runs for this project") {
		t.Fatalf("Message must carry the cause: %q", got.Message)
	}
}

// Verifies a pressed dialog that is still open after every recheck is DIALOG_ANSWER_FAILED.
func TestRunDialogCommandFailsWhenTheDialogStaysOpen(t *testing.T) {
	fake := pressedFake()
	fake.looks = []dialogLookAnswer{{dialogs: []statusDialog{dialogTestPressed}}}

	assertDialogFailure(t, runPressDialogButtonForTest(context.Background(), []string{"--label", "Later"}, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeDialogAnswerFailed, retryable: false, safeToRetry: false})

	if fake.lookCalls != dialogTestRecheckCount {
		t.Fatalf("look ran %d times, want %d", fake.lookCalls, dialogTestRecheckCount)
	}
}

// Verifies the command keeps rechecking until the pressed dialog is gone and then succeeds.
func TestRunDialogCommandWaitsForTheDialogToClose(t *testing.T) {
	fake := pressedFake()
	fake.looks = []dialogLookAnswer{{dialogs: []statusDialog{dialogTestPressed}}, {dialogs: nil}}

	output := decodeDialogOutput(t, runPressDialogButtonForTest(context.Background(), []string{"--label", "Later"}, fake))

	assertPressedDialogOutput(t, output)
	if fake.lookCalls != 2 {
		t.Fatalf("look ran %d times, want 2", fake.lookCalls)
	}
}

// Verifies a different dialog that opens after the press is reported in NextDialogs.
func TestRunDialogCommandReportsTheDialogThatFollows(t *testing.T) {
	fake := pressedFake()
	fake.looks = []dialogLookAnswer{{dialogs: []statusDialog{dialogTestFollowing}}}

	output := decodeDialogOutput(t, runPressDialogButtonForTest(context.Background(), []string{"--label", "Later"}, fake))

	assertPressedDialogOutput(t, output)
	assertDialogJSON(t, output["NextDialogs"], []statusDialog{dialogTestFollowing})
}

// Verifies a recheck that fails still reports the press as done, without NextDialogs.
func TestRunDialogCommandSucceedsWhenTheRecheckFails(t *testing.T) {
	fake := pressedFake()
	fake.looks = []dialogLookAnswer{{err: errors.New("osascript could not read Unity's windows")}}

	output := decodeDialogOutput(t, runPressDialogButtonForTest(context.Background(), []string{"--label", "Later"}, fake))

	assertPressedDialogOutput(t, output)
	if _, ok := output["NextDialogs"]; ok {
		t.Fatalf("NextDialogs must be left out when the recheck failed: %#v", output)
	}
}

// Verifies a cancellation while waiting to recheck still reports the press as done, without
// looking again.
func TestRunDialogCommandSucceedsWhenCancelledAfterThePress(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	fake := pressedFake()
	fake.looks = []dialogLookAnswer{{dialogs: []statusDialog{dialogTestPressed}}}
	fake.recheckDelay = time.Hour
	fake.afterPress = cancel

	output := decodeDialogOutput(t, runPressDialogButtonForTest(ctx, []string{"--label", "Later"}, fake))

	assertPressedDialogOutput(t, output)
	if _, ok := output["NextDialogs"]; ok {
		t.Fatalf("NextDialogs must be left out when the recheck was cancelled: %#v", output)
	}
	if fake.lookCalls != 0 {
		t.Fatalf("look must not run after cancellation: %d", fake.lookCalls)
	}
}

// Verifies --label without a non-empty value is INVALID_ARGUMENT and touches no dialog.
func TestRunDialogCommandRejectsAnswerWithoutValue(t *testing.T) {
	for _, args := range [][]string{{"--label"}, {"--label", ""}, {"--label="}} {
		t.Run(strings.Join(args, " "), func(t *testing.T) {
			fake := pressedFake()

			got := decodeDialogError(t, runPressDialogButtonForTest(context.Background(), args, fake),
				expectedDialogFailure{code: clierrors.ErrorCodeInvalidArgument, retryable: false, safeToRetry: false})

			if got.Message != "--label requires a button label" {
				t.Fatalf("Message = %q", got.Message)
			}

			if fake.pressCalls != 0 || fake.lookCalls != 0 {
				t.Fatalf("press and look must not run: press=%d look=%d", fake.pressCalls, fake.lookCalls)
			}
		})
	}
}

// Verifies unknown options, extra positional arguments, the old --answer option, and a repeated
// --label are INVALID_ARGUMENT for press-dialog-button and touch no dialog.
func TestRunDialogCommandRejectsUnknownOptions(t *testing.T) {
	for _, testCase := range []struct {
		args        []string
		wantMessage string
	}{
		{args: []string{"--bogus"}, wantMessage: "press-dialog-button does not accept --bogus"},
		{args: []string{"extra"}, wantMessage: "press-dialog-button does not accept extra"},
		{args: []string{"--answer", "Later"}, wantMessage: "press-dialog-button does not accept --answer"},
		{args: []string{"--label", "Later", "extra"}, wantMessage: "press-dialog-button does not accept extra"},
		{args: []string{"--label", "A", "--label", "B"}, wantMessage: "--label can be given only once"},
	} {
		t.Run(strings.Join(testCase.args, " "), func(t *testing.T) {
			fake := pressedFake()

			got := decodeDialogError(t, runPressDialogButtonForTest(context.Background(), testCase.args, fake),
				expectedDialogFailure{code: clierrors.ErrorCodeInvalidArgument, retryable: false, safeToRetry: false})

			if got.Message != testCase.wantMessage {
				t.Fatalf("Message = %q, want %q", got.Message, testCase.wantMessage)
			}

			if fake.pressCalls != 0 || fake.lookCalls != 0 {
				t.Fatalf("press and look must not run: press=%d look=%d", fake.pressCalls, fake.lookCalls)
			}
		})
	}
}

// Verifies get-dialogs takes no options: any argument is INVALID_ARGUMENT naming it, and neither
// look nor press runs.
func TestRunGetDialogsCommandRejectsOptions(t *testing.T) {
	for _, args := range [][]string{{"--label", "Later"}, {"extra"}} {
		t.Run(strings.Join(args, " "), func(t *testing.T) {
			fake := pressedFake()

			got := decodeDialogError(t, runGetDialogsForTest(context.Background(), args, fake),
				expectedDialogFailure{code: clierrors.ErrorCodeInvalidArgument, retryable: false, safeToRetry: false})

			if got.Message != "get-dialogs takes no options: "+args[0] {
				t.Fatalf("Message = %q", got.Message)
			}
			if fake.pressCalls != 0 || fake.lookCalls != 0 {
				t.Fatalf("press and look must not run: press=%d look=%d", fake.pressCalls, fake.lookCalls)
			}
		})
	}
}

// Verifies press-dialog-button without --label is INVALID_ARGUMENT and neither looks nor presses,
// instead of falling back to listing the dialogs.
func TestRunPressDialogButtonCommandRequiresALabel(t *testing.T) {
	fake := pressedFake()

	got := decodeDialogError(t, runPressDialogButtonForTest(context.Background(), nil, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeInvalidArgument, retryable: false, safeToRetry: false})

	if !strings.Contains(got.Message, "requires --label") {
		t.Fatalf("Message = %q, want it to ask for --label", got.Message)
	}
	if fake.pressCalls != 0 || fake.lookCalls != 0 {
		t.Fatalf("press and look must not run: press=%d look=%d", fake.pressCalls, fake.lookCalls)
	}
}
