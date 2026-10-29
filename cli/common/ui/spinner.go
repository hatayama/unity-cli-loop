package ui

import (
	"fmt"
	"io"
	"os"
	"sync"
	"time"

	"github.com/hatayama/unity-cli-loop/common/progress"
)

const (
	spinnerFrameInterval = 80 * time.Millisecond
	// Without a spinner a notice becomes a line that piles up instead of being redrawn; at the
	// heartbeat cadence (10s) a 5-minute stall would print 30 lines, so it matches the 60-second
	// cadence of compile's interim reports.
	noticeLineInterval = 60 * time.Second
)

var spinnerFrames = []string{"⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"}

type TerminalSpinner struct {
	writer       io.Writer
	Enabled      bool
	message      string
	frame        int
	done         chan struct{}
	stopped      chan struct{}
	stopOnce     sync.Once
	mutex        sync.Mutex
	stoppedFlag  bool
	now          func() time.Time
	lastNoticeAt time.Time
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
// notice) through verbatim as notices, which are written as lines even when no
// spinner is shown. Without this mapping the stall diagnosis built by the IPC
// client would never reach the user.
func NewSpinnerProgressFunc(spinner *TerminalSpinner, executingMessage string) progress.Func {
	return func(event progress.Event) {
		if event.Stage == progress.StageConnected || event.Stage == progress.StageAccepted {
			spinner.Update(executingMessage)
			return
		}
		spinner.Notice(event.Message)
	}
}

func newSpinner(writer io.Writer, enabled bool, message string) *TerminalSpinner {
	spinner := &TerminalSpinner{
		writer:  writer,
		Enabled: enabled,
		message: message,
		done:    make(chan struct{}),
		stopped: make(chan struct{}),
		now:     time.Now,
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
	if spinner.stoppedFlag {
		spinner.mutex.Unlock()
		return
	}
	spinner.message = message
	spinner.mutex.Unlock()
	spinner.render()
}

// Notice shows a message that must reach the reader even when no spinner is
// shown, because agents read stderr through a pipe rather than a terminal. An
// enabled spinner shows it in place; otherwise it is written as a plain line, at
// most once per noticeLineInterval.
func (spinner *TerminalSpinner) Notice(message string) {
	if spinner.Enabled {
		spinner.Update(message)
		return
	}

	spinner.mutex.Lock()
	defer spinner.mutex.Unlock()
	if spinner.stoppedFlag {
		return
	}
	now := spinner.now()
	if !spinner.lastNoticeAt.IsZero() && now.Sub(spinner.lastNoticeAt) < noticeLineInterval {
		return
	}
	spinner.lastNoticeAt = now
	_, _ = fmt.Fprintln(spinner.writer, message)
}

func (spinner *TerminalSpinner) Stop() {
	if !spinner.Enabled {
		// Marked stopped so a notice that arrives after the command ends writes no line.
		spinner.mutex.Lock()
		spinner.stoppedFlag = true
		spinner.mutex.Unlock()
		return
	}

	spinner.stopOnce.Do(func() {
		close(spinner.done)
		<-spinner.stopped
		spinner.mutex.Lock()
		defer spinner.mutex.Unlock()
		spinner.stoppedFlag = true
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
	if spinner.stoppedFlag {
		return
	}

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
