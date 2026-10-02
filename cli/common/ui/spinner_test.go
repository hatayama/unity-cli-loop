package ui

import (
	"bytes"
	"io"
	"os"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/progress"
)

func TestSpinnerDoesNotWriteWhenDisabled(t *testing.T) {
	// Verifies that disabled spinners stay silent.
	var stderr bytes.Buffer

	spinner := newSpinner(&stderr, false, "Executing compile...")
	spinner.Stop()

	if stderr.Len() != 0 {
		t.Fatalf("disabled spinner wrote output: %q", stderr.String())
	}
}

func TestSpinnerWritesMessageAndClearsLine(t *testing.T) {
	// Verifies that enabled spinners render and clean up their terminal line.
	var stderr bytes.Buffer

	spinner := newSpinner(&stderr, true, "Executing compile...")
	spinner.Stop()

	output := stderr.String()
	if !strings.Contains(output, "Executing compile...") {
		t.Fatalf("spinner output did not include message: %q", output)
	}
	if !strings.HasSuffix(output, "\r\x1b[K\n") {
		t.Fatalf("spinner output did not clear the line: %q", output)
	}
}

func TestLaunchSpinnerWritesStartupMessage(t *testing.T) {
	// Verifies that launch spinners show startup progress text.
	var stdout bytes.Buffer

	spinner := newSpinner(&stdout, true, "Waiting for Unity to finish starting...")
	spinner.Stop()

	output := stdout.String()
	if !strings.Contains(output, "Waiting for Unity to finish starting...") {
		t.Fatalf("launch spinner output did not include message: %q", output)
	}
	if !strings.HasSuffix(output, "\r\x1b[K\n") {
		t.Fatalf("launch spinner output did not clear the line before returning: %q", output)
	}
}

func TestSpinnerProgressFuncShowsStallMessageVerbatim(t *testing.T) {
	// Verifies that heartbeat stall payloads reach the spinner verbatim.
	var stderr bytes.Buffer
	spinner := newSpinner(&stderr, true, "Connecting to Unity...")
	progressFunc := NewSpinnerProgressFunc(spinner, "Executing get-logs...")

	progressFunc(progress.Event{
		Stage:   progress.StageMessage,
		Message: "unity editor main thread busy for 38s...",
	})
	spinner.Stop()

	if !strings.Contains(stderr.String(), "unity editor main thread busy for 38s...") {
		t.Fatalf("spinner output did not include stall message: %q", stderr.String())
	}
}

func TestSpinnerProgressFuncMapsConnectionEventsToExecutingMessage(t *testing.T) {
	// Verifies that connection-stage events show the contextual executing message
	// instead of the raw event token.
	var stderr bytes.Buffer
	spinner := newSpinner(&stderr, true, "Connecting to Unity...")
	progressFunc := NewSpinnerProgressFunc(spinner, "Executing get-logs...")

	progressFunc(progress.Event{Stage: progress.StageConnected})
	progressFunc(progress.Event{Stage: progress.StageAccepted})
	spinner.Stop()

	output := stderr.String()
	if !strings.Contains(output, "Executing get-logs...") {
		t.Fatalf("spinner output did not include executing message: %q", output)
	}
	if strings.Contains(output, string(progress.StageConnected)) || strings.Contains(output, string(progress.StageAccepted)) {
		t.Fatalf("spinner output leaked a raw progress event token: %q", output)
	}
}

func TestSpinnerUpdateAfterStopWritesNothing(t *testing.T) {
	// Verifies Update after Stop does not render leftover TTY frames.
	var stderr bytes.Buffer

	spinner := newSpinner(&stderr, true, "Executing compile...")
	spinner.Stop()
	afterStop := stderr.Len()

	spinner.Update("Warming execute-dynamic-code after compile...")

	if stderr.Len() != afterStop {
		t.Fatalf("Update after Stop wrote output: %q", stderr.String()[afterStop:])
	}
	if strings.Contains(stderr.String(), "Warming execute-dynamic-code after compile...") {
		t.Fatalf("stopped spinner rendered an Update message: %q", stderr.String())
	}
}

func TestNewToolSpinnerRespectsFeedbackFlag(t *testing.T) {
	// Verifies callers can disable tool spinner feedback for hot paths.
	var stderr bytes.Buffer

	spinner := NewToolSpinner(&stderr, false)
	spinner.Stop()

	if stderr.Len() != 0 {
		t.Fatalf("disabled tool spinner wrote output: %q", stderr.String())
	}
}

// Opens a temporary regular file that is not a terminal and closes it at test end.
func openRegularFile(t *testing.T) *os.File {
	t.Helper()
	file, err := os.CreateTemp(t.TempDir(), "spinner-*.txt")
	if err != nil {
		t.Fatalf("failed to create temp file: %v", err)
	}
	t.Cleanup(func() { _ = file.Close() })
	return file
}

