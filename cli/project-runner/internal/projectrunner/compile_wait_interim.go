package projectrunner

import (
	"fmt"
	"io"
	"time"

	"github.com/hatayama/unity-cli-loop/common/ui"
)

const (
	compileWaitInterimIntervalDefault = 60 * time.Second
	compileWaitInterimSilentThreshold = 30 * time.Second
)

type compileWaitInterimReporter func(string)

type compileWaitInterimState struct {
	waitStartedAt     time.Time
	lastSuccessAt     time.Time
	lastSuccessStatus compileStatusResponse
	hasSuccess        bool
	lastReportAt      time.Time
	hasReported       bool
}

func newCompileWaitInterimState(waitStartedAt time.Time) compileWaitInterimState {
	return compileWaitInterimState{waitStartedAt: waitStartedAt}
}

func (state *compileWaitInterimState) noteSuccessfulPoll(completedAt time.Time, status compileStatusResponse) {
	state.lastSuccessAt = completedAt
	state.lastSuccessStatus = status
	state.hasSuccess = true
}

func (state *compileWaitInterimState) lineIfDue(now time.Time, interval time.Duration) (string, bool) {
	if interval <= 0 {
		interval = compileWaitInterimIntervalDefault
	}
	silentFor := now.Sub(state.silentAnchor())
	useSilent := silentFor >= compileWaitInterimSilentThreshold
	if !state.isDue(now, interval, useSilent, silentFor) {
		return "", false
	}
	state.hasReported = true
	state.lastReportAt = now
	if useSilent {
		return formatCompileWaitSilentLine(silentFor), true
	}
	return formatCompileWaitProgressLine(now.Sub(state.waitStartedAt), state.lastSuccessStatus), true
}

func (state *compileWaitInterimState) silentAnchor() time.Time {
	if state.hasSuccess {
		return state.lastSuccessAt
	}
	return state.waitStartedAt
}

func (state *compileWaitInterimState) isDue(
	now time.Time,
	interval time.Duration,
	useSilent bool,
	silentFor time.Duration,
) bool {
	if state.hasReported {
		return now.Sub(state.lastReportAt) >= interval
	}
	if useSilent {
		return silentFor >= compileWaitInterimSilentThreshold
	}
	if !state.hasSuccess {
		return false
	}
	return now.Sub(state.waitStartedAt) >= interval
}

func formatCompileWaitProgressLine(elapsed time.Duration, status compileStatusResponse) string {
	return fmt.Sprintf(
		"compile: still waiting for Unity (elapsed %ds; last status: is_compiling=%t, is_domain_reload_in_progress=%t).",
		int(elapsed/time.Second),
		status.IsCompiling,
		status.IsDomainReloadInProgress,
	)
}

func formatCompileWaitSilentLine(silentFor time.Duration) string {
	return fmt.Sprintf(
		"compile: Unity has not answered status polls for %ds. The Editor may be blocked by a modal dialog or stuck; if this persists, restart Unity with 'uloop launch -r'.",
		int(silentFor/time.Second),
	)
}

func compileWaitNow(deps compileWaitDeps) time.Time {
	if deps.now != nil {
		return deps.now()
	}
	return time.Now()
}

func observeCompileWaitInterim(
	state *compileWaitInterimState,
	deps compileWaitDeps,
	status compileStatusResponse,
	err error,
) {
	now := compileWaitNow(deps)
	if err == nil {
		state.noteSuccessfulPoll(now, status)
	}
	if deps.reportInterim == nil {
		return
	}
	line, due := state.lineIfDue(now, deps.interimReportInterval)
	if !due {
		return
	}
	deps.reportInterim(line)
}

func bindCompileWaitInterimReporter(stderr io.Writer, spinner *ui.TerminalSpinner, deps *compileWaitDeps) {
	stopped := false
	deps.reportInterim = func(line string) {
		if !stopped {
			spinner.Stop()
			stopped = true
		}
		_, _ = fmt.Fprintln(stderr, line)
	}
}
