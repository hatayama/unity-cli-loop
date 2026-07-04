package ui

import (
	"fmt"
	"io"
	"os"
	"sync"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	spinnerFrameInterval = 80 * time.Millisecond
)

var spinnerFrames = []string{"⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"}

type TerminalSpinner struct {
	writer   io.Writer
	Enabled  bool
	message  string
	frame    int
	done     chan struct{}
	stopped  chan struct{}
	stopOnce sync.Once
	mutex    sync.Mutex
}

func NewToolSpinner(stderr io.Writer, showFeedback bool) *TerminalSpinner {
	return newSpinner(stderr, showFeedback && isTerminalWriter(stderr), "Connecting to Unity...")
}

func NewLaunchSpinner(stdout io.Writer, stderr io.Writer) *TerminalSpinner {
	if isTerminalWriter(stdout) {
		return newSpinner(stdout, true, "Waiting for Unity to finish starting...")
	}
	return newSpinner(stderr, isTerminalWriter(stderr), "Waiting for Unity to finish starting...")
}

// Maps connection-stage progress events to the contextual executing message
// and passes display-ready payloads (such as the heartbeat main-thread stall
// notice) through to the spinner verbatim. Without this mapping the stall
// diagnosis built by the IPC client would never reach the user.
func NewSpinnerProgressFunc(spinner *TerminalSpinner, executingMessage string) unityipc.ProgressFunc {
	return func(message string) {
		if message == unityipc.ProgressEventConnected || message == unityipc.ProgressEventAccepted {
			spinner.Update(executingMessage)
			return
		}
		spinner.Update(message)
	}
}

func newSpinner(writer io.Writer, enabled bool, message string) *TerminalSpinner {
	spinner := &TerminalSpinner{
		writer:  writer,
		Enabled: enabled,
		message: message,
		done:    make(chan struct{}),
		stopped: make(chan struct{}),
	}

	if !enabled {
		close(spinner.stopped)
		return spinner
	}

	spinner.render()
	go spinner.run()
	return spinner
}

func (spinner *TerminalSpinner) Update(message string) {
	if !spinner.Enabled {
		return
	}

	spinner.mutex.Lock()
	spinner.message = message
	spinner.mutex.Unlock()
	spinner.render()
}

func (spinner *TerminalSpinner) Stop() {
	if !spinner.Enabled {
		return
	}

	spinner.stopOnce.Do(func() {
		close(spinner.done)
		<-spinner.stopped
		spinner.mutex.Lock()
		defer spinner.mutex.Unlock()
		_, _ = fmt.Fprint(spinner.writer, "\r\x1b[K\n")
	})
}

func (spinner *TerminalSpinner) run() {
	ticker := time.NewTicker(spinnerFrameInterval)
	defer ticker.Stop()
	defer close(spinner.stopped)

	for {
		select {
		case <-ticker.C:
			spinner.render()
		case <-spinner.done:
			return
		}
	}
}

func (spinner *TerminalSpinner) render() {
	spinner.mutex.Lock()
	defer spinner.mutex.Unlock()

	frame := spinnerFrames[spinner.frame]
	spinner.frame = (spinner.frame + 1) % len(spinnerFrames)
	_, _ = fmt.Fprintf(spinner.writer, "\r\x1b[K%s %s", frame, spinner.message)
}

func isTerminalWriter(writer io.Writer) bool {
	file, ok := writer.(*os.File)
	if !ok {
		return false
	}

	info, err := file.Stat()
	if err != nil {
		return false
	}

	return info.Mode()&os.ModeCharDevice != 0
}