// Opens the null device, which reports itself as a character device like a terminal.
func openCharacterDevice(t *testing.T) *os.File {
	t.Helper()
	file, err := os.OpenFile(os.DevNull, os.O_WRONLY, 0)
	if err != nil {
		t.Fatalf("failed to open null device: %v", err)
	}
	t.Cleanup(func() { _ = file.Close() })
	return file
}

func TestIsTerminalWriterDetectsCharacterDevices(t *testing.T) {
	// Verifies only character-device files count as terminals; buffers, regular
	// files, and files that can no longer be stat'ed do not.
	closedFile := openRegularFile(t)
	if err := closedFile.Close(); err != nil {
		t.Fatalf("failed to close temp file: %v", err)
	}

	cases := []struct {
		name   string
		writer io.Writer
		want   bool
	}{
		{name: "buffer", writer: &bytes.Buffer{}, want: false},
		{name: "regular file", writer: openRegularFile(t), want: false},
		{name: "closed file", writer: closedFile, want: false},
		{name: "character device", writer: openCharacterDevice(t), want: true},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if got := isTerminalWriter(testCase.writer); got != testCase.want {
				t.Fatalf("expected %v, got %v", testCase.want, got)
			}
		})
	}
}

func TestNewLaunchSpinnerStaysSilentWithoutTerminal(t *testing.T) {
	// Verifies a launch spinner is disabled and writes nothing when neither stream is a terminal.
	stdout := openRegularFile(t)
	stderr := openRegularFile(t)

	spinner := NewLaunchSpinner(stdout, stderr)
	spinner.Update("Still waiting...")
	spinner.Stop()

	if spinner.Enabled {
		t.Fatalf("expected launch spinner to be disabled without a terminal")
	}
	for _, file := range []*os.File{stdout, stderr} {
		info, err := file.Stat()
		if err != nil {
			t.Fatalf("failed to stat output file: %v", err)
		}
		if info.Size() != 0 {
			t.Fatalf("disabled launch spinner wrote %d bytes to %s", info.Size(), file.Name())
		}
	}
}

func TestNewLaunchSpinnerPrefersTerminalStream(t *testing.T) {
	// Verifies a launch spinner renders on stdout when it is a terminal, and falls
	// back to stderr only when stdout is not.
	cases := []struct {
		name           string
		stdoutTerminal bool
		stderrTerminal bool
		wantStdout     bool
	}{
		{name: "stdout terminal", stdoutTerminal: true, stderrTerminal: true, wantStdout: true},
		{name: "stderr terminal only", stdoutTerminal: false, stderrTerminal: true, wantStdout: false},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			stdout := pickSpinnerStream(t, testCase.stdoutTerminal)
			stderr := pickSpinnerStream(t, testCase.stderrTerminal)

			spinner := NewLaunchSpinner(stdout, stderr)
			spinner.Stop()

			want := stderr
			if testCase.wantStdout {
				want = stdout
			}
			if !spinner.Enabled || spinner.writer != want {
				t.Fatalf("expected enabled spinner writing to %s, got enabled=%v writer=%v", want.Name(), spinner.Enabled, spinner.writer)
			}
		})
	}
}

// Returns a character-device stream when terminal is true, otherwise a regular file.
func pickSpinnerStream(t *testing.T, terminal bool) *os.File {
	t.Helper()
	if terminal {
		return openCharacterDevice(t)
	}
	return openRegularFile(t)
}

func TestDisabledSpinnerUpdateWritesNothing(t *testing.T) {
	// Verifies Update on a disabled spinner neither renders nor changes the stored message.
	var stderr bytes.Buffer
	spinner := newSpinner(&stderr, false, "Connecting to Unity...")

	spinner.Update("Executing compile...")

	if stderr.Len() != 0 {
		t.Fatalf("disabled spinner Update wrote output: %q", stderr.String())
	}
	if spinner.message != "Connecting to Unity..." {
		t.Fatalf("disabled spinner Update changed the message to %q", spinner.message)
	}
}

func TestSpinnerRenderAfterStopWritesNothing(t *testing.T) {
	// Verifies a frame rendered after Stop (as a late ticker tick would) leaves the cleared line intact.
	var stderr bytes.Buffer
	spinner := newSpinner(&stderr, true, "Executing compile...")
	spinner.Stop()
	afterStop := stderr.String()

	spinner.render()

	if stderr.String() != afterStop {
		t.Fatalf("render after Stop wrote output: %q", stderr.String()[len(afterStop):])
	}
}
