package clicore

import (
	"context"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityprocess/unitydialog"
)

const (
	// dialogLookDelay is how long a readiness wait goes without readiness before it first looks for a
	// dialog. Why not at once: Unity starts and reloads without a dialog most of the time, and a look
	// on macOS starts osascript.
	dialogLookDelay = 5 * time.Second
	// dialogLookInterval is the least time between two looks of one wait.
	dialogLookInterval = 10 * time.Second
)

// DialogFinder returns the modal dialogs that hold the Editor, frontmost first. An error is read as
// no dialog: the wait goes on as it would without the look.
type DialogFinder func(ctx context.Context) ([]unitydialog.Dialog, error)

// ToolReadinessBlockedByDialogError reports that a readiness wait stopped because modal dialogs hold
// the Editor, which cannot become ready until someone answers them.
type ToolReadinessBlockedByDialogError struct {
	// Dialogs is never empty, frontmost first.
	Dialogs []unitydialog.Dialog
	// Cause is the probe failure seen just before the look; it may be nil.
	Cause error
}

// Error leaves out the dialogs' text on purpose: the transport checks read words such as "EOF" in
// error text, so a dialog title would make this error read as a lost connection. There is no Unwrap
// for the same reason: Cause is usually a lost connection itself.
func (err ToolReadinessBlockedByDialogError) Error() string {
	return "a modal dialog is open in Unity; the wait for the Editor to be ready stopped"
}

// DialogLookout decides, during one readiness wait, when to look for a dialog: first once the wait
// has gone dialogLookDelay without readiness, then at most once per dialogLookInterval.
type DialogLookout struct {
	find      DialogFinder
	now       func() time.Time
	startedAt time.Time
	lastLook  time.Time
	looked    bool
}

// NewDialogLookout starts the lookout's clock. A nil find never looks; a nil now uses time.Now.
func NewDialogLookout(find DialogFinder, now func() time.Time) *DialogLookout {
	if now == nil {
		now = time.Now
	}
	return &DialogLookout{find: find, now: now, startedAt: now()}
}

// Look is called after each probe that found the Editor not ready, with that probe's failure. It
// returns ToolReadinessBlockedByDialogError when a look is due and finds a dialog, and nil otherwise.
func (lookout *DialogLookout) Look(ctx context.Context, cause error) error {
	if lookout.find == nil || !lookout.due() {
		return nil
	}
	dialogs, err := lookout.find(ctx)
	// Why a failed look is not reported: the wait goes on as it would without the look, and a
	// macOS without the permission fails every look while no dialog is open.
	if err != nil || len(dialogs) == 0 {
		return nil
	}
	return ToolReadinessBlockedByDialogError{Dialogs: dialogs, Cause: cause}
}

// due reports whether a look is due now, and when it is, counts it as made.
func (lookout *DialogLookout) due() bool {
	now := lookout.now()
	if now.Sub(lookout.startedAt) < dialogLookDelay {
		return false
	}
	if lookout.looked && now.Sub(lookout.lastLook) < dialogLookInterval {
		return false
	}
	lookout.looked = true
	lookout.lastLook = now
	return true
}
