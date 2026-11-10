package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"reflect"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
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

type dialogRun struct {
	code   int
	stdout string
	stderr string
}

func runDialogForTest(ctx context.Context, args []string, fake *fakeDialogCommand) dialogRun {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	connection := unityipc.Connection{ProjectRoot: dialogTestProjectRoot}
	code := runDialogCommandWithDeps(ctx, connection, args, &stdout, &stderr, fake.deps())
	return dialogRun{code: code, stdout: stdout.String(), stderr: stderr.String()}
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
	if got.Command != "dialog" || got.ProjectRoot != dialogTestProjectRoot {
		t.Fatalf("error must name the command and project: Command=%q ProjectRoot=%q", got.Command, got.ProjectRoot)
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

// Verifies uloop dialog without --answer prints every open dialog in the order the look found
// them and never presses anything.
func TestRunDialogCommandListsOpenDialogs(t *testing.T) {
	fake := &fakeDialogCommand{
		canPress: true,
		looks:    []dialogLookAnswer{{dialogs: []statusDialog{dialogTestPressed, dialogTestFollowing}}},
	}

	output := decodeDialogOutput(t, runDialogForTest(context.Background(), nil, fake))

	if output["Success"] != true {
		t.Fatalf("Success must be true: %#v", output)
	}
	assertDialogJSON(t, output["Dialogs"], []statusDialog{dialogTestPressed, dialogTestFollowing})
	if fake.pressCalls != 0 {
		t.Fatalf("listing must not press: press ran %d times", fake.pressCalls)
	}
}

// Verifies uloop dialog prints an empty Dialogs array, not null, when no dialog is open.
func TestRunDialogCommandListsNoDialogs(t *testing.T) {
	fake := &fakeDialogCommand{canPress: true, looks: []dialogLookAnswer{{dialogs: nil}}}

	run := runDialogForTest(context.Background(), nil, fake)
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

	got := decodeDialogError(t, runDialogForTest(context.Background(), nil, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeDialogCheckFailed, retryable: true, safeToRetry: true})

	if !strings.Contains(got.Message, "osascript could not read Unity's windows") {
		t.Fatalf("Message must carry the cause: %q", got.Message)
	}
}

// Verifies listing where uloop cannot look at Unity's windows is DIALOG_NOT_ANSWERABLE.
func TestRunDialogCommandIsUnsupportedWhereUloopCannotLook(t *testing.T) {
	fake := &fakeDialogCommand{canPress: false, looks: []dialogLookAnswer{{err: errDialogInspectionUnsupported}}}

	assertDialogFailure(t, runDialogForTest(context.Background(), nil, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeDialogNotAnswerable, retryable: false, safeToRetry: true})
}

// Verifies --answer presses the named button once and prints the dialog read before the press.
func TestRunDialogCommandPressesTheNamedButton(t *testing.T) {
	for _, args := range [][]string{{"--answer", "Later"}, {"--answer=Later"}} {
		t.Run(strings.Join(args, " "), func(t *testing.T) {
			fake := pressedFake()

			output := decodeDialogOutput(t, runDialogForTest(context.Background(), args, fake))

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

			got := decodeDialogError(t, runDialogForTest(context.Background(), []string{"--answer", label}, fake),
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

// Verifies --answer with no modal dialog open is DIALOG_NOT_OPEN and nothing is rechecked.
func TestRunDialogCommandFailsWhenNoDialogIsOpen(t *testing.T) {
	fake := &fakeDialogCommand{canPress: true, pressResult: dialogPressResult{DialogFound: false}}

	assertDialogFailure(t, runDialogForTest(context.Background(), []string{"--answer", "Later"}, fake),
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

	got := decodeDialogError(t, runDialogForTest(context.Background(), []string{"--answer", "OK"}, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeDialogNotAnswerable, retryable: false, safeToRetry: true})

	if !strings.Contains(got.Message, "Probe modal") {
		t.Fatalf("Message must name the dialog: %q", got.Message)
	}
	if fake.lookCalls != 0 {
		t.Fatalf("look must not run: %d", fake.lookCalls)
	}
}

// Verifies --answer where uloop cannot press buttons fails before looking or pressing.
func TestRunDialogCommandCannotPressOnThisSystem(t *testing.T) {
	fake := pressedFake()
	fake.canPress = false

	assertDialogFailure(t, runDialogForTest(context.Background(), []string{"--answer", "Later"}, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeDialogNotAnswerable, retryable: false, safeToRetry: true})

	if fake.pressCalls != 0 || fake.lookCalls != 0 {
		t.Fatalf("press and look must not run: press=%d look=%d", fake.pressCalls, fake.lookCalls)
	}
}

// Verifies a failed press is DIALOG_ANSWER_FAILED with its cause and is not safe to retry, since
// the press may have landed.
func TestRunDialogCommandReportsFailedPress(t *testing.T) {
	fake := &fakeDialogCommand{canPress: true, pressErr: errors.New("osascript did not answer within 5s")}

	got := decodeDialogError(t, runDialogForTest(context.Background(), []string{"--answer", "Later"}, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeDialogAnswerFailed, retryable: false, safeToRetry: false})

	if !strings.Contains(got.Message, "osascript did not answer within 5s") {
		t.Fatalf("Message must carry the cause: %q", got.Message)
	}
	if fake.lookCalls != 0 {
		t.Fatalf("look must not run: %d", fake.lookCalls)
	}
}

// Verifies a pressed dialog that is still open after every recheck is DIALOG_ANSWER_FAILED.
func TestRunDialogCommandFailsWhenTheDialogStaysOpen(t *testing.T) {
	fake := pressedFake()
	fake.looks = []dialogLookAnswer{{dialogs: []statusDialog{dialogTestPressed}}}

	assertDialogFailure(t, runDialogForTest(context.Background(), []string{"--answer", "Later"}, fake),
		expectedDialogFailure{code: clierrors.ErrorCodeDialogAnswerFailed, retryable: false, safeToRetry: false})

	if fake.lookCalls != dialogTestRecheckCount {
		t.Fatalf("look ran %d times, want %d", fake.lookCalls, dialogTestRecheckCount)
	}
}

// Verifies the command keeps rechecking until the pressed dialog is gone and then succeeds.
func TestRunDialogCommandWaitsForTheDialogToClose(t *testing.T) {
	fake := pressedFake()
	fake.looks = []dialogLookAnswer{{dialogs: []statusDialog{dialogTestPressed}}, {dialogs: nil}}

	output := decodeDialogOutput(t, runDialogForTest(context.Background(), []string{"--answer", "Later"}, fake))

	assertPressedDialogOutput(t, output)
	if fake.lookCalls != 2 {
		t.Fatalf("look ran %d times, want 2", fake.lookCalls)
	}
}

// Verifies a different dialog that opens after the press is reported in NextDialogs.
func TestRunDialogCommandReportsTheDialogThatFollows(t *testing.T) {
	fake := pressedFake()
	fake.looks = []dialogLookAnswer{{dialogs: []statusDialog{dialogTestFollowing}}}

	output := decodeDialogOutput(t, runDialogForTest(context.Background(), []string{"--answer", "Later"}, fake))

	assertPressedDialogOutput(t, output)
	assertDialogJSON(t, output["NextDialogs"], []statusDialog{dialogTestFollowing})
}

// Verifies a recheck that fails still reports the press as done, without NextDialogs.
func TestRunDialogCommandSucceedsWhenTheRecheckFails(t *testing.T) {
	fake := pressedFake()
	fake.looks = []dialogLookAnswer{{err: errors.New("osascript could not read Unity's windows")}}

	output := decodeDialogOutput(t, runDialogForTest(context.Background(), []string{"--answer", "Later"}, fake))

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

	output := decodeDialogOutput(t, runDialogForTest(ctx, []string{"--answer", "Later"}, fake))

	assertPressedDialogOutput(t, output)
	if _, ok := output["NextDialogs"]; ok {
		t.Fatalf("NextDialogs must be left out when the recheck was cancelled: %#v", output)
	}
	if fake.lookCalls != 0 {
		t.Fatalf("look must not run after cancellation: %d", fake.lookCalls)
	}
}

// Verifies --answer without a non-empty value is INVALID_ARGUMENT and touches no dialog.
func TestRunDialogCommandRejectsAnswerWithoutValue(t *testing.T) {
	for _, args := range [][]string{{"--answer"}, {"--answer", ""}, {"--answer="}} {
		t.Run(strings.Join(args, " "), func(t *testing.T) {
			fake := pressedFake()

			assertDialogFailure(t, runDialogForTest(context.Background(), args, fake),
				expectedDialogFailure{code: clierrors.ErrorCodeInvalidArgument, retryable: false, safeToRetry: false})

			if fake.pressCalls != 0 || fake.lookCalls != 0 {
				t.Fatalf("press and look must not run: press=%d look=%d", fake.pressCalls, fake.lookCalls)
			}
		})
	}
}

// Verifies unknown options, extra positional arguments, and a repeated --answer are
// INVALID_ARGUMENT and touch no dialog.
func TestRunDialogCommandRejectsUnknownOptions(t *testing.T) {
	for _, args := range [][]string{
		{"--bogus"},
		{"extra"},
		{"--answer", "Later", "extra"},
		{"--answer", "A", "--answer", "B"},
	} {
		t.Run(strings.Join(args, " "), func(t *testing.T) {
			fake := pressedFake()

			assertDialogFailure(t, runDialogForTest(context.Background(), args, fake),
				expectedDialogFailure{code: clierrors.ErrorCodeInvalidArgument, retryable: false, safeToRetry: false})

			if fake.pressCalls != 0 || fake.lookCalls != 0 {
				t.Fatalf("press and look must not run: press=%d look=%d", fake.pressCalls, fake.lookCalls)
			}
		})
	}
}
