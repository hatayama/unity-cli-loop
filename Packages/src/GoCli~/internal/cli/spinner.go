package cli

import (
	"fmt"
	"io"
	"os"
	"sync"
	"time"
)

const spinnerFrameInterval = 80 * time.Millisecond

var spinnerFrames = []string{"⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"}

type terminalSpinner struct {
	writer   io.Writer
	enabled  bool
	message  string
	frame    int
	done     chan struct{}
	stopped  chan struct{}
	stopOnce sync.Once
	mutex    sync.Mutex
}

func newToolSpinner(stderr io.Writer, command string) *terminalSpinner {
	return newSpinner(stderr, isTerminalWriter(stderr), "Connecting to Unity...")
}

func newSpinner(writer io.Writer, enabled bool, message string) *terminalSpinner {
	spinner := &terminalSpinner{
		writer:  writer,
		enabled: enabled,
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

func (spinner *terminalSpinner) Update(message string) {
	if !spinner.enabled {
		return
	}

	spinner.mutex.Lock()
	spinner.message = message
	spinner.mutex.Unlock()
	spinner.render()
}

func (spinner *terminalSpinner) Stop() {
	if !spinner.enabled {
		return
	}

	spinner.stopOnce.Do(func() {
		close(spinner.done)
		<-spinner.stopped
		spinner.mutex.Lock()
		defer spinner.mutex.Unlock()
		_, _ = fmt.Fprint(spinner.writer, "\r\x1b[K")
	})
}

func (spinner *terminalSpinner) run() {
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

func (spinner *terminalSpinner) render() {
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
